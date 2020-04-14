using System.Collections.Generic;
using System.Linq;
using Acs0;
using AElf.OS.Node.Application;
using AElf.Types;
using AElf.Contracts.DAOContract;

namespace AElf.Blockchains.MainChain
{
    // ReSharper disable InconsistentNaming
    public partial class GenesisSmartContractDtoProvider
    {
        public IEnumerable<GenesisSmartContractDto> GetGenesisSmartContractDtosForDAO()
        {
            var l = new List<GenesisSmartContractDto>();

            l.AddGenesisSmartContract(
                _codes.Single(kv => kv.Key.Contains("DAO")).Value,
                Hash.FromString("AElf.ContractNames.DAOContract"), GenerateDAOInitializationCallList());

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
}