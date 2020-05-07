using AElf.Contracts.Association;
using AElf.Contracts.MultiToken;
using AElf.Types;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace AElf.Contracts.DAOContract
{
    // ReSharper disable InconsistentNaming
    public partial class DAOContract
    {
        public override Empty ProposeJoin(Address input)
        {
            AssertReleasedByParliament();
            if (State.DepositInfo.Value.Amount > 0)
            {
                State.TokenContract.TransferFrom.Send(new TransferFromInput
                {
                    From = input,
                    To = Context.Self,
                    Symbol = State.DepositInfo.Value.Symbol,
                    Amount = State.DepositInfo.Value.Amount
                });
            }

            var memberList = State.DAOMemberList.Value;
            Assert(!memberList.Value.Contains(input), $"{input} is already a DAO member.");
            memberList.Value.Add(input);
            CreateProposalToAssociationContractAndRelease(nameof(State.AssociationContract.ChangeOrganizationMember), new OrganizationMemberList
            {
                OrganizationMembers = {memberList.Value}
            }.ToByteString());
            State.DAOMemberList.Value = memberList;
            AdjustDAOProposalReleaseThreshold();
            return new Empty();
        }

        public override Empty Quit(Empty input)
        {
            if (State.DepositInfo.Value.Amount > 0)
            {
                State.TokenContract.Transfer.Send(new TransferInput
                {
                    To = Context.Sender,
                    Symbol = State.DepositInfo.Value.Symbol,
                    Amount = State.DepositInfo.Value.Amount
                });
            }

            var memberList = State.DAOMemberList.Value;
            Assert(memberList.Value.Contains(Context.Sender), $"DAO Member {Context.Sender} not found.");
            memberList.Value.Remove(Context.Sender);
            CreateProposalToAssociationContractAndRelease(nameof(State.AssociationContract.ChangeOrganizationMember), new OrganizationMemberList
            {
                OrganizationMembers = {memberList.Value}
            }.ToByteString());
            AdjustDAOProposalReleaseThreshold();
            return new Empty();
        }

        public override Empty ProposeExpel(Address input)
        {
            AssertReleasedByParliament();
            var memberList = State.DAOMemberList.Value;
            Assert(memberList.Value.Contains(input), $"DAO Member {input.Value} not found.");
            memberList.Value.Remove(input);
            CreateProposalToAssociationContractAndRelease(nameof(State.AssociationContract.ChangeOrganizationMember), new OrganizationMemberList
            {
                OrganizationMembers = {memberList.Value}
            }.ToByteString());
            AdjustDAOProposalReleaseThreshold();
            return new Empty();
        }
    }
}