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
        private Hash CreateProposalToAssociationContractAndRelease(string methodName, ByteString parameter)
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

            return proposalId;
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

        private void AssertApprovedByDecentralizedAutonomousOrganization(ProjectInfo projectInfo)
        {
            var projectId = projectInfo.GetProjectId();
            var proposalId = State.PreviewProposalIds[projectId];
            var approvalCount = State.AssociationContract.GetProposal.Call(proposalId).ApprovalCount;
            AssertApprovalCountMeetThreshold(approvalCount);
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

        private void AssertApprovalCountMeetThreshold(long approvalCount)
        {
            Assert(approvalCount >= State.ApprovalThreshold.Value, "Not approved by DAO members yet.");
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

            foreach (var budgetPlan in projectInfo.BudgetPlans)
            {
                Context.SendVirtualInline(projectInfo.GetProjectId(), State.ProfitContract.Value,
                    nameof(State.ProfitContract.AddBeneficiary), new AddBeneficiaryInput
                    {
                        SchemeId = profitSchemeId,
                        EndPeriod = projectInfo.BudgetPlans.Count,
                        BeneficiaryShare = new BeneficiaryShare
                        {
                            Beneficiary = budgetPlan.ReceiverAddress,
                            Shares = 1
                        }
                    }.ToByteString());
            }

            return profitSchemeId;
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
    }
}