using System.Linq;
using Acs3;
using AElf.Contracts.Profit;
using AElf.CSharp.Core;
using AElf.CSharp.Core.Extension;
using AElf.Sdk.CSharp;
using AElf.Types;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;

namespace AElf.Contracts.DAOContract
{
    // ReSharper disable InconsistentNaming
    public partial class DAOContract
    {
        private void CreateProposalToAssociationContractAndRelease(string methodName, ByteString parameter)
        {
            var createProposalInput = new CreateProposalInput
            {
                ContractMethodName = methodName,
                Params = parameter,
                OrganizationAddress = State.OrganizationAddress.Value,
                ExpiredTime = Context.CurrentBlockTime.AddHours(1),
                ToAddress = State.AssociationContract.Value
            };
            State.AssociationContract.CreateProposal.Send(createProposalInput);
            // TODO: Association Contract need to help calculating proposal id.
            var proposalId = State.AssociationContract.CreateProposal.Call(createProposalInput);
            State.AssociationContract.Approve.Send(proposalId);
            State.AssociationContract.Release.Send(proposalId);
        }

        private Hash CreateProposalToParliament(string methodName, ByteString parameter)
        {
            var createProposalInput = new CreateProposalInput
            {
                ContractMethodName = methodName,
                Params = parameter,
                OrganizationAddress = State.ParliamentDefaultAddress.Value,
                ExpiredTime = Context.CurrentBlockTime.AddHours(1),
                ToAddress = Context.Self
            };
            State.ParliamentContract.CreateProposal.Send(createProposalInput);
            var proposalId = State.ParliamentContract.CreateProposal.Call(createProposalInput);
            return proposalId;
        }

        private Hash CreateProposalToSelf(string methodName, ByteString parameter)
        {
            var createProposalInput = new CreateProposalInput
            {
                ContractMethodName = methodName,
                Params = parameter,
                OrganizationAddress = State.OrganizationAddress.Value,
                ExpiredTime = Context.CurrentBlockTime.AddHours(1),
                ToAddress = Context.Self
            };
            State.AssociationContract.CreateProposal.Send(createProposalInput);
            var proposalId = State.AssociationContract.CreateProposal.Call(createProposalInput);
            return proposalId;
        }

        private Hash CreateProposalToDeveloperOrganization(Address developerOrganizationAddress, string methodName,
            ByteString parameter)
        {
            var createProposalInput = new CreateProposalInput
            {
                ContractMethodName = methodName,
                Params = parameter,
                OrganizationAddress = developerOrganizationAddress,
                ExpiredTime = Context.CurrentBlockTime.AddHours(1),
                ToAddress = Context.Self
            };
            State.AssociationContract.CreateProposal.Send(createProposalInput);
            var proposalId = State.AssociationContract.CreateProposal.Call(createProposalInput);
            return proposalId;
        }

        private void AssertReleasedByParliament()
        {
            if (State.ParliamentContract.Value == null)
                State.ParliamentContract.Value =
                    Context.GetContractAddressByName(SmartContractConstants.ParliamentContractSystemName);
            var defaultAddress = State.ParliamentContract.GetDefaultOrganizationAddress.Call(new Empty());
            Assert(Context.Sender == defaultAddress, "No permission.");
        }

        private void AdjustApprovalThreshold()
        {
            State.ApprovalThreshold.Value = State.DAOMemberList.Value.Value.Count.Mul(2).Div(3).Add(1);
        }

        private void AssertApprovalCountMeetDAOThreshold(long approvalCount)
        {
            Assert(approvalCount >= State.ApprovalThreshold.Value, "Not approved by DAO members yet.");
        }

        private void AssertApprovalCountMeetDeveloperOrganizationThreshold(Hash proposalId, int developerCount)
        {
            var proposalInfo = State.AssociationContract.GetProposal.Call(proposalId);
            // Allow one developer not approve.
            Assert(proposalInfo.ApprovalCount >= developerCount.Sub(1), "Not approved by other developers");
        }

        private Hash CreateProfitScheme(ProjectInfo projectInfo)
        {
            State.ProfitContract.CreateScheme.Send(new CreateSchemeInput
            {
                Manager = projectInfo.VirtualAddress,
                IsReleaseAllBalanceEveryTimeByDefault = true,
                CanRemoveBeneficiaryDirectly = true
            });

            var profitSchemeId = State.ProfitContract.CreateScheme.Call(new CreateSchemeInput
            {
                Manager = projectInfo.VirtualAddress,
                IsReleaseAllBalanceEveryTimeByDefault = true,
                CanRemoveBeneficiaryDirectly = true
            });

            return profitSchemeId;
        }

        private void AddBeneficiaries(ProjectInfo projectInfo)
        {
            foreach (var budgetPlan in projectInfo.BudgetPlans)
            {
                Context.SendVirtualInline(projectInfo.GetProjectId(), State.ProfitContract.Value,
                    nameof(State.ProfitContract.AddBeneficiary), new AddBeneficiaryInput
                    {
                        SchemeId = projectInfo.ProfitSchemeId,
                        EndPeriod = projectInfo.BudgetPlans.Count,
                        BeneficiaryShare = new BeneficiaryShare
                        {
                            Beneficiary = budgetPlan.ReceiverAddress,
                            Shares = 1
                        }
                    }.ToByteString());
            }
        }

        private void PayBudget(ProjectInfo projectInfoIsState, ProjectInfo inputProjectInfo)
        {
            var projectId = inputProjectInfo.GetProjectId();
            var budgetPlan =
                projectInfoIsState.BudgetPlans.Single(p => p.Index == inputProjectInfo.CurrentBudgetPlanIndex);
            var inputBudgetPlan =
                inputProjectInfo.BudgetPlans.Single(p => p.Index == inputProjectInfo.CurrentBudgetPlanIndex);
            Assert(budgetPlan.PaidInAmount == budgetPlan.Amount, "Budget not ready.");
            Context.SendVirtualInline(projectId, State.ProfitContract.Value,
                nameof(State.ProfitContract.ContributeProfits), new ContributeProfitsInput
                {
                    SchemeId = projectInfoIsState.ProfitSchemeId,
                    Symbol = budgetPlan.Symbol,
                    Amount = budgetPlan.Amount
                });
            Context.SendVirtualInline(projectId, State.ProfitContract.Value,
                nameof(State.ProfitContract.DistributeProfits), new DistributeProfitsInput
                {
                    SchemeId = projectInfoIsState.ProfitSchemeId,
                    Period = projectInfoIsState.CurrentBudgetPlanIndex.Add(1),
                    AmountsMap = {{budgetPlan.Symbol, budgetPlan.Amount}}
                });

            // Update Budget Plan.
            budgetPlan.DeliverPullRequestUrl = inputBudgetPlan.DeliverPullRequestUrl;
            budgetPlan.DeliverCommitId = inputBudgetPlan.DeliverCommitId;
        }

        private void CheckProjectProposalCanBeReleased(Hash projectId)
        {
            Assert(State.CanBeReleased[projectId], "Not ready to release any proposal.");
            State.CanBeReleased.Remove(projectId);
        }

        private void CheckBudgetPlans(RepeatedField<BudgetPlan> budgetPlans)
        {
            // TODO: Some checks about BudgetPlans, like correctness of indices and phases.
        }

        private Hash ProposeToAddProject(string pullRequestUrl, string commitId, ProjectType projectType)
        {
            var projectInfo = new ProjectInfo
            {
                PullRequestUrl = pullRequestUrl,
                CommitId = commitId,
                // Initial status of an investment project.
                Status = ProjectStatus.Proposed,
                ProjectType = projectType
            };
            var projectId = projectInfo.GetProjectId();
            Assert(State.Projects[projectId] == null, "Project already proposed successfully before.");
            var proposalId = CreateProposalToSelf(nameof(AddProject), projectInfo.ToByteString());
            State.PreviewProposalIds[projectId] = proposalId;
            return proposalId;
        }

        private Hash ProposedToUpdateProjectWithBudgetPlans(ProposeProjectWithBudgetsInput input,
            ProjectType projectType)
        {
            var projectInfo = State.Projects[input.ProjectId];
            Assert(projectInfo != null, "Project not found.");
            if (projectType == ProjectType.Reward)
            {
                foreach (var budgetPlan in input.BudgetPlans)
                {
                    budgetPlan.ReceiverAddress = null;
                }
            }
            var proposalId = CreateProposalToParliament(
                projectType == ProjectType.Investment ? nameof(UpdateInvestmentProject) : nameof(UpdateRewardProject),
                new ProjectInfo
                {
                    // ReSharper disable once PossibleNullReferenceException
                    PullRequestUrl = projectInfo.PullRequestUrl,
                    CommitId = projectInfo.CommitId,
                    Status = ProjectStatus.Approved,
                    BudgetPlans = {input.BudgetPlans}
                }.ToByteString());
            return proposalId;
        }
    }
}