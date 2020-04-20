using System.Collections.Generic;
using System.Linq;
using Acs3;
using AElf.Contracts.Association;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;

namespace AElf.Contracts.DAOContract
{
    // ReSharper disable InconsistentNaming
    public partial class DAOContract
    {
        public override Empty AddProject(ProjectInfo input)
        {
            var projectId = input.GetProjectId();
            CheckProjectProposalCanBeReleased(projectId);
            State.Projects[projectId] = new ProjectInfo
            {
                PullRequestUrl = input.PullRequestUrl,
                CommitId = input.CommitId,
                PreAuditionHash = input.PreAuditionHash,
                VirtualAddress = Context.ConvertVirtualAddressToContractAddress(projectId),
                ProjectType = input.ProjectType
            };
            return new Empty();
        }

        public override Empty UpdateInvestmentProject(ProjectInfo input)
        {
            var projectId = input.GetProjectId();
            CheckProjectProposalCanBeReleased(projectId);
            var currentProject = State.Projects[projectId];

            Assert(currentProject != null, "Project not found.");
            // ReSharper disable once PossibleNullReferenceException
            currentProject.CurrentBudgetPlanIndex = input.CurrentBudgetPlanIndex;
            Assert(currentProject.Status != ProjectStatus.Delivered, "Project already delivered.");

            if (input.Status == ProjectStatus.Approved && currentProject.ProfitSchemeId == null)
            {
                // Update budget plans.
                CheckBudgetPlans(input.BudgetPlans);
                currentProject.BudgetPlans.AddRange(input.BudgetPlans);

                // Create project scheme and add developers as beneficiaries.
                var profitSchemeId = CreateProfitScheme(currentProject);
                currentProject.ProfitSchemeId = profitSchemeId;
                AddBeneficiaryForInvestmentProject(currentProject);
            }

            if (input.Status == ProjectStatus.Ready || input.Status == ProjectStatus.Delivered)
            {
                if (currentProject.Status == ProjectStatus.Ready)
                {
                    PayBudget(currentProject, input);
                }
            }

            if (input.Status == ProjectStatus.Delivered)
            {
                State.PreviewProposalIds.Remove(projectId);

                // TODO: Maintain beneficiaries for sender.
                // Once a project is DELIVERED, beneficiaries will be investors.
                // If symbols are diff for every budget plan, may need to calculate weight.
            }

            currentProject.Status = input.Status;
            State.Projects[projectId] = currentProject;
            return new Empty();
        }

        public override Empty UpdateRewardProject(ProjectInfo input)
        {
            var projectId = input.GetProjectId();
            CheckProjectProposalCanBeReleased(projectId);
            var currentProject = State.Projects[projectId];
            Assert(currentProject != null, "Project not found.");
            // ReSharper disable once PossibleNullReferenceException
            currentProject.CurrentBudgetPlanIndex = input.CurrentBudgetPlanIndex;
            Assert(currentProject.Status != ProjectStatus.Delivered, "Project already delivered.");

            if (input.Status == ProjectStatus.Approved && currentProject.ProfitSchemeId == null)
            {
                CheckBudgetPlans(input.BudgetPlans);
                currentProject.BudgetPlans.AddRange(input.BudgetPlans);
                var profitSchemeId = CreateProfitScheme(currentProject);
                currentProject.ProfitSchemeId = profitSchemeId;
            }

            if (input.Status == ProjectStatus.Approved)
            {
                if (!currentProject.BudgetPlans.Any())
                {
                    // Initial budget plans.
                    currentProject.BudgetPlans.AddRange(input.BudgetPlans);
                }
            }

            if (input.Status == ProjectStatus.Ready || input.Status == ProjectStatus.Taken)
            {
                foreach (var inputBudgetPlan in input.BudgetPlans.Where(p => p.ReceiverAddress != null))
                {
                    var budgetPlan = currentProject.BudgetPlans.Single(p => p.Index == inputBudgetPlan.Index);
                    budgetPlan.ReceiverAddress = inputBudgetPlan.ReceiverAddress;
                }
            }

            if (input.Status == ProjectStatus.Taken || input.Status == ProjectStatus.Delivered)
            {
                AddBeneficiaryForRewardProject(currentProject);

                if (currentProject.BudgetPlans.All(p => p.ReceiverAddress != null))
                {
                    var developerList = currentProject.BudgetPlans.Select(p => p.ReceiverAddress);
                    State.DeveloperOrganizationAddress[projectId] = CreateDeveloperOrganization(developerList);
                }

                if (currentProject.Status == ProjectStatus.Taken &&
                    currentProject.BudgetPlans.All(p => p.IsApprovedByDevelopers))
                {
                    PayBudget(currentProject, input);
                }

                Assert(input.BudgetPlans.Count == 1, "Can only update one budget plan one time.");
                var updateBudgetPlan = input.BudgetPlans.Single();

                // Approved by developers.
                if (updateBudgetPlan.IsApprovedByDevelopers)
                {
                    var targetBudgetPlan =
                        currentProject.BudgetPlans.SingleOrDefault(p => p.Index == updateBudgetPlan.Index);
                    Assert(targetBudgetPlan != null, "Target budget plan not found.");
                    // ReSharper disable once PossibleNullReferenceException
                    targetBudgetPlan.IsApprovedByDevelopers = true;
                }
            }

            if (input.Status == ProjectStatus.Delivered)
            {
                State.PreviewProposalIds.Remove(projectId);
            }

            currentProject.Status = input.Status;
            State.Projects[projectId] = currentProject;
            return new Empty();
        }

        private Address CreateDeveloperOrganization(IEnumerable<Address> developerList)
        {
            var createOrganizationInput = new CreateOrganizationInput
            {
                OrganizationMemberList = new OrganizationMemberList
                {
                    OrganizationMembers = {developerList}
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
            return State.AssociationContract.CalculateOrganizationAddress.Call(createOrganizationInput);
        }
    }
}