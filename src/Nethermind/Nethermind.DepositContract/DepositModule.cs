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
// 

using System;
using System.IO;
using System.Threading.Tasks;
using Nethermind.Abi;
using Nethermind.Blockchain.Contracts.Json;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Extensions;
using Nethermind.Facade;
using Nethermind.JsonRpc;
using Nethermind.Logging;
using Nethermind.TxPool;

namespace Nethermind.DepositContract
{
    public class DepositModule : IDepositModule
    {
        private readonly ITxPoolBridge _txPoolBridge;
        private readonly ILogger _logger;
        private AbiDefinition _abiDefinition;
        
        private AbiDefinitionParser _parser = new AbiDefinitionParser();

        public DepositModule(ITxPoolBridge txPoolBridge, ILogManager logManager)
        {
            _logger = logManager?.GetClassLogger<DepositModule>() ?? throw new ArgumentNullException(nameof(logManager));
            _txPoolBridge = txPoolBridge ?? throw new ArgumentNullException(nameof(txPoolBridge));
            _abiDefinition = _parser.Parse(File.ReadAllText("contracts/validator_registration.json"));
        }

        public ValueTask<ResultWrapper<Keccak>> deposit_deploy(Address senderAddress)
        {
            Transaction tx = new Transaction();
            tx.Value = 0;
            tx.Init = _abiDefinition.Bytecode;
            tx.GasLimit = 2000000;
            tx.GasPrice = 20.GWei();
            tx.SenderAddress = senderAddress;

            Keccak txHash = _txPoolBridge.SendTransaction(tx, TxHandlingOptions.ManagedNonce);

            if(_logger.IsInfo) _logger.Info($"Sent transaction at price {tx.GasPrice} to {tx.SenderAddress}");

            return new ValueTask<ResultWrapper<Keccak>>(ResultWrapper<Keccak>.Success(txHash));
        }

        public ValueTask<ResultWrapper<Keccak>> deposit_test()
        {
            var result = ResultWrapper<Keccak>.Fail("not implemented", ErrorCodes.InternalError);
            return new ValueTask<ResultWrapper<Keccak>>(result);
        }
    }
}