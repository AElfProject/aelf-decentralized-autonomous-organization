using System.Threading.Tasks;
using Acs3;
using AElf.Contracts.DAOContract;
using AElf.CSharp.Core.Extension;
using AElf.Kernel;
using AElf.Types;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Shouldly;
using Xunit;

namespace AElf.Contracts.DAOContract
{
    // ReSharper disable InconsistentNaming
    public class DaoContractTest : DAOContractTestBase
    {
        private async Task InitialDAOContract()
        {
            await DAOContractStub.Initialize.SendAsync(new InitializeInput
            {
                DepositAmount = 10_00000000
            });
        }

        [Fact]
        public async Task DAOManagementTest()
        {
            await InitialDAOContract();

            var aliceParliamentStub = GetParliamentContractStub(AliceKeyPair);

            // Alice want to join DAO.
            {
                var proposalId = (await aliceParliamentStub.CreateProposal.SendAsync(new CreateProposalInput
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
                await aliceParliamentStub.Release.SendAsync(proposalId);

                // Check DAO member list.
                var memberList = (await DAOContractStub.GetDAOMemberList.CallAsync(new Empty())).Value;
                memberList.ShouldContain(AliceAddress);
            }

            // Alice want to quit DAO.
            {
                var proposalId = (await aliceParliamentStub.CreateProposal.SendAsync(new CreateProposalInput
                {
                    OrganizationAddress = ParliamentDefaultOrganizationAddress,
                    ContractMethodName = nameof(DAOContractStub.Quit),
                    ExpiredTime = TimestampHelper.GetUtcNow().AddHours(1),
                    Params = new StringValue
                    {
                        Value = AliceKeyPair.PublicKey.ToHex()
                    }.ToByteString(),
                    ToAddress = DAOContractAddress
                })).Output;

                await ParliamentApproveAsync(proposalId);
                await aliceParliamentStub.Release.SendAsync(proposalId);
                
                // Check DAO member list.
                var memberList = (await DAOContractStub.GetDAOMemberList.CallAsync(new Empty())).Value;
                memberList.ShouldNotContain(AliceAddress);
            }
        }
    }
}