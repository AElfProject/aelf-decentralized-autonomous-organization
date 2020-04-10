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

        public override Empty ReleaseProposal(Hash input)
        {
            var proposalInfo = State.AssociationContract.GetProposal.Call(input);
            AssertApprovalCountMeetThreshold(proposalInfo.ApprovalCount);
            State.AssociationContract.Release.Send(input);
            return new Empty();
        }

        /// <summary>
        /// Help developers to create a proposal for initializing an investment project.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public override Hash ProposeProjectToDAO(ProposeProjectInput input)
        {
            var projectInfo = new ProjectInfo
            {
                PullRequestUrl = input.PullRequestUrl,
                CommitId = input.CommitId,
                // Initial status of an investment project.
                Status = ProjectStatus.Proposed
            };
            var projectId = projectInfo.GetProjectId();
            Assert(State.Projects[projectId] == null, "Project already proposed successfully before.");
            var proposalId = SelfProposalProcess(nameof(AddInvestmentProject), projectInfo.ToByteString());
            State.PreviewProposalIds[projectId] = proposalId;
            return proposalId;
        }

        public override Empty ProposeProjectToParliament(ProposeProjectWithBudgetsInput input)
        {
            var projectInfo = new ProjectInfo
            {
                PullRequestUrl = input.PullRequestUrl,
                CommitId = input.CommitId,
                Status = ProjectStatus.Approved,
                BudgetPlans = {input.BudgetPlans}
            };
            var projectId = projectInfo.GetProjectId();
            Assert(State.Projects[projectId] != null, "Project not found.");
            SelfProposalProcess(nameof(UpdateInvestmentProject), projectInfo.ToByteString());
            return new Empty();
        }

        public override Empty Invest(InvestInput input)
        {
            Assert(State.Projects[input.ProjectId] != null, "Project not found.");
            var projectInfo = State.Projects[input.ProjectId];
            var totalBudgets = projectInfo.BudgetPlans.Where(p => p.Symbol == input.Symbol).Sum(p => p.Amount);
            var existBalance = State.TokenContract.GetBalance.Call(new GetBalanceInput
            {
                Owner = projectInfo.VirtualAddress,
                Symbol = input.Symbol
            }).Balance;
            var actualAmount = Math.Min(totalBudgets.Sub(existBalance), input.Amount);
            if (actualAmount > 0)
            {
                State.TokenContract.TransferFrom.Send(new TransferFromInput
                {
                    From = Context.Sender,
                    To = projectInfo.VirtualAddress,
                    Amount = actualAmount,
                    Symbol = input.Symbol
                });
            }

            return new Empty();
        }

        public override Empty ProposeDeliver(ProposeAuditionInput input)
        {
            var projectInfo = new ProjectInfo
            {
                PullRequestUrl = input.PullRequestUrl,
                CommitId = input.CommitId,
                Status = ProjectStatus.Delivered,
                CurrentBudgetPlanIndex = input.BudgetPlanIndex
            };
            var projectInfoInState = State.Projects[projectInfo.GetProjectId()];
            Assert(projectInfoInState.CurrentBudgetPlanIndex.Add(1) == projectInfo.CurrentBudgetPlanIndex,
                "Incorrect budget plan index.");
            SelfProposalProcess(nameof(UpdateInvestmentProject), projectInfo.ToByteString());
            return new Empty();
        }

        public override Hash ProposeRewardProject(ProposeProjectInput input)
        {
            var projectInfo = new ProjectInfo
            {
                PullRequestUrl = input.PullRequestUrl,
                CommitId = input.CommitId,
                // Initial status of an reward project.
                Status = ProjectStatus.Proposed
            };
            var projectId = projectInfo.GetProjectId();
            Assert(State.Projects[projectId] == null, "Project already proposed successfully before.");
            var proposalId = SelfProposalProcess(nameof(AddInvestmentProject), projectInfo.ToByteString());
            State.PreviewProposalIds[projectId] = proposalId;
            return proposalId;
        }

        public override Empty ProposeIssueRewardProject(ProposeIssueRewardProjectInput input)
        {
            SelfProposalProcess(nameof(UpdateRewardProject), new ProjectInfo
            {
                PullRequestUrl = input.PullRequestUrl,
                CommitId = input.CommitId,
                Status = ProjectStatus.Approved
            }.ToByteString());
            return new Empty();
        }

        public override Empty ProposeTakeOverRewardProject(ProposeTakeOverRewardProjectInput input)
        {
            var projectInfo = new ProjectInfo
            {
                PullRequestUrl = input.PullRequestUrl,
                CommitId = input.CommitId
            };
            var projectId = projectInfo.GetProjectId();
            var projectInfoInState = State.Projects[projectId];
            var takenBudgetPlanIndices = projectInfoInState.BudgetPlans.Where(p => p.ReceiverAddress != null)
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

            // If all budget plans are taken, status will be ProjectStatus.Taken, otherwise stay ProjectStatus.Approved.
            projectInfo.Status =
                takenBudgetPlanIndices.Count.Add(input.BudgetPlanIndices.Count) == projectInfo.BudgetPlans.Count
                    ? ProjectStatus.Taken
                    : ProjectStatus.Approved;

            SelfProposalProcess(nameof(UpdateRewardProject), projectInfo.ToByteString());
            return new Empty();
        }

        public override Empty ProposeDevelopersAudition(ProposeAuditionInput input)
        {
            // TODO: Use new states to record audition result.
            return new Empty();
        }
    }
}