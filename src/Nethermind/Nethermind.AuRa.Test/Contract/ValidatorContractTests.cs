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
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FluentAssertions.Common;
using Nethermind.Abi;
using Nethermind.Consensus.AuRa.Contracts;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Specs.ChainSpecStyle;
using Nethermind.Core.Test;
using Nethermind.Core.Test.Builders;
using Nethermind.Dirichlet.Numerics;
using Nethermind.Evm;
using Nethermind.Evm.Tracing;
using Nethermind.Store;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NSubstitute.ReceivedExtensions;
using NUnit.Framework;

namespace Nethermind.AuRa.Test.Contract
{
    [TestFixture]
    public class ValidatorContractTests
    {
        private Block _block;
        private readonly Address _contractAddress = Address.FromNumber(long.MaxValue);
        private ITransactionProcessor _transactionProcessor;

        [SetUp]
        public void SetUp()
        {
            _block = new Block(Build.A.BlockHeader.TestObject, new BlockBody());
            _transactionProcessor = Substitute.For<ITransactionProcessor>();
        }

        [Test]
        public void constructor_throws_ArgumentNullException_on_null_encoder()
        {
            Action action = () => new ValidatorContract(_transactionProcessor, null, _contractAddress);
            action.Should().Throw<ArgumentNullException>();
        }
        
        [Test]
        public void constructor_throws_ArgumentNullException_on_null_contractAddress()
        {
            Action action = () => new ValidatorContract(_transactionProcessor, new AbiEncoder(), null);
            action.Should().Throw<ArgumentNullException>();
        }
        
        [Test]
        public void finalize_change_should_call_correct_transaction()
        {
            var expectation = new Transaction()
            {
                Value = 0, 
                Data = new byte[] {0x75, 0x28, 0x62, 0x11},
                Hash = new Keccak("0xa43db3bf833748d98eb99453bc933f313f9f6a7471fed0018190f0d5b0f863a1"), 
                To = _contractAddress,
                SenderAddress = Address.SystemUser,
                GasLimit = long.MaxValue,
                GasPrice = 0,
                Nonce = 0
            };
            
            var contract = new ValidatorContract(_transactionProcessor, new AbiEncoder(), _contractAddress);
            
            contract.FinalizeChange(_block.Header);
            
            _transactionProcessor.Received().Execute(Arg.Is<Transaction>(t => IsEquivalentTo(t, expectation)), _block.Header, Arg.Any<ITxTracer>());
        }
        
        private static bool IsEquivalentTo(object expected, object item)
        {
            try
            {
                item.Should().BeEquivalentTo(expected);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}