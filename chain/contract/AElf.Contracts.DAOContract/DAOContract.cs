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
            State.ReferendumContract.Value =
                Context.GetContractAddressByName(SmartContractConstants.ReferendumContractSystemName);

            State.ParliamentDefaultAddress.Value =
                State.ParliamentContract.GetDefaultOrganizationAddress.Call(new Empty());

            // Create Decentralized Autonomous Organization via Association Contract.
            var minerList = input.InitialMemberList != null && input.InitialMemberList.Any()
                ? new MemberList {Value = {input.InitialMemberList}}
                : new MemberList
                {
                    Value =
                    {
                        State.ConsensusContract.GetMinerList.Call(new GetMinerListInput {TermNumber = 1}).Pubkeys
                            .Select(p =>
                                Address.FromPublicKey(ByteArrayHelper.HexStringToByteArray(p.ToHex())))
                    }
                };
            minerList.Value.Add(Context.Self);
            var createOrganizationInput = new CreateOrganizationInput
            {
                OrganizationMemberList = new OrganizationMemberList
                {
                    OrganizationMembers =
                    {
                        minerList.Value
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
            State.DAOMemberList.Value = minerList;

            State.DAOInitialMemberList.Value = minerList;

            State.DepositSymbol.Value = Context.Variables.NativeSymbol;
            State.DepositAmount.Value = input.DepositAmount;

            AdjustDAOProposalReleaseThreshold();

            return new Empty();
        }

        public override Empty ReleaseProposal(ReleaseProposalInput input)
        {
            State.CanBeReleased[input.ProjectId] = true;
            switch (input.OrganizationType)
            {
                case ProposalOrganizationType.Parliament:
                    State.ParliamentContract.Release.Send(input.ProposalId);
                    break;
                case ProposalOrganizationType.DAO:
                    var proposalInfo = State.AssociationContract.GetProposal.Call(input.ProposalId);
                    AssertReleaseThresholdReached(proposalInfo, State.DAOProposalReleaseThreshold.Value);
                    State.AssociationContract.Release.Send(input.ProposalId);
                    break;
                case ProposalOrganizationType.Developers:
                    var projectInfo = State.Projects[input.ProjectId];
                    AssertReleaseDeveloperOrganizationThresholdReached(input.ProposalId,
                        projectInfo.BudgetPlans.Select(p => p.ReceiverAddress).Distinct().Count());
                    State.AssociationContract.Release.Send(input.ProposalId);
                    break;
                default:
                {
                    Assert(false, "Invalid Organization Type.");
                    break;
                }
            }

            return new Empty();
        }

        public override Hash ProjectPreAudition(ProjectPreAuditionInput input)
        {
            Assert(Context.Sender == State.ReferendumOrganizationAddress.Value, "No permission.");
            var project = new ProjectInfo
            {
                PullRequestUrl = input.PullRequestUrl,
                CommitId = input.CommitId
            };
            State.PreAuditionResult[project.GetProjectId()] = true;
            return new Hash();
        }

        public override Empty SetReferendumOrganizationAddress(Address input)
        {
            Assert(Context.Sender == State.ParliamentDefaultAddress.Value, "No permission.");
            State.ReferendumOrganizationAddress.Value = input;
            return new Empty();
        }

        public override Empty AdjustProposalReleaseThreshold(DAOProposalReleaseThreshold input)
        {
            Assert(State.DAOInitialMemberList.Value.Value.Contains(Context.Sender), "No permission.");
            State.DAOProposalReleaseThreshold.Value = input;
            return new Empty();
        }

        /// <summary>
        /// Help developers to create a proposal for initializing an investment project.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public override Hash ProposeProjectToDAO(ProposeProjectInput input)
        {
            return ProposeToAddProject(input.PullRequestUrl, input.CommitId, ProjectType.Grant);
        }

        public override Hash ProposeProjectToParliament(ProposeProjectWithBudgetsInput input)
        {
            return ProposedToUpdateProjectWithBudgetPlans(input, ProjectType.Grant);
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
                    if (remainBudgets <= 0)
                    {
                        Context.Fire(new InvestmentFeedback
                        {
                            InvestmentStatus = InvestmentStatus.Complete
                        });
                        break;
                    }
                }

                if (remainBudgets > 0)
                {
                    Context.Fire(new InvestmentFeedback
                    {
                        Symbol = input.Symbol,
                        RemainAmount = remainBudgets,
                        InvestmentStatus = InvestmentStatus.NotEnough
                    });
                }
            }
            else
            {
                Context.Fire(new InvestmentFeedback
                {
                    InvestmentStatus = InvestmentStatus.Complete
                });
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
                    : projectInfo.ProjectType == ProjectType.Grant
                        ? ProjectStatus.Ready
                        : ProjectStatus.Taken,
                BudgetPlans = {projectInfo.BudgetPlans.Single(p => p.Index == input.BudgetPlanIndex)}
            };

            if (projectInfo.ProjectType == ProjectType.Bounty && projectInfo.IsDevelopersAuditionRequired)
            {
                Assert(projectInfo.BudgetPlans.All(p => p.IsApprovedByDevelopers),
                    "Project budget plans need to approved by developers before deliver.");
            }

            var budgetPlan = newProjectInfo.BudgetPlans.SingleOrDefault(p => p.Index == input.BudgetPlanIndex);
            Assert(budgetPlan != null, "Budget Plan not found.");
            // ReSharper disable once PossibleNullReferenceException
            budgetPlan.DeliverPullRequestUrl = input.DeliverPullRequestUrl;
            budgetPlan.DeliverCommitId = input.DeliverCommitId;
            var proposalId =
                CreateProposalToSelf(
                    projectInfo.ProjectType == ProjectType.Grant
                        ? nameof(UpdateGrantProject)
                        : nameof(UpdateBountyProject), newProjectInfo.ToByteString());
            return proposalId;
        }

        public override Hash ProposeBountyProject(ProposeProjectInput input)
        {
            Assert(State.DAOMemberList.Value.Value.Contains(Context.Sender),
                "Only DAO Member can propose bounty project.");
            return ProposeToAddProject(input.PullRequestUrl, input.CommitId, ProjectType.Bounty,
                input.IsDevelopersAuditionRequired);
        }

        public override Hash ProposeIssueBountyProject(ProposeProjectWithBudgetsInput input)
        {
            Assert(State.DAOMemberList.Value.Value.Contains(Context.Sender),
                "Only DAO Member can propose bounty project.");
            return ProposedToUpdateProjectWithBudgetPlans(input, ProjectType.Bounty);
        }

        public override Hash ProposeTakeOverBountyProject(ProposeTakeOverBountyProjectInput input)
        {
            var projectInfo = State.Projects[input.ProjectId].Clone();
            Assert(projectInfo != null, "Project not found.");
            // ReSharper disable once PossibleNullReferenceException
            Assert(projectInfo.ProjectType == ProjectType.Bounty, "Only bounty project support this option.");
            Assert(projectInfo.Status == ProjectStatus.Ready, "bounty not ready.");
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

            var status = takenBudgetPlanIndices.Count.Add(input.BudgetPlanIndices.Count) ==
                         projectInfo.BudgetPlans.Count
                ? ProjectStatus.Taken
                : ProjectStatus.Ready;
            return CreateProposalToSelf(nameof(UpdateBountyProject), new ProjectInfo
            {
                PullRequestUrl = projectInfo.PullRequestUrl,
                CommitId = projectInfo.CommitId,
                // If all budget plans are taken, status will be ProjectStatus.Taken, otherwise stay ProjectStatus.Approved.
                Status = status,
                BudgetPlans = {projectInfo.BudgetPlans.Where(p => input.BudgetPlanIndices.Contains(p.Index))}
            }.ToByteString());
        }

        public override Hash ProposeDevelopersAudition(ProposeAuditionInput input)
        {
            var projectInfo = State.Projects[input.ProjectId].Clone();
            if (projectInfo == null)
            {
                throw new AssertionException("Project not found.");
            }

            Assert(projectInfo.Status == ProjectStatus.Taken, "Project needs to be taken.");
            var developerOrganizationAddress = State.DeveloperOrganizationAddress[input.ProjectId];
            // ReSharper disable once PossibleNullReferenceException
            var targetBudgetPlan = projectInfo.BudgetPlans.Single(p => p.Index == input.BudgetPlanIndex);
            targetBudgetPlan.IsApprovedByDevelopers = true;
            var newProjectInfo = new ProjectInfo
            {
                PullRequestUrl = projectInfo.PullRequestUrl,
                CommitId = projectInfo.CommitId,
                CurrentBudgetPlanIndex = projectInfo.CurrentBudgetPlanIndex,
                PreAuditionHash = projectInfo.PreAuditionHash,
                Status = ProjectStatus.Taken,
                BudgetPlans = {targetBudgetPlan}
            };
            var proposalId = CreateProposalToDeveloperOrganization(developerOrganizationAddress,
                nameof(UpdateBountyProject), newProjectInfo.ToByteString());
            return proposalId;
        }

        public override Hash ProposeRemoveProject(Hash input)
        {
            Assert(State.DAOMemberList.Value.Value.Contains(Context.Sender), "No permission.");
            return CreateProposalToSelf(nameof(RemoveProject), input.ToByteString());
        }
    }
}