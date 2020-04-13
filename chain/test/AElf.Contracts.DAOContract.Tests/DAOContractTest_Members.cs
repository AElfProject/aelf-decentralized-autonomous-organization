using System.Threading.Tasks;
using Acs3;
using AElf.Contracts.MultiToken;
using AElf.Contracts.Parliament;
using AElf.CSharp.Core.Extension;
using AElf.Kernel;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Shouldly;
using Xunit;

namespace AElf.Contracts.DAOContract
{
    // ReSharper disable InconsistentNaming
    public partial class DaoContractTest : DAOContractTestBase
    {
        private const long DepositAmount = 10_00000000;

        private ParliamentContractContainer.ParliamentContractStub AliceParliamentStub =>
            GetParliamentContractStub(AliceKeyPair);

        private DAOContractContainer.DAOContractStub AliceDAOContractStub => GetDAOContractStub(AliceKeyPair);
        private TokenContractContainer.TokenContractStub AliceTokenContractStub => GetTokenContractStub(AliceKeyPair);

        private async Task InitialDAOContract()
        {
            await DAOContractStub.Initialize.SendAsync(new InitializeInput
            {
                DepositAmount = DepositAmount
            });
        }

        [Fact]
        public async Task DAOManagementTest_Join()
        {
            await InitialDAOContract();

            // Alice want to join DAO.
            // First approve.
            var balanceBefore = (await AliceTokenContractStub.GetBalance.CallAsync(new GetBalanceInput
            {
                Owner = AliceAddress,
                Symbol = "ELF"
            })).Balance;
            await AliceTokenContractStub.Approve.SendAsync(new ApproveInput
            {
                Spender = DAOContractAddress,
                Symbol = "ELF",
                Amount = DepositAmount
            });
            var proposalId = (await AliceParliamentStub.CreateProposal.SendAsync(new CreateProposalInput
            {
                OrganizationAddress = ParliamentDefaultOrganizationAddress,
                ContractMethodName = nameof(DAOContractStub.ProposeJoin),
                ExpiredTime = TimestampHelper.GetUtcNow().AddHours(1),
                Params = new StringValue
                {
                    Value = AliceKeyPair.PublicKey.ToHex()
                }.ToByteString(),
                ToAddress = DAOContractAddress
            })).Output;
            await ParliamentApproveAsync(proposalId);
            await AliceParliamentStub.Release.SendAsync(proposalId);

            // Check DAO member list.
            var memberList = (await DAOContractStub.GetDAOMemberList.CallAsync(new Empty())).Value;
            memberList.ShouldContain(AliceAddress);

            var balanceAfter = (await AliceTokenContractStub.GetBalance.CallAsync(new GetBalanceInput
            {
                Owner = AliceAddress,
                Symbol = "ELF"
            })).Balance;
            (balanceBefore - balanceAfter).ShouldBe(DepositAmount);
        }

        [Fact]
        public async Task DAOManagementTest_Quit()
        {
            await DAOManagementTest_Join();

            var balanceBefore = (await AliceTokenContractStub.GetBalance.CallAsync(new GetBalanceInput
            {
                Owner = AliceAddress,
                Symbol = "ELF"
            })).Balance;

            await AliceDAOContractStub.Quit.SendAsync(new Empty());

            // Check DAO member list.
            var memberList = (await DAOContractStub.GetDAOMemberList.CallAsync(new Empty())).Value;
            memberList.ShouldNotContain(AliceAddress);

            var balanceAfter = (await AliceTokenContractStub.GetBalance.CallAsync(new GetBalanceInput
            {
                Owner = AliceAddress,
                Symbol = "ELF"
            })).Balance;
            (balanceAfter - balanceBefore).ShouldBe(DepositAmount);
        }

        [Fact]
        public async Task DAOManagementTest_Expel()
        {
            await DAOManagementTest_Join();
            
            var balanceBefore = (await AliceTokenContractStub.GetBalance.CallAsync(new GetBalanceInput
            {
                Owner = AliceAddress,
                Symbol = "ELF"
            })).Balance;

            var proposalId = (await AliceParliamentStub.CreateProposal.SendAsync(new CreateProposalInput
            {
                OrganizationAddress = ParliamentDefaultOrganizationAddress,
                ContractMethodName = nameof(DAOContractStub.ProposeExpel),
                ExpiredTime = TimestampHelper.GetUtcNow().AddHours(1),
                Params = new StringValue
                {
                    Value = AliceKeyPair.PublicKey.ToHex()
                }.ToByteString(),
                ToAddress = DAOContractAddress
            })).Output;
            await ParliamentApproveAsync(proposalId);
            await AliceParliamentStub.Release.SendAsync(proposalId);
            
            // Check DAO member list.
            var memberList = (await DAOContractStub.GetDAOMemberList.CallAsync(new Empty())).Value;
            memberList.ShouldNotContain(AliceAddress);

            var balanceAfter = (await AliceTokenContractStub.GetBalance.CallAsync(new GetBalanceInput
            {
                Owner = AliceAddress,
                Symbol = "ELF"
            })).Balance;
            balanceAfter.ShouldBe(balanceBefore);
        }
    }
}