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

using System.Threading.Tasks;

namespace Nethermind.Blockchain.Synchronization.TotalSync
{
    public class BlockDownloaderFeed : SyncFeed<BlocksRequest>
    {
        private BlocksRequest _request = new BlocksRequest();
        
        public BlockDownloaderFeed(DownloaderOptions options, int numberOfLatestBlocksToIgnore)
        {
            _request.Options = options;
            _request.NumberOfLatestBlocksToBeIgnored = numberOfLatestBlocksToIgnore;
        }
        
        public BlockDownloaderFeed(DownloaderOptions options)
        {
            _request.Options = options;
        }
        
        public override Task<BlocksRequest> PrepareRequest()
        {
            return Task.FromResult(_request);
        }

        public override SyncBatchResponseHandlingResult HandleResponse(BlocksRequest response)
        {
            ChangeState(SyncFeedState.Dormant);
            return SyncBatchResponseHandlingResult.OK;
        }

        public override bool IsMultiFeed => false;

        public override void Activate()
        {
            ChangeState(SyncFeedState.Active);
        }
    }
}