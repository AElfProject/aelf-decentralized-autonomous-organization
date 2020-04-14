using System.Collections.Generic;
using System.Threading.Tasks;
using AElf.Sdk.CSharp;
using AElf.Types;
using Shouldly;
using Xunit;

namespace AElf.Contracts.DAOContract
{
    public partial class DaoContractTest
    {
        private const string PullRequestUrl = "https://github.com/AElfProject/AElf/pull/66666";
        private const string CommitId = "747899be26019c8207222719c4535a9f7011aab9";

        private List<BudgetPlan> BudgetPlans => new List<BudgetPlan>
        {
            new BudgetPlan
            {
                Index = 0,
                Phase = 1,
                Symbol = "ELF",
                Amount = 1000_00000000,
                ReceiverAddress = AliceAddress
            }
        };

        [Fact]
        public async Task<Hash> ProposeProjectToDAO_Test()
        {
            await InitialDAOContract();

            // Alice want to propose a project to DAO.
            var proposalId = (await AliceDAOContractStub.ProposeProjectToDAO.SendAsync(new ProposeProjectInput
            {
                PullRequestUrl = PullRequestUrl,
                CommitId = CommitId
            })).Output;

            // Check proposal exists and correct.
            var proposalInfo = await AssociationContractStub.GetProposal.CallAsync(proposalId);
            proposalInfo.ContractMethodName.ShouldBe(nameof(DAOContractStub.AddInvestmentProject));
            proposalInfo.ToAddress.ShouldBe(DAOContractAddress);
            proposalInfo.Proposer.ShouldBe(DAOContractAddress);

            return proposalId;
        }

        [Fact]
        public async Task<Hash> ProposeProjectToDAO_Approve_Test()
        {
            var proposalId = await ProposeProjectToDAO_Test();
            var projectId = Hash.FromString(CommitId.Append(PullRequestUrl));

            await DAOApproveAsync(proposalId);
            // Anyone call this method to release this proposal.
            await DAOContractStub.ReleaseProposal.SendAsync(new ReleaseProposalInput
            {
                ProjectId = projectId,
                ProposalId = proposalId
            });

            // Check project info.
            var projectInfo = await DAOContractStub.GetProjectInfo.CallAsync(projectId);
            projectInfo.PullRequestUrl.ShouldBe(PullRequestUrl);
            projectInfo.CommitId.ShouldBe(CommitId);
            projectInfo.VirtualAddress.ShouldNotBeNull();

            return projectId;
        }

        [Fact]
        public async Task<Hash> ProposeProjectToParliament_Test()
        {
            var projectId = await ProposeProjectToDAO_Approve_Test();

            // After approved by DAO, Alice propose this project to Parliament.
            var result = await AliceDAOContractStub.ProposeProjectToParliament.SendAsync(
                new ProposeProjectWithBudgetsInput
                {
                    PullRequestUrl = PullRequestUrl,
                    CommitId = CommitId,
                    BudgetPlans = {BudgetPlans}
                });
            var proposalId = result.Output;

            await ParliamentApproveAsync(proposalId);

            // Anyone call this method to release this proposal.
            await DAOContractStub.ReleaseProposal.SendAsync(new ReleaseProposalInput
            {
                ProjectId = projectId,
                ProposalId = proposalId,
                IsParliamentProposal = true
            });

            // Check project info.
            var projectInfo = await DAOContractStub.GetProjectInfo.CallAsync(projectId);
            projectInfo.PullRequestUrl.ShouldBe(PullRequestUrl);
            projectInfo.CommitId.ShouldBe(CommitId);
            projectInfo.Status.ShouldBe(ProjectStatus.Approved);
            projectInfo.ProfitSchemeId.ShouldNotBeNull();
            projectInfo.BudgetPlans.ShouldBe(BudgetPlans);
            projectInfo.VirtualAddress.ShouldNotBeNull();

            return projectId;
        }

        [Fact]
        public async Task InvestToInvestmentProjectTest()
        {
            var projectId = await ProposeProjectToParliament_Test();
            await AliceDAOContractStub.Invest.SendAsync(new InvestInput
            {
                ProjectId = projectId,
                Symbol = "ELF",
                Amount = 1000_00000000
            });
            
        }
    }
}