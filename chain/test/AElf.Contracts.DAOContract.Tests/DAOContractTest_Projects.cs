using System.Collections.Generic;
using System.Threading.Tasks;
using AElf.Contracts.MultiToken;
using AElf.Contracts.Profit;
using AElf.Types;
using Shouldly;
using Xunit;

namespace AElf.Contracts.DAOContract
{
    public partial class DaoContractTest
    {
        private const string GrantProjectPullRequestUrl = "https://github.com/AElfProject/AElf/pull/111111";
        private const string GrantProjectCommitId = "grant_project_commit_id";
        private const string GrantProjectDeliverPullRequestUrl = "https://github.com/AElfProject/AElf/pull/222222";
        private const string GrantProjectDeliverCommitId = "grant_project_deliver_commit_id";
        private const string BountyProjectPullRequestUrl = "https://github.com/AElfProject/AElf/pull/333333";
        private const string BountyProjectCommitId = "bounty_project_commit_id";
        private const string BountyProjectDeliverPullRequestUrl = "https://github.com/AElfProject/AElf/pull/444444";
        private const string BountyProjectDeliverCommitId = "bounty_project_deliver_commit_id";

        private const long InvestAmount = 1000_00000000;

        private List<BudgetPlan> BudgetPlans => new List<BudgetPlan>
        {
            new BudgetPlan
            {
                Index = 0,
                Phase = 1,
                Symbol = "ELF",
                Amount = 1000_00000000,
                ReceiverAddress = AliceAddress
            }
        };

        private List<BudgetPlan> BountyBudgetPlans => new List<BudgetPlan>
        {
            new BudgetPlan
            {
                Index = 1,
                Phase = 2,
                Symbol = "ELF",
                Amount = 500_00000000
            },
            new BudgetPlan
            {
                Index = 0,
                Phase = 1,
                Symbol = "ELF",
                Amount = 500_00000000
            }
        };

        [Fact]
        public async Task<Hash> ProposeProjectToDAO_Test()
        {
            await InitialDAOContract();

            // Alice want to propose a project to DAO.
            var proposalId = (await AliceDAOContractStub.ProposeProjectToDAO.SendAsync(new ProposeProjectInput
            {
                PullRequestUrl = GrantProjectPullRequestUrl,
                CommitId = GrantProjectCommitId
            })).Output;

            // Check proposal exists and correct.
            var proposalInfo = await AssociationContractStub.GetProposal.CallAsync(proposalId);
            proposalInfo.ContractMethodName.ShouldBe(nameof(DAOContractStub.AddProject));
            proposalInfo.ToAddress.ShouldBe(DAOContractAddress);

            return proposalId;
        }

        [Fact]
        public async Task<Hash> ProposeProjectToDAO_Approve_Test()
        {
            var proposalId = await ProposeProjectToDAO_Test();
            var projectId = await DAOContractStub.CalculateProjectId.CallAsync(new ProposeProjectInput
            {
                PullRequestUrl = GrantProjectPullRequestUrl,
                CommitId = GrantProjectCommitId
            });

            await DAOApproveAsync(proposalId);
            // Anyone call this method to release this proposal.
            await DAOContractStub.ReleaseProposal.SendAsync(new ReleaseProposalInput
            {
                ProjectId = projectId,
                ProposalId = proposalId,
                OrganizationType = ProposalOrganizationType.DAO
            });

            // Check project info.
            var projectInfo = await DAOContractStub.GetProjectInfo.CallAsync(projectId);
            projectInfo.PullRequestUrl.ShouldBe(GrantProjectPullRequestUrl);
            projectInfo.CommitId.ShouldBe(GrantProjectCommitId);
            projectInfo.VirtualAddress.ShouldNotBeNull();
            projectInfo.Status.ShouldBe(ProjectStatus.Proposed);

            return projectId;
        }

        [Fact]
        public async Task<Hash> ProposeProjectToParliament_Test()
        {
            var projectId = await ProposeProjectToDAO_Approve_Test();

            // After approved by DAO, Alice propose this project to Parliament.
            var proposalId = (await AliceDAOContractStub.ProposeProjectToParliament.SendAsync(
                new ProposeProjectWithBudgetsInput
                {
                    ProjectId = projectId,
                    BudgetPlans = {BudgetPlans}
                })).Output;

            await CheckProjectStatus(projectId, ProjectStatus.Proposed);

            await ParliamentApproveAsync(proposalId);

            // Anyone call this method to release this proposal.
            await DAOContractStub.ReleaseProposal.SendAsync(new ReleaseProposalInput
            {
                ProjectId = projectId,
                ProposalId = proposalId,
                OrganizationType = ProposalOrganizationType.Parliament
            });

            // Check project info.
            var projectInfo = await DAOContractStub.GetProjectInfo.CallAsync(projectId);
            projectInfo.PullRequestUrl.ShouldBe(GrantProjectPullRequestUrl);
            projectInfo.CommitId.ShouldBe(GrantProjectCommitId);
            projectInfo.Status.ShouldBe(ProjectStatus.Approved);
            projectInfo.ProfitSchemeId.ShouldNotBeNull();
            projectInfo.BudgetPlans.ShouldBe(BudgetPlans);
            projectInfo.VirtualAddress.ShouldNotBeNull();

            return projectId;
        }

        [Fact]
        public async Task<Hash> InvestToGrantProjectTest()
        {
            var projectId = await ProposeProjectToParliament_Test();

            await CheckProjectStatus(projectId, ProjectStatus.Approved);

            await AliceTokenContractStub.Approve.SendAsync(new ApproveInput
            {
                Spender = DAOContractAddress,
                Symbol = "ELF",
                Amount = InvestAmount
            });
            await AliceDAOContractStub.Invest.SendAsync(new InvestInput
            {
                ProjectId = projectId,
                Symbol = "ELF",
                Amount = InvestAmount
            });

            var budgetPlan = await DAOContractStub.GetBudgetPlan.CallAsync(new GetBudgetPlanInput
            {
                ProjectId = projectId,
                BudgetPlanIndex = 0
            });
            budgetPlan.Amount.ShouldBe(InvestAmount);
            budgetPlan.PaidInAmount.ShouldBe(InvestAmount);
            budgetPlan.Phase.ShouldBe(1);

            await CheckProjectStatus(projectId, ProjectStatus.Ready);

            return projectId;
        }

        [Fact]
        public async Task DeliverGrantProjectTest()
        {
            var projectId = await InvestToGrantProjectTest();

            // Alice want to deliver project.
            var proposalId = (await AliceDAOContractStub.ProposeDeliver.SendAsync(new ProposeAuditionInput
            {
                ProjectId = projectId,
                DeliverPullRequestUrl = GrantProjectDeliverPullRequestUrl,
                DeliverCommitId = GrantProjectDeliverCommitId,
                BudgetPlanIndex = 0
            })).Output;

            await CheckProjectStatus(projectId, ProjectStatus.Ready);

            await DAOApproveAsync(proposalId);
            await DAOContractStub.ReleaseProposal.SendAsync(new ReleaseProposalInput
            {
                ProjectId = projectId,
                ProposalId = proposalId,
                OrganizationType = ProposalOrganizationType.DAO
            });

            await CheckProjectStatus(projectId, ProjectStatus.Delivered);

            var budgetPlan = await DAOContractStub.GetBudgetPlan.CallAsync(new GetBudgetPlanInput
            {
                ProjectId = projectId,
                BudgetPlanIndex = 0
            });
            budgetPlan.DeliverPullRequestUrl.ShouldNotBeEmpty();
            budgetPlan.DeliverCommitId.ShouldNotBeEmpty();

            {
                var balance = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
                {
                    Owner = AliceAddress,
                    Symbol = "ELF"
                });
                balance.Balance.ShouldBe(10_0000_0000_00000000 - InvestAmount);
            }

            // Alice gonna take rewards.
            var projectInfo = await DAOContractStub.GetProjectInfo.CallAsync(projectId);
            var result = await AliceProfitContractStub.ClaimProfits.SendAsync(new ClaimProfitsInput
            {
                SchemeId = projectInfo.ProfitSchemeId,
                Beneficiary = AliceAddress
            });
            result.TransactionResult.Status.ShouldBe(TransactionResultStatus.Mined);

            {
                var balance = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
                {
                    Owner = AliceAddress,
                    Symbol = "ELF"
                });
                balance.Balance.ShouldBe(10_0000_0000_00000000);
            }
        }

        [Fact]
        public async Task<Hash> ProposeBountyProject_Test()
        {
            await InitialDAOContract();

            var proposeProjectInput = new ProposeProjectInput
            {
                PullRequestUrl = BountyProjectPullRequestUrl,
                CommitId = BountyProjectCommitId,
                IsDevelopersAuditionRequired = true
            };
            // One DAO member propose a bounty project.
            var proposalId = (await DAOContractStub.ProposeBountyProject.SendAsync(proposeProjectInput)).Output;

            await DAOApproveAsync(proposalId);

            var projectId = await DAOContractStub.CalculateProjectId.CallAsync(proposeProjectInput);

            await DAOContractStub.ReleaseProposal.SendAsync(new ReleaseProposalInput
            {
                ProjectId = projectId,
                ProposalId = proposalId,
                OrganizationType = ProposalOrganizationType.DAO
            });

            // Check project info.
            var projectInfo = await DAOContractStub.GetProjectInfo.CallAsync(projectId);
            projectInfo.PullRequestUrl.ShouldBe(BountyProjectPullRequestUrl);
            projectInfo.CommitId.ShouldBe(BountyProjectCommitId);
            projectInfo.VirtualAddress.ShouldNotBeNull();
            projectInfo.Status.ShouldBe(ProjectStatus.Proposed);

            return projectId;
        }

        public async Task<Hash> ProposeIssueBountyProject_Test()
        {
            var projectId = await ProposeBountyProject_Test();

            // DAO member propose this bounty project with budget plans.
            var proposalId = (await DAOContractStub.ProposeIssueBountyProject.SendAsync(
                new ProposeProjectWithBudgetsInput
                {
                    ProjectId = projectId,
                    BudgetPlans = {BountyBudgetPlans}
                })).Output;

            await CheckProjectStatus(projectId, ProjectStatus.Proposed);

            await ParliamentApproveAsync(proposalId);

            // Anyone call this method to release this proposal.
            await DAOContractStub.ReleaseProposal.SendAsync(new ReleaseProposalInput
            {
                ProjectId = projectId,
                ProposalId = proposalId,
                OrganizationType = ProposalOrganizationType.Parliament
            });

            await CheckProjectStatus(projectId, ProjectStatus.Approved);

            return projectId;
        }

        [Fact]
        public async Task<Hash> InvestToBountyProjectTest()
        {
            var projectId = await ProposeIssueBountyProject_Test();

            await CheckProjectStatus(projectId, ProjectStatus.Approved);

            await AliceTokenContractStub.Approve.SendAsync(new ApproveInput
            {
                Spender = DAOContractAddress,
                Symbol = "ELF",
                Amount = InvestAmount
            });
            await AliceDAOContractStub.Invest.SendAsync(new InvestInput
            {
                ProjectId = projectId,
                Symbol = "ELF",
                Amount = InvestAmount
            });

            await CheckProjectStatus(projectId, ProjectStatus.Ready);

            return projectId;
        }

        [Fact]
        public async Task<Hash> TakeOverBountyProjectTest()
        {
            var projectId = await InvestToBountyProjectTest();

            // Bob wants to take over phase 1.
            {
                var proposalId = (await BobDAOContractStub.ProposeTakeOverBountyProject.SendAsync(
                    new ProposeTakeOverBountyProjectInput
                    {
                        ProjectId = projectId,
                        BudgetPlanIndices = {0}
                    })).Output;

                await DAOApproveAsync(proposalId);

                await BobDAOContractStub.ReleaseProposal.SendAsync(new ReleaseProposalInput
                {
                    ProjectId = projectId,
                    ProposalId = proposalId,
                    OrganizationType = ProposalOrganizationType.DAO
                });

                await CheckProjectStatus(projectId, ProjectStatus.Ready);
            }

            // Ean wants to take over phase 2.

            {
                var proposalId = (await EanDAOContractStub.ProposeTakeOverBountyProject.SendAsync(
                    new ProposeTakeOverBountyProjectInput
                    {
                        ProjectId = projectId,
                        BudgetPlanIndices = {1}
                    })).Output;

                await DAOApproveAsync(proposalId);

                await EanDAOContractStub.ReleaseProposal.SendAsync(new ReleaseProposalInput
                {
                    ProjectId = projectId,
                    ProposalId = proposalId,
                    OrganizationType = ProposalOrganizationType.DAO
                });

                await CheckProjectStatus(projectId, ProjectStatus.Taken);
            }

            return projectId;
        }

        [Fact]
        public async Task<Hash> DevelopersAuditionTest()
        {
            var projectId = await TakeOverBountyProjectTest();

            // Bob wants to commit his works for developers to audit.
            {
                var proposalId = (await BobDAOContractStub.ProposeDevelopersAudition.SendAsync(new ProposeAuditionInput
                {
                    ProjectId = projectId,
                    DeliverPullRequestUrl = BountyProjectDeliverPullRequestUrl,
                    DeliverCommitId = BountyProjectDeliverCommitId,
                    BudgetPlanIndex = 0
                })).Output;

                // Bob approves himself.
                await BobAssociationContractStub.Approve.SendAsync(proposalId);

                await DAOContractStub.ReleaseProposal.SendAsync(new ReleaseProposalInput
                {
                    ProjectId = projectId,
                    ProposalId = proposalId,
                    OrganizationType = ProposalOrganizationType.Developers
                });

                await CheckProjectStatus(projectId, ProjectStatus.Taken);
            }

            {
                // Ean wants to commit his works for developers to audit.
                {
                    var proposalId = (await EanDAOContractStub.ProposeDevelopersAudition.SendAsync(
                        new ProposeAuditionInput
                        {
                            ProjectId = projectId,
                            DeliverPullRequestUrl = BountyProjectDeliverPullRequestUrl,
                            DeliverCommitId = BountyProjectDeliverCommitId,
                            BudgetPlanIndex = 1
                        })).Output;

                    // Ean approves himself.
                    await EanAssociationContractStub.Approve.SendAsync(proposalId);

                    await DAOContractStub.ReleaseProposal.SendAsync(new ReleaseProposalInput
                    {
                        ProjectId = projectId,
                        ProposalId = proposalId,
                        OrganizationType = ProposalOrganizationType.Developers
                    });

                    await CheckProjectStatus(projectId, ProjectStatus.Taken);
                }
            }

            return projectId;
        }

        [Fact]
        public async Task DeliverBountyProjectWithoutDevelopersAuditionTest()
        {
            var projectId = await TakeOverBountyProjectTest();

            // Bob want to deliver project.
            var txResult = (await BobDAOContractStub.ProposeDeliver.SendWithExceptionAsync(new ProposeAuditionInput
            {
                ProjectId = projectId,
                DeliverPullRequestUrl = BountyProjectDeliverPullRequestUrl,
                DeliverCommitId = BountyProjectDeliverCommitId,
                BudgetPlanIndex = 0
            })).TransactionResult;

            txResult.Error.ShouldContain("Project budget plans need to approved by developers before deliver.");
        }

        [Fact]
        public async Task DeliverBountyProjectTest()
        {
            var projectId = await DevelopersAuditionTest();

            // Bob wants to deliver project.
            {
                var proposalId = (await BobDAOContractStub.ProposeDeliver.SendAsync(new ProposeAuditionInput
                {
                    ProjectId = projectId,
                    DeliverPullRequestUrl = BountyProjectDeliverPullRequestUrl,
                    DeliverCommitId = BountyProjectDeliverCommitId,
                    BudgetPlanIndex = 0
                })).Output;

                await CheckProjectStatus(projectId, ProjectStatus.Taken);

                await DAOApproveAsync(proposalId);
                await DAOContractStub.ReleaseProposal.SendAsync(new ReleaseProposalInput
                {
                    ProjectId = projectId,
                    ProposalId = proposalId,
                    OrganizationType = ProposalOrganizationType.DAO
                });

                await CheckProjectStatus(projectId, ProjectStatus.Taken);

                var budgetPlan = await DAOContractStub.GetBudgetPlan.CallAsync(new GetBudgetPlanInput
                {
                    ProjectId = projectId,
                    BudgetPlanIndex = 0
                });
                budgetPlan.DeliverPullRequestUrl.ShouldNotBeEmpty();
                budgetPlan.DeliverCommitId.ShouldNotBeEmpty();

                {
                    var balance = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
                    {
                        Owner = BobAddress,
                        Symbol = "ELF"
                    });
                    balance.Balance.ShouldBe(0);
                }

                var projectInfo = await DAOContractStub.GetProjectInfo.CallAsync(projectId);

                {
                    var balance = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
                    {
                        Owner = projectInfo.VirtualAddress,
                        Symbol = "ELF"
                    });
                    balance.Balance.ShouldBe(InvestAmount / 2);
                }

                // Bob takes rewards.
                await BobProfitContractStub.ClaimProfits.SendAsync(new ClaimProfitsInput
                {
                    SchemeId = projectInfo.ProfitSchemeId,
                    Beneficiary = BobAddress
                });

                {
                    var balance = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
                    {
                        Owner = BobAddress,
                        Symbol = "ELF"
                    });
                    balance.Balance.ShouldBe(InvestAmount / 2);
                }
            }

            // Ean wants to deliver project.
            {
                var proposalId = (await EanDAOContractStub.ProposeDeliver.SendAsync(new ProposeAuditionInput
                {
                    ProjectId = projectId,
                    DeliverPullRequestUrl = BountyProjectDeliverPullRequestUrl,
                    DeliverCommitId = BountyProjectDeliverCommitId,
                    BudgetPlanIndex = 1
                })).Output;

                await CheckProjectStatus(projectId, ProjectStatus.Taken);

                await DAOApproveAsync(proposalId);
                await DAOContractStub.ReleaseProposal.SendAsync(new ReleaseProposalInput
                {
                    ProjectId = projectId,
                    ProposalId = proposalId,
                    OrganizationType = ProposalOrganizationType.DAO
                });

                await CheckProjectStatus(projectId, ProjectStatus.Delivered);

                var budgetPlan = await DAOContractStub.GetBudgetPlan.CallAsync(new GetBudgetPlanInput
                {
                    ProjectId = projectId,
                    BudgetPlanIndex = 0
                });
                budgetPlan.DeliverPullRequestUrl.ShouldNotBeEmpty();
                budgetPlan.DeliverCommitId.ShouldNotBeEmpty();

                {
                    var balance = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
                    {
                        Owner = EanAddress,
                        Symbol = "ELF"
                    });
                    balance.Balance.ShouldBe(0);
                }

                var projectInfo = await DAOContractStub.GetProjectInfo.CallAsync(projectId);

                {
                    var balance = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
                    {
                        Owner = projectInfo.VirtualAddress,
                        Symbol = "ELF"
                    });
                    balance.Balance.ShouldBe(0);
                }

                // Ean takes rewards.
                await EanProfitContractStub.ClaimProfits.SendAsync(new ClaimProfitsInput
                {
                    SchemeId = projectInfo.ProfitSchemeId,
                    Beneficiary = EanAddress
                });

                {
                    var balance = await TokenContractStub.GetBalance.CallAsync(new GetBalanceInput
                    {
                        Owner = EanAddress,
                        Symbol = "ELF"
                    });
                    balance.Balance.ShouldBe(InvestAmount / 2);
                }
            }
        }

        private async Task CheckProjectStatus(Hash projectId, ProjectStatus status)
        {
            status.ShouldBe((await DAOContractStub.GetProjectInfo.CallAsync(projectId)).Status);
        }
    }
}