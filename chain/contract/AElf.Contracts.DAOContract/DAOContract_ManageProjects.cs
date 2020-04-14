using System.Linq;
using AElf.CSharp.Core;
using Google.Protobuf.WellKnownTypes;

namespace AElf.Contracts.DAOContract
{
    // ReSharper disable InconsistentNaming
    public partial class DAOContract
    {
        public override Empty AddInvestmentProject(ProjectInfo input)
        {
            var projectId = input.GetProjectId();
            CheckProjectProposalCanBeReleased(projectId);
            State.Projects[projectId] = new ProjectInfo
            {
                PullRequestUrl = input.PullRequestUrl,
                CommitId = input.CommitId,
                PreAuditionHash = input.PreAuditionHash,
                VirtualAddress = Context.ConvertVirtualAddressToContractAddress(projectId)
            };
            return new Empty();
        }

        public override Empty UpdateInvestmentProject(ProjectInfo input)
        {
            var projectId = input.GetProjectId();
            CheckProjectProposalCanBeReleased(projectId);
            var currentProject = State.Projects[projectId];

            if (input.CurrentBudgetPlanIndex > 0)
            {
                currentProject.CurrentBudgetPlanIndex = input.CurrentBudgetPlanIndex;
            }

            if (input.Status == ProjectStatus.Approved && currentProject.ProfitSchemeId == null)
            {
                CheckBudgetPlans(input.BudgetPlans);
                currentProject.BudgetPlans.AddRange(input.BudgetPlans);
                var profitSchemeId = CreateProfitScheme(currentProject);
                currentProject.ProfitSchemeId = profitSchemeId;
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

        public override Empty AddRewardProject(ProjectInfo input)
        {
            var projectId = input.GetProjectId();
            CheckProjectProposalCanBeReleased(projectId);
            input.VirtualAddress = Context.ConvertVirtualAddressToContractAddress(projectId);
            State.Projects[projectId] = input;
            return new Empty();
        }

        public override Empty UpdateRewardProject(ProjectInfo input)
        {
            var projectId = input.GetProjectId();
            CheckProjectProposalCanBeReleased(projectId);
            var currentProject = State.Projects[projectId];
            currentProject.Status = input.Status;

            if (input.CurrentBudgetPlanIndex > 0)
            {
                currentProject.CurrentBudgetPlanIndex = input.CurrentBudgetPlanIndex;
            }

            if (input.Status == ProjectStatus.Approved)
            {
                if (currentProject.BudgetPlans.Any())
                {
                    // Take budget plans.
                    foreach (var inputBudgetPlan in input.BudgetPlans.Where(p => p.ReceiverAddress != null))
                    {
                        var budgetPlan = currentProject.BudgetPlans.Single(p => p.Index == inputBudgetPlan.Index);
                        budgetPlan.ReceiverAddress = inputBudgetPlan.ReceiverAddress;
                    }
                }
                else
                {
                    // Initial budget plans.
                    currentProject.BudgetPlans.AddRange(input.BudgetPlans);
                    // Need to use Invest method to collect budgets.
                }
            }

            if (input.Status == ProjectStatus.Ready)
            {
                if (input.CurrentBudgetPlanIndex > 0)
                {
                    PayBudget(currentProject, input);
                }
            }

            if (input.Status == ProjectStatus.Delivered)
            {
                State.PreviewProposalIds.Remove(projectId);
            }

            State.Projects[projectId] = currentProject;
            return new Empty();
        }
    }
}