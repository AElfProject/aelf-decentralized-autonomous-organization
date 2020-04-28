using AElf.Sdk.CSharp;
using AElf.Types;

// ReSharper disable once CheckNamespace
namespace AElf.Contracts.DAOContract
{
    public partial class ProjectInfo
    {
        public Hash GetProjectId()
        {
            return HashHelper.ComputeFrom(CommitId.Append(PullRequestUrl));
        }
    }
}