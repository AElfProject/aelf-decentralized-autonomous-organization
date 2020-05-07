using System.Linq;
using AElf.Types;
using Google.Protobuf.WellKnownTypes;

namespace AElf.Contracts.DAOContract
{
    // ReSharper disable once InconsistentNaming
    public partial class DAOContract
    {
        public override BudgetPlan GetBudgetPlan(GetBudgetPlanInput input)
        {
            var projectInfo = State.Projects[input.ProjectId];
            Assert(projectInfo != null, "Project not found.");
            // ReSharper disable once PossibleNullReferenceException
            return projectInfo.BudgetPlans.SingleOrDefault(p => p.Index == input.BudgetPlanIndex);
        }

        public override MemberList GetDAOMemberList(Empty input)
        {
            var organization = State.AssociationContract.GetOrganization.Call(State.OrganizationAddress.Value);

            return new MemberList
            {
                Value = {organization.OrganizationMemberList.OrganizationMembers}
            };
        }

        public override Hash GetPreviewProposalId(ProposeProjectInput input)
        {
            var projectInfo = new ProjectInfo
            {
                PullRequestUrl = input.PullRequestUrl,
                CommitId = input.CommitId
            };
            return State.PreviewProposalIds[projectInfo.GetProjectId()];
        }

        public override ProjectInfo GetProjectInfo(Hash input)
        {
            return State.Projects[input];
        }

        public override Hash CalculateProjectId(ProposeProjectInput input)
        {
            return new ProjectInfo
            {
                PullRequestUrl = input.PullRequestUrl,
                CommitId = input.CommitId
            }.GetProjectId();
        }

        public override BoolValue GetPreAuditionResult(Hash input)
        {
            return new BoolValue
            {
                Value = State.PreAuditionResult[input]
            };
        }

        public override DepositInfo GetDepositInfo(Empty input)
        {
            return State.DepositInfo.Value;
        }
    }
}