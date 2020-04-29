using AElf.Contracts.Association;
using AElf.Contracts.Consensus.AEDPoS;
using AElf.Contracts.MultiToken;
using AElf.Contracts.Parliament;
using AElf.Contracts.Profit;
using AElf.Contracts.Referendum;
using AElf.Sdk.CSharp.State;
using AElf.Types;

namespace AElf.Contracts.DAOContract
{
    // ReSharper disable InconsistentNaming
    public class DAOContractState : ContractState
    {
        internal AssociationContractContainer.AssociationContractReferenceState AssociationContract { get; set; }
        internal AEDPoSContractContainer.AEDPoSContractReferenceState ConsensusContract { get; set; }
        internal ParliamentContractContainer.ParliamentContractReferenceState ParliamentContract { get; set; }
        internal ReferendumContractContainer.ReferendumContractReferenceState ReferendumContract { get; set; }
        internal TokenContractContainer.TokenContractReferenceState TokenContract { get; set; }
        internal ProfitContractContainer.ProfitContractReferenceState ProfitContract { get; set; }

        public SingletonState<MemberList> DAOInitialMemberList { get; set; }

        public SingletonState<Address> ParliamentDefaultAddress { get; set; }

        public SingletonState<string> DepositSymbol { get; set; }
        public SingletonState<long> DepositAmount { get; set; }
        public SingletonState<MemberList> DAOMemberList { get; set; }

        public MappedState<Hash, ProjectInfo> Projects { get; set; }

        public SingletonState<Address> OrganizationAddress { get; set; }

        /// <summary>
        /// Project Id -> Proposal Id
        /// </summary>
        public MappedState<Hash, Hash> PreviewProposalIds { get; set; }

        public SingletonState<DAOProposalReleaseThreshold> DAOProposalReleaseThreshold { get; set; }

        public MappedState<Hash, bool> CanBeReleased { get; set; }

        public MappedState<Hash, Address> DeveloperOrganizationAddress { get; set; }

        public MappedState<Hash, bool> PreAuditionResult { get; set; }
    }
}