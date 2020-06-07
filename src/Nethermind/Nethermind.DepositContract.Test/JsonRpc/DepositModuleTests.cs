using System.IO;
using System.IO.Abstractions;
using System.Security;
using System.Threading.Tasks;
using FluentAssertions;
using Nethermind.Abi;
using Nethermind.Core;
using Nethermind.Core.Extensions;
using Nethermind.Core.Test.Builders;
using Nethermind.Evm;
using Nethermind.JsonRpc.Test.Modules;
using Nethermind.Logging;
using Nethermind.Specs;
using Nethermind.Specs.Forks;
using NSubstitute;
using NUnit.Framework;

namespace Nethermind.DepositContract.Test.JsonRpc
{
    [TestFixture]
    public class BaselineModuleTests
    {
        private IFileSystem _fileSystem;

        [SetUp]
        public void SetUp()
        {
            _fileSystem = Substitute.For<IFileSystem>();
            const string expectedFilePath = "contracts/validator_registration.json";
            _fileSystem.File.ReadAllLinesAsync(expectedFilePath).Returns(File.ReadAllLines(expectedFilePath));
            _fileSystem.File.ReadAllText(expectedFilePath).Returns(File.ReadAllText(expectedFilePath));
        }

        [Test]
        public async Task deploy_deploys_the_contract()
        {
            var spec = new SingleReleaseSpecProvider(ConstantinopleFix.Instance, 1);
            TestRpcBlockchain testRpc = await TestRpcBlockchain.ForTest(SealEngineType.NethDev).Build(spec);
            testRpc.TestWallet.UnlockAccount(TestItem.Addresses[0], new SecureString());
            await testRpc.AddFunds(TestItem.Addresses[0], 1.Ether());
            
            DepositModule depositModule = new DepositModule(
                testRpc.TxPoolBridge,
                LimboLogs.Instance);
            
            var result = await depositModule.deposit_deploy(TestItem.Addresses[0]);
            result.Data.Should().NotBe(null);
            result.ErrorCode.Should().Be(0);
            result.Result.Error.Should().BeNull();
            result.Result.ResultType.Should().Be(ResultType.Success);
            
            await testRpc.AddBlock();

            testRpc.BlockTree.Head.Number.Should().Be(5);
            testRpc.BlockTree.Head.Transactions.Should().Contain(tx => tx.IsContractCreation);

            var code = testRpc.StateReader
                .GetCode(testRpc.BlockTree.Head.StateRoot, ContractAddress.From(TestItem.Addresses[0], 0));

            code.Should().NotBeEmpty();
        }
    }
}