//  Copyright (c) 2018 Demerzel Solutions Limited
//  This file is part of the Nethermind library.
// 
//  The Nethermind library is free software: you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
// 
//  The Nethermind library is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
//  GNU Lesser General Public License for more details.
// 
//  You should have received a copy of the GNU Lesser General Public License
//  along with the Nethermind. If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Nethermind.Blockchain.Receipts;
using Nethermind.Blockchain.Synchronization.FastSync;
using Nethermind.Blockchain.Synchronization.TotalSync;
using Nethermind.Blockchain.Validators;
using Nethermind.Consensus;
using Nethermind.Core;
using Nethermind.Core.Specs;
using Nethermind.Logging;
using Nethermind.Stats;

namespace Nethermind.Blockchain.Synchronization
{
    public class Synchronizer : ISynchronizer
    {
        private const int SyncTimerInterval = 1000;

        private readonly ILogger _logger;
        private readonly ISpecProvider _specProvider;
        private readonly IBlockTree _blockTree;
        private readonly IReceiptStorage _receiptStorage;
        private readonly IBlockValidator _blockValidator;
        private readonly ISealValidator _sealValidator;
        private readonly ISyncConfig _syncConfig;
        private readonly IEthSyncPeerPool _syncPeerPool;
        private readonly INodeDataDownloader _nodeDataDownloader;
        private readonly INodeStatsManager _nodeStatsManager;
        private readonly ILogManager _logManager;
        private readonly ISyncReport _syncReport;

        private BlockDownloaderFeed _fullSyncBlockDownloaderFeed;
        private BlockDownloader _fullSyncBlockDownloader;

        private BlockDownloaderFeed _fastSyncBlockDownloaderFeed;
        private BlockDownloader _fastSyncBlockDownloader;

        private CancellationTokenSource _fullSyncCancellation = new CancellationTokenSource();
        private System.Timers.Timer _syncTimer;
        private ISyncModeSelector _syncMode;
        private Task _syncLoopTask;

        /* sync events are used mainly for managing sync peers reputation */
        public event EventHandler<SyncEventArgs> SyncEvent;

        private CancellationTokenSource _fastBlocksCancellation;
        private SyncProgressResolver _syncProgressResolver;

        public Synchronizer(
            ISpecProvider specProvider,
            IBlockTree blockTree,
            IReceiptStorage receiptStorage,
            IBlockValidator blockValidator,
            ISealValidator sealValidator,
            IEthSyncPeerPool peerPool,
            ISyncConfig syncConfig,
            INodeDataDownloader nodeDataDownloader,
            INodeStatsManager nodeStatsManager,
            ILogManager logManager)
        {
            _logger = logManager?.GetClassLogger() ?? throw new ArgumentNullException(nameof(logManager));
            _specProvider = specProvider ?? throw new ArgumentNullException(nameof(specProvider));
            _blockTree = blockTree ?? throw new ArgumentNullException(nameof(blockTree));
            _receiptStorage = receiptStorage ?? throw new ArgumentNullException(nameof(receiptStorage));
            _blockValidator = blockValidator ?? throw new ArgumentNullException(nameof(blockValidator));
            _sealValidator = sealValidator ?? throw new ArgumentNullException(nameof(sealValidator));
            _syncConfig = syncConfig ?? throw new ArgumentNullException(nameof(syncConfig));
            _syncPeerPool = peerPool ?? throw new ArgumentNullException(nameof(peerPool));
            _nodeDataDownloader = nodeDataDownloader ?? throw new ArgumentNullException(nameof(nodeDataDownloader));
            _nodeStatsManager = nodeStatsManager ?? throw new ArgumentNullException(nameof(nodeStatsManager));
            _logManager = logManager;

            _syncProgressResolver = new SyncProgressResolver(_blockTree, receiptStorage, _nodeDataDownloader, syncConfig, logManager);
            _syncMode = new SyncModeSelector(_syncProgressResolver, _syncPeerPool, _syncConfig, logManager);
            _syncReport = new SyncReport(_syncPeerPool, _nodeStatsManager, syncConfig, _syncProgressResolver, _syncMode, logManager);
            _syncPeerPool.PeerAdded += (sender, args) => RequestSynchronization(SyncTriggerType.PeerAdded);
        }

        private void StartFullSyncComponents()
        {
            _fullSyncBlockDownloaderFeed = new BlockDownloaderFeed(DownloaderOptions.WithBodies);
            _fullSyncBlockDownloader = new BlockDownloader(_fullSyncBlockDownloaderFeed, _syncPeerPool, _blockTree, _blockValidator, _sealValidator, _syncReport, _receiptStorage, _specProvider, _logManager);
            _fullSyncBlockDownloader.Start(_fullSyncCancellation.Token).ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    _logger.Error("Full sync block downloader failed", t.Exception);
                }
            });
        }

        public SyncMode SyncMode => _syncMode.Current;

        public event EventHandler<SyncModeChangedEventArgs> SyncModeChanged
        {
            add => _syncMode.Changed += value;
            remove => _syncMode.Changed -= value;
        }

        public void Start()
        {
            // StartFullSyncComponents();
            if (_syncConfig.FastBlocks)
            {
                StartFastSyncComponents();
            }
            
            StartSyncTimer();
        }

        private void StartFastSyncComponents()
        {
            FastBlockPeerSelectionStrategyFactory fastFactory = new FastBlockPeerSelectionStrategyFactory();
            _fastBlocksCancellation = new CancellationTokenSource();

            FastHeadersSyncFeed headersSyncFeed = new FastHeadersSyncFeed(_blockTree, _syncPeerPool, _syncConfig, _syncReport, _logManager);
            var fastBlockDownloader = new HeadersSyncExecutor(headersSyncFeed, _syncPeerPool, fastFactory, _logManager);
            Task headersTask = fastBlockDownloader.Start(_fastBlocksCancellation.Token).ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    _logger.Error("Fast blocks headers downloader failed", t.Exception);
                }
            });
            
            headersSyncFeed.Activate();

            FastBodiesSyncFeed bodiesSyncFeed = new FastBodiesSyncFeed(_blockTree, _syncPeerPool, _syncConfig, _syncReport, _logManager);
            var bodiesDownloader = new BodiesSyncExecutor(bodiesSyncFeed, _syncPeerPool, fastFactory, _logManager);
            Task bodiesTask = bodiesDownloader.Start(_fastBlocksCancellation.Token).ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    _logger.Error("Fast blocks bodies downloader failed", t.Exception);
                }
            });
            
            bodiesSyncFeed.Activate();

            FastReceiptsSyncFeed receiptsSyncFeed = new FastReceiptsSyncFeed(_specProvider, _blockTree, _receiptStorage, _syncPeerPool, _syncConfig, _syncReport, _logManager);
            var receiptsDownloader = new ReceiptsSyncExecutor(receiptsSyncFeed, _syncPeerPool, fastFactory, _logManager);
            Task receiptsTask = receiptsDownloader.Start(_fastBlocksCancellation.Token).ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    _logger.Error("Fast blocks receipts downloader failed", t.Exception);
                }
            });
            
            receiptsSyncFeed.Activate();

            DownloaderOptions options = BuildFastSyncOptions();
            _fastSyncBlockDownloaderFeed = new BlockDownloaderFeed(options, _syncConfig.BeamSync ? 0 : SyncModeSelector.FullSyncThreshold);
            _fastSyncBlockDownloader = new BlockDownloader(_fastSyncBlockDownloaderFeed, _syncPeerPool, _blockTree, _blockValidator, _sealValidator, _syncReport, _receiptStorage, _specProvider, _logManager);
            _fastSyncBlockDownloader.Start(_fullSyncCancellation.Token).ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    _logger.Error("Fast sync block downloader failed", t.Exception);
                }
            });
        }

        private DownloaderOptions BuildFastSyncOptions()
        {
            DownloaderOptions options = DownloaderOptions.MoveToMain;
            if (_syncConfig.DownloadReceiptsInFastSync)
            {
                options |= DownloaderOptions.WithReceipts;
            }

            if (_syncConfig.DownloadBodiesInFastSync)
            {
                options |= DownloaderOptions.WithBodies;
            }

            return options;
        }

        public async Task StopAsync()
        {
            StopSyncTimer();

            _fullSyncCancellation?.Cancel();

            await (_syncLoopTask ?? Task.CompletedTask);

            if (_logger.IsInfo) _logger.Info("Sync stopped");
        }

        /// <summary>
        /// Notifies synchronizer that an event occured that should trigger synchronization
        /// at the nearest convenient time.
        /// </summary>
        /// <param name="syncTriggerType">Reason for the synchronization request for logging</param>
        public void RequestSynchronization(SyncTriggerType syncTriggerType)
        {
            if (!_blockTree.CanAcceptNewBlocks)
            {
                return;
            }

            if (_logger.IsDebug)
            {
                string message = $"Requesting synchronization [{syncTriggerType.ToString().ToUpperInvariant()}]";
                if (syncTriggerType == SyncTriggerType.SyncTimer)
                {
                    _logger.Trace(message);
                }
                else
                {
                    _logger.Debug(message);
                }
            }

            // do this depending on the SyncMode!
            if (_syncProgressResolver.FindBestHeader() != 0)
            {
                _fastSyncBlockDownloaderFeed?.Activate();
                _fullSyncBlockDownloaderFeed?.Activate();
            }
        }

        private void StartSyncTimer()
        {
            if (_logger.IsDebug) _logger.Debug($"Starting sync timer with interval of {SyncTimerInterval}ms");
            _syncTimer = new System.Timers.Timer(SyncTimerInterval);
            _syncTimer.Elapsed += SyncTimerOnElapsed;
            _syncTimer.Start();
        }

        private void StopSyncTimer()
        {
            try
            {
                if (_logger.IsDebug) _logger.Debug("Stopping sync timer");
                _syncTimer?.Stop();
            }
            catch (Exception e)
            {
                if (_logger.IsError) _logger.Error("Error during the sync timer stop", e);
            }
        }

        private void SyncTimerOnElapsed(object sender, ElapsedEventArgs e)
        {
            _syncTimer.Enabled = false;

            try
            {
                _syncMode.Update();
                RequestSynchronization(SyncTriggerType.SyncTimer);
            }
            catch (Exception ex)
            {
                if (_logger.IsDebug) _logger.Error("Sync timer failed", ex);
            }
            finally
            {
                _syncTimer.Enabled = true;
            }
        }

        private async Task<long> DownloadStateNodes(CancellationToken cancellation)
        {
            BlockHeader bestSuggested = _blockTree.BestSuggestedHeader;
            if (bestSuggested == null)
            {
                return 0;
            }

            if (_logger.IsInfo) _logger.Info($"Starting the node data sync from the {bestSuggested.ToString(BlockHeader.Format.Short)} {bestSuggested.StateRoot} root");
            return await _nodeDataDownloader.SyncNodeData(cancellation, bestSuggested.Number, bestSuggested.StateRoot);
        }

        public void Dispose()
        {
            _fastBlocksCancellation?.Cancel();
            _syncTimer?.Dispose();
            _syncLoopTask?.Dispose();
            _fullSyncCancellation?.Dispose();
            _syncReport?.Dispose();
        }
    }
}