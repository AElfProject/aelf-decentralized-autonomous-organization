using System.Collections.Generic;
using System.Linq;
using Acs3;
using AElf.Contracts.Profit;
using AElf.CSharp.Core;
using AElf.CSharp.Core.Extension;
using AElf.Sdk.CSharp;
using AElf.Types;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace AElf.Contracts.DAOContract
{
    // ReSharper disable InconsistentNaming
    public partial class DAOContract
    {
        private void CreateProposalToAssociationContractAndRelease(string methodName, ByteString parameter)
        {
            var proposalToken = HashHelper.XorAndCompute(HashHelper.ComputeFrom(parameter.ToByteArray()),
                Context.PreviousBlockHash);
            var createProposalInput = new CreateProposalInput
            {
                ContractMethodName = methodName,
                Params = parameter,
                OrganizationAddress = State.OrganizationAddress.Value,
                ExpiredTime = GetExpiredTime(),
                ToAddress = State.AssociationContract.Value,
                Token = proposalToken
            };
            State.AssociationContract.CreateProposal.Send(createProposalInput);
            var proposalId = Context.GenerateId(State.AssociationContract.Value, proposalToken);
            State.AssociationContract.Approve.Send(proposalId);
            State.AssociationContract.Release.Send(proposalId);
        }

        private Hash CreateProposalToParliament(string methodName, ByteString parameter)
        {
            var proposalToken = HashHelper.XorAndCompute(HashHelper.ComputeFrom(parameter.ToByteArray()),
                Context.PreviousBlockHash);
            var createProposalInput = new CreateProposalInput
            {
                ContractMethodName = methodName,
                Params = parameter,
                OrganizationAddress = State.ParliamentDefaultAddress.Value,
                ExpiredTime = GetExpiredTime(),
                ToAddress = Context.Self,
                Token = proposalToken
            };
            State.ParliamentContract.CreateProposal.Send(createProposalInput);
            return Context.GenerateId(State.ParliamentContract.Value, proposalToken);
        }

        private Hash CreateProposalToSelf(string methodName, ByteString parameter)
        {
            var proposalToken = HashHelper.XorAndCompute(HashHelper.ComputeFrom(parameter.ToByteArray()),
                Context.PreviousBlockHash);
            var createProposalInput = new CreateProposalInput
            {
                ContractMethodName = methodName,
                Params = parameter,
                OrganizationAddress = State.OrganizationAddress.Value,
                ExpiredTime = GetExpiredTime(),
                ToAddress = Context.Self,
                Token = proposalToken
            };
            State.AssociationContract.CreateProposal.Send(createProposalInput);
            return Context.GenerateId(State.AssociationContract.Value, proposalToken);
        }

        private Hash CreateProposalToDeveloperOrganization(Address developerOrganizationAddress, string methodName,
            ByteString parameter)
        {
            var proposalToken = HashHelper.XorAndCompute(HashHelper.ComputeFrom(parameter.ToByteArray()),
                Context.PreviousBlockHash);
            var createProposalInput = new CreateProposalInput
            {
                ContractMethodName = methodName,
                Params = parameter,
                OrganizationAddress = developerOrganizationAddress,
                ExpiredTime = GetExpiredTime(),
                ToAddress = Context.Self,
                Token = proposalToken
            };
            State.AssociationContract.CreateProposal.Send(createProposalInput);
            return Context.GenerateId(State.AssociationContract.Value, proposalToken);
        }

        private Timestamp GetExpiredTime()
        {
            return Context.CurrentBlockTime.AddDays(7);
        }

        private void AssertReleasedByParliament()
        {
            if (State.ParliamentContract.Value == null)
                State.ParliamentContract.Value =
                    Context.GetContractAddressByName(SmartContractConstants.ParliamentContractSystemName);
            var defaultAddress = State.ParliamentContract.GetDefaultOrganizationAddress.Call(new Empty());
            Assert(Context.Sender == defaultAddress, "No permission.");
        }

        private void AssertReleaseThresholdReached(ProposalOutput proposal,
            DAOProposalReleaseThreshold proposalReleaseThreshold)
        {
            Assert(IsReleaseThresholdReached(proposal, proposalReleaseThreshold), "No approved by certain members.");
        }

        private bool IsReleaseThresholdReached(ProposalOutput proposal,
            DAOProposalReleaseThreshold proposalReleaseThreshold)
        {
            var isRejected = IsProposalRejected(proposal, proposalReleaseThreshold);
            if (isRejected)
                return false;

            var isAbstained = IsProposalAbstained(proposal, proposalReleaseThreshold);
            return !isAbstained && CheckEnoughVoteAndApprovals(proposal, proposalReleaseThreshold);
        }

        private bool IsProposalRejected(ProposalOutput proposal, DAOProposalReleaseThreshold proposalReleaseThreshold)
        {
            return proposal.RejectionCount > proposalReleaseThreshold.MaximalRejectionThreshold;
        }

        private bool IsProposalAbstained(ProposalOutput proposal, DAOProposalReleaseThreshold proposalReleaseThreshold)
        {
            return proposal.AbstentionCount > proposalReleaseThreshold.MaximalAbstentionThreshold;
        }

        private bool CheckEnoughVoteAndApprovals(ProposalOutput proposal,
            DAOProposalReleaseThreshold proposalReleaseThreshold)
        {
            var isApprovalEnough =
                proposal.ApprovalCount >= proposalReleaseThreshold.MinimalApprovalThreshold;
            if (!isApprovalEnough)
                return false;

            var isVoteThresholdReached =
                proposal.ApprovalCount.Add(proposal.AbstentionCount).Add(proposal.RejectionCount) >=
                proposalReleaseThreshold.MinimalVoteThreshold;
            return isVoteThresholdReached;
        }

        private void AdjustDAOProposalReleaseThreshold()
        {
            var memberListCount = State.DAOInitialMemberList.Value.Value.Count;
            State.DAOProposalReleaseThreshold.Value = new DAOProposalReleaseThreshold
            {
                MaximalAbstentionThreshold = memberListCount.Div(2),
                MaximalRejectionThreshold = memberListCount.Div(2),
                MinimalApprovalThreshold = memberListCount.Div(2),
                MinimalVoteThreshold = memberListCount.Div(2)
            };
        }

        private void AssertReleaseDeveloperOrganizationThresholdReached(Hash proposalId, int developerCount)
        {
            var proposalInfo = State.AssociationContract.GetProposal.Call(proposalId);
            Assert(proposalInfo.ProposalId != null, "Proposal not found.");
            // Allow one developer not approve.
            AssertReleaseThresholdReached(proposalInfo, new DAOProposalReleaseThreshold
            {
                MaximalAbstentionThreshold = developerCount.Div(2),
                MaximalRejectionThreshold = developerCount.Div(2),
                MinimalApprovalThreshold = developerCount.Div(2),
                MinimalVoteThreshold = developerCount.Div(2)
            });
        }

        private Hash CreateProfitScheme(ProjectInfo projectInfo)
        {
            var token = HashHelper.ComputeFrom(projectInfo);
            var createSchemeInput = new CreateSchemeInput
            {
                Manager = projectInfo.VirtualAddress,
                IsReleaseAllBalanceEveryTimeByDefault = true,
                CanRemoveBeneficiaryDirectly = true,
                Token = token
            };
            State.ProfitContract.CreateScheme.Send(createSchemeInput);
            return Context.GenerateId(State.ProfitContract.Value, token);
        }

        private void AddBeneficiaryForGrantProject(ProjectInfo projectInfo)
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

        private void AddBeneficiaryForBountyProject(ProjectInfo projectInfo)
        {
            var targetIndex = projectInfo.CurrentBudgetPlanIndex.Sub(1);
            var budgetPlan = projectInfo.BudgetPlans.SingleOrDefault(p => p.Index == targetIndex);
            if (budgetPlan == null) return;
            if (projectInfo.CurrentBudgetPlanIndex > 0)
            {
                var preBudgetPlan = projectInfo.BudgetPlans
                    .SingleOrDefault(p => p.Index == targetIndex.Sub(1));
                if (preBudgetPlan != null)
                {
                    var preBudgetPlanReceiver = preBudgetPlan.ReceiverAddress;
                    Context.SendVirtualInline(projectInfo.GetProjectId(), State.ProfitContract.Value,
                        nameof(State.ProfitContract.RemoveBeneficiary), new RemoveBeneficiaryInput
                        {
                            SchemeId = projectInfo.ProfitSchemeId,
                            Beneficiary = preBudgetPlanReceiver
                        }.ToByteString());
                }
            }

            Context.SendVirtualInline(projectInfo.GetProjectId(), State.ProfitContract.Value,
                nameof(State.ProfitContract.AddBeneficiary), new AddBeneficiaryInput
                {
                    SchemeId = projectInfo.ProfitSchemeId,
                    EndPeriod = projectInfo.CurrentBudgetPlanIndex,
                    BeneficiaryShare = new BeneficiaryShare
                    {
                        Beneficiary = budgetPlan.ReceiverAddress,
                        Shares = 1
                    }
                }.ToByteString());
        }

        private void PayBudget(ProjectInfo projectInfoIsState, ProjectInfo inputProjectInfo)
        {
            var projectId = inputProjectInfo.GetProjectId();
            var targetIndex = inputProjectInfo.CurrentBudgetPlanIndex.Sub(1);
            var budgetPlan =
                projectInfoIsState.BudgetPlans.Single(p => p.Index == targetIndex);
            var inputBudgetPlan =
                inputProjectInfo.BudgetPlans.Single(p => p.Index == targetIndex);
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
                    Period = inputProjectInfo.CurrentBudgetPlanIndex,
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

        private void ValidateBudgetPlanIndices(IReadOnlyCollection<BudgetPlan> budgetPlans)
        {
            if (!budgetPlans.Any()) return;
            Assert(budgetPlans.First().Index == 0, "Budget plan index must start from 0.");
            var indices = budgetPlans.Select(p => p.Index).ToList();
            for (var i = 0; i < indices.Count.Sub(1); i++)
            {
                if (indices[i.Add(1)] <= indices[i])
                {
                    throw new AssertionException("Budget plans indices must in order.");
                }
            }
        }

        private Hash ProposeToAddProject(string pullRequestUrl, string commitId, ProjectType projectType,
            bool isDevelopersAuditionRequired = false)
        {
            var projectInfo = new ProjectInfo
            {
                PullRequestUrl = pullRequestUrl,
                CommitId = commitId,
                // Initial status of an investment project.
                Status = ProjectStatus.Proposed,
                ProjectType = projectType,
                IsDevelopersAuditionRequired = isDevelopersAuditionRequired
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
            if (projectInfo == null)
            {
                throw new AssertionException("Project not found.");
            }

            Assert(projectInfo.Status == ProjectStatus.Proposed, "Incorrect status.");
            Assert(projectInfo.ProjectType == projectType, "Incorrect project type.");
            foreach (var budgetPlan in input.BudgetPlans)
            {
                if (projectType == ProjectType.Bounty)
                {
                    budgetPlan.ReceiverAddress = null;
                }
                else
                {
                    Assert(budgetPlan.ReceiverAddress != null, "Receiver address cannot be null for a grant project.");
                }
            }

            var proposalId = CreateProposalToParliament(
                projectType == ProjectType.Grant ? nameof(UpdateGrantProject) : nameof(UpdateBountyProject),
                new ProjectInfo
                {
                    // ReSharper disable once PossibleNullReferenceException
                    PullRequestUrl = projectInfo.PullRequestUrl,
                    CommitId = projectInfo.CommitId,
                    Status = ProjectStatus.Approved,
                    BudgetPlans = {input.BudgetPlans},
                    IsDevelopersAuditionRequired = projectInfo.IsDevelopersAuditionRequired
                }.ToByteString());
            return proposalId;
        }
    }
}