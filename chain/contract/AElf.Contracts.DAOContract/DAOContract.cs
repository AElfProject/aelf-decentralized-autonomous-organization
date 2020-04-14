using System;
using System.Linq;
using Acs3;
using AElf.Contracts.Association;
using AElf.Contracts.Consensus.AEDPoS;
using AElf.Contracts.MultiToken;
using AElf.CSharp.Core;
using AElf.Sdk.CSharp;
using AElf.Types;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace AElf.Contracts.DAOContract
{
    // ReSharper disable InconsistentNaming
    public partial class DAOContract : DAOContractContainer.DAOContractBase
    {
        public override Empty Initialize(InitializeInput input)
        {
            State.AssociationContract.Value =
                Context.GetContractAddressByName(SmartContractConstants.AssociationContractSystemName);
            State.ConsensusContract.Value =
                Context.GetContractAddressByName(SmartContractConstants.ConsensusContractSystemName);
            State.ParliamentContract.Value =
                Context.GetContractAddressByName(SmartContractConstants.ParliamentContractSystemName);
            State.TokenContract.Value =
                Context.GetContractAddressByName(SmartContractConstants.TokenContractSystemName);
            State.ProfitContract.Value =
                Context.GetContractAddressByName(SmartContractConstants.ProfitContractSystemName);

            State.ParliamentDefaultAddress.Value =
                State.ParliamentContract.GetDefaultOrganizationAddress.Call(new Empty());

            // Create Decentralized Autonomous Organization via Association Contract.
            var minerList = State.ConsensusContract.GetMinerList.Call(new GetMinerListInput {TermNumber = 1});
            var members = minerList.Pubkeys.Select(p =>
                Address.FromPublicKey(ByteArrayHelper.HexStringToByteArray(p.ToHex()))).ToList();
            members.Add(Context.Self);
            var createOrganizationInput = new CreateOrganizationInput
            {
                OrganizationMemberList = new OrganizationMemberList
                {
                    OrganizationMembers =
                    {
                        members
                    }
                },
                ProposalReleaseThreshold = new ProposalReleaseThreshold
                {
                    MinimalApprovalThreshold = 1, MinimalVoteThreshold = 1
                },
                ProposerWhiteList = new ProposerWhiteList
                {
                    Proposers = {Context.Self}
                }
            };
            State.AssociationContract.CreateOrganization.Send(createOrganizationInput);
            // Record DAO Address and initial member list.
            State.OrganizationAddress.Value =
                State.AssociationContract.CalculateOrganizationAddress.Call(createOrganizationInput);
            State.DAOMemberList.Value = new MemberList
            {
                Value = {members}
            };

            State.DepositSymbol.Value = Context.Variables.NativeSymbol;
            State.DepositAmount.Value = input.DepositAmount;
            State.ApprovalThreshold.Value = 1;
            return new Empty();
        }

        public override Empty ReleaseProposal(ReleaseProposalInput input)
        {
            State.CanBeReleased[input.ProjectId] = true;

            if (input.IsParliamentProposal)
            {
                State.ParliamentContract.Release.Send(input.ProposalId);
            }
            else
            {
                var proposalInfo = State.AssociationContract.GetProposal.Call(input.ProposalId);
                AssertApprovalCountMeetThreshold(proposalInfo.ApprovalCount);
                State.AssociationContract.Release.Send(input.ProposalId);
            }

            return new Empty();
        }

        /// <summary>
        /// Help developers to create a proposal for initializing an investment project.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public override Hash ProposeProjectToDAO(ProposeProjectInput input)
        {
            return ProposeToAddProject(input.PullRequestUrl, input.CommitId);
        }

        public override Hash ProposeProjectToParliament(ProposeProjectWithBudgetsInput input)
        {
            return ProposedToUpdateProjectWithBudgetPlans(input);
        }

        public override Empty Invest(InvestInput input)
        {
            Assert(State.Projects[input.ProjectId] != null, "Project not found.");
            var projectInfo = State.Projects[input.ProjectId];
            var totalBudgets = projectInfo.BudgetPlans.Where(p => p.Symbol == input.Symbol).Sum(p => p.Amount);
            var currentBalance = State.TokenContract.GetBalance.Call(new GetBalanceInput
            {
                Owner = projectInfo.VirtualAddress,
                Symbol = input.Symbol
            }).Balance;
            var actualAmount = Math.Min(totalBudgets.Sub(currentBalance), input.Amount);
            if (actualAmount > 0)
            {
                State.TokenContract.TransferFrom.Send(new TransferFromInput
                {
                    From = Context.Sender,
                    To = projectInfo.VirtualAddress,
                    Amount = actualAmount,
                    Symbol = input.Symbol
                });

                // Update BudgetPlans.
                // TODO: Possible improve.
                var remainBudgets = currentBalance.Add(actualAmount);
                foreach (var budgetPlan in projectInfo.BudgetPlans.Where(p => p.Symbol == input.Symbol))
                {
                    budgetPlan.PaidInAmount = Math.Min(budgetPlan.Amount, remainBudgets);
                    remainBudgets = remainBudgets.Sub(budgetPlan.Amount);
                    if (remainBudgets <= 0) break;
                }
            }

            if (projectInfo.BudgetPlans.All(p => p.Amount == p.PaidInAmount))
            {
                projectInfo.Status = ProjectStatus.Ready;
                State.Projects[input.ProjectId] = projectInfo;
            }

            return new Empty();
        }

        public override Hash ProposeDeliver(ProposeAuditionInput input)
        {
            var projectInfo = State.Projects[input.ProjectId];
            Assert(
                projectInfo.CurrentBudgetPlanIndex == 0 ||
                projectInfo.CurrentBudgetPlanIndex.Add(1) == input.BudgetPlanIndex,
                "Incorrect budget plan index.");

            var newProjectInfo = new ProjectInfo
            {
                PullRequestUrl = projectInfo.PullRequestUrl,
                CommitId = projectInfo.CommitId,
                CurrentBudgetPlanIndex = input.BudgetPlanIndex,
                Status = projectInfo.BudgetPlans.Select(p => p.Index).OrderBy(p => p).Last() == input.BudgetPlanIndex
                    ? ProjectStatus.Delivered
                    : ProjectStatus.Ready,
                BudgetPlans = {projectInfo.BudgetPlans}
            };

            var budgetPlan = newProjectInfo.BudgetPlans.SingleOrDefault(p => p.Index == input.BudgetPlanIndex);
            Assert(budgetPlan != null, "Budget Plan not found.");
            // ReSharper disable once PossibleNullReferenceException
            budgetPlan.DeliverPullRequestUrl = input.DeliverPullRequestUrl;
            budgetPlan.DeliverCommitId = input.DeliverCommitId;
            var proposalId = CreateProposalToSelf(nameof(UpdateInvestmentProject), newProjectInfo.ToByteString());
            return proposalId;
        }

        public override Hash ProposeRewardProject(ProposeProjectInput input)
        {
            Assert(State.DAOMemberList.Value.Value.Contains(Context.Sender),
                "Only DAO Member can propose reward project.");
            return ProposeToAddProject(input.PullRequestUrl, input.CommitId);
        }

        public override Hash ProposeIssueRewardProject(ProposeProjectWithBudgetsInput input)
        {
            Assert(State.DAOMemberList.Value.Value.Contains(Context.Sender),
                "Only DAO Member can propose reward project.");
            return ProposedToUpdateProjectWithBudgetPlans(input);
        }

        public override Hash ProposeTakeOverRewardProject(ProposeTakeOverRewardProjectInput input)
        {
            var projectInfo = State.Projects[input.ProjectId];
            Assert(projectInfo != null, "Project not found.");
            // ReSharper disable once PossibleNullReferenceException
            var takenBudgetPlanIndices = projectInfo.BudgetPlans.Where(p => p.ReceiverAddress != null)
                .Select(p => p.Index).ToList();
            Assert(!takenBudgetPlanIndices.Any(i => input.BudgetPlanIndices.Contains(i)),
                "Budget plan already taken.");

            foreach (var planIndex in input.BudgetPlanIndices)
            {
                var proposeTakenPlan = projectInfo.BudgetPlans.FirstOrDefault(p => p.Index == planIndex);
                Assert(proposeTakenPlan != null, "Budget plan not found.");
                // ReSharper disable once PossibleNullReferenceException
                proposeTakenPlan.ReceiverAddress = Context.Sender;
            }

            return CreateProposalToSelf(nameof(UpdateRewardProject), new ProjectInfo
            {
                PullRequestUrl = projectInfo.PullRequestUrl,
                CommitId = projectInfo.CommitId,
                // If all budget plans are taken, status will be ProjectStatus.Taken, otherwise stay ProjectStatus.Approved.
                Status = ProjectStatus.Ready,
                BudgetPlans = {projectInfo.BudgetPlans}
            }.ToByteString());
        }

        public override Hash ProposeDevelopersAudition(ProposeAuditionInput input)
        {

            return Hash.Empty;
        }
    }
}