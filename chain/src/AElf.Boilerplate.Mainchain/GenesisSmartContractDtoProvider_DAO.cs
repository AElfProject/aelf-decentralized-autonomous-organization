using System.Collections.Generic;
using System.Linq;
using Acs0;
using AElf.OS.Node.Application;
using AElf.Types;
using AElf.Contracts.DAOContract;
using AElf.Kernel.Infrastructure;
using AElf.Kernel.SmartContract;
using Volo.Abp.DependencyInjection;

namespace AElf.Blockchains.MainChain
{
    public partial class GenesisSmartContractDtoProvider
    {
        public IEnumerable<GenesisSmartContractDto> GetGenesisSmartContractDtosForDAO()
        {
            var l = new List<GenesisSmartContractDto>();

            l.AddGenesisSmartContract(
                _codes.Single(kv => kv.Key.Contains("DAO")).Value,
                DAOContractAddressNameProvider.Name, GenerateDAOInitializationCallList());

            return l;
        }

        private SystemContractDeploymentInput.Types.SystemTransactionMethodCallList
            GenerateDAOInitializationCallList()
        {
            var bingoGameContractMethodCallList =
                new SystemContractDeploymentInput.Types.SystemTransactionMethodCallList();
            bingoGameContractMethodCallList.Add(
                nameof(DAOContractContainer.DAOContractStub.Initialize),
                new InitializeInput
                {
                    DepositAmount = 10_0000_00000000
                });
            return bingoGameContractMethodCallList;
        }
    }

    public class DAOContractAddressNameProvider : ISmartContractAddressNameProvider, ISingletonDependency
    {
        public static readonly Hash Name = HashHelper.ComputeFrom("AElf.ContractNames.DAO");
        public static readonly string StringName = Name.ToStorageKey();
        public Hash ContractName => Name;
        public string ContractStringName => StringName;
    }
}