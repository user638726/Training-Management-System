using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrainingManagement.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdvancedBusinessFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ApprovalPolicyId",
                table: "Courses",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BudgetId",
                table: "Courses",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CheckScheduleConflicts",
                table: "Courses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SeriesId",
                table: "Courses",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProvisioningClientId",
                table: "AppUsers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProvisioningExternalId",
                table: "AppUsers",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RequestedCost",
                table: "Approvals",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "WaitlistEntryId",
                table: "Approvals",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkflowId",
                table: "Approvals",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ApprovalPolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MinimumAmount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    StagesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CourseSeries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseSeries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CredentialTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CredentialTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Delegations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PrincipalUserId = table.Column<int>(type: "int", nullable: false),
                    DelegateUserId = table.Column<int>(type: "int", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Delegations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Delegations_AppUsers_DelegateUserId",
                        column: x => x.DelegateUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Delegations_AppUsers_PrincipalUserId",
                        column: x => x.PrincipalUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HrSyncSchedules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IntervalMinutes = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    AllowDeactivation = table.Column<bool>(type: "bit", nullable: false),
                    NextRunAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastRunAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastStatus = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LastSummary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrSyncSchedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntegrationClients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Scopes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationClients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntegrationEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpandedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Meetings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CourseId = table.Column<int>(type: "int", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Meetings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Meetings_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProvisioningLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClientId = table.Column<int>(type: "int", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ResourceId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProvisioningLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Skills",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Skills", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Waitlist",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CourseId = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    QueuedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OfferedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OfferExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Waitlist", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Waitlist_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Waitlist_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WebhookSubscriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Endpoint = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    EventTypes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ProtectedSecret = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookSubscriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Budgets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Department = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OwnerUserId = table.Column<int>(type: "int", nullable: false),
                    Allocated = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    ExpensePolicyId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Budgets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Budgets_AppUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Budgets_ApprovalPolicies_ExpensePolicyId",
                        column: x => x.ExpensePolicyId,
                        principalTable: "ApprovalPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Workflows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RelatedId = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    PolicyId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workflows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Workflows_ApprovalPolicies_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "ApprovalPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Workflows_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CredentialRequirements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CredentialTypeId = table.Column<int>(type: "int", nullable: false),
                    RenewalNoticeDays = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CredentialRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CredentialRequirements_CredentialTypes_CredentialTypeId",
                        column: x => x.CredentialTypeId,
                        principalTable: "CredentialTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExternalCredentials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    CredentialTypeId = table.Column<int>(type: "int", nullable: false),
                    Issuer = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CredentialNumber = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReplacesId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    ReviewerId = table.Column<int>(type: "int", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalCredentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalCredentials_CredentialTypes_CredentialTypeId",
                        column: x => x.CredentialTypeId,
                        principalTable: "CredentialTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExternalCredentials_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExternalCredentials_ExternalCredentials_ReplacesId",
                        column: x => x.ReplacesId,
                        principalTable: "ExternalCredentials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Activities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CourseId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ActivityIri = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    LaunchUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ClientId = table.Column<int>(type: "int", nullable: true),
                    RequiredSeconds = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Activities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Activities_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Activities_IntegrationClients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "IntegrationClients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProvisioningGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClientId = table.Column<int>(type: "int", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Department = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MembersJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProvisioningGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProvisioningGroups_IntegrationClients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "IntegrationClients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Goals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SkillId = table.Column<int>(type: "int", nullable: false),
                    TargetLevel = table.Column<int>(type: "int", nullable: false),
                    DueAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CourseIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Goals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Goals_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Goals_Skills_SkillId",
                        column: x => x.SkillId,
                        principalTable: "Skills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JobSkillRequirements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SkillId = table.Column<int>(type: "int", nullable: false),
                    TargetLevel = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobSkillRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobSkillRequirements_Skills_SkillId",
                        column: x => x.SkillId,
                        principalTable: "Skills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SkillAssessments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    SkillId = table.Column<int>(type: "int", nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false),
                    Evidence = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ReviewerId = table.Column<int>(type: "int", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SkillAssessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SkillAssessments_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SkillAssessments_Skills_SkillId",
                        column: x => x.SkillId,
                        principalTable: "Skills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SkillCourses",
                columns: table => new
                {
                    SkillId = table.Column<int>(type: "int", nullable: false),
                    CourseId = table.Column<int>(type: "int", nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SkillCourses", x => new { x.SkillId, x.CourseId });
                    table.ForeignKey(
                        name: "FK_SkillCourses_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SkillCourses_Skills_SkillId",
                        column: x => x.SkillId,
                        principalTable: "Skills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WebhookDeliveries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubscriptionId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LeaseUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LeaseToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastHttpStatus = table.Column<int>(type: "int", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebhookDeliveries_IntegrationEvents_EventId",
                        column: x => x.EventId,
                        principalTable: "IntegrationEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WebhookDeliveries_WebhookSubscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "WebhookSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Commitments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BudgetId = table.Column<int>(type: "int", nullable: false),
                    EnrollmentId = table.Column<int>(type: "int", nullable: false),
                    Attempt = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Commitments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Commitments_Budgets_BudgetId",
                        column: x => x.BudgetId,
                        principalTable: "Budgets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Commitments_Enrollments_EnrollmentId",
                        column: x => x.EnrollmentId,
                        principalTable: "Enrollments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalStages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkflowId = table.Column<int>(type: "int", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ApproverKind = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ApproverUserId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DueHours = table.Column<int>(type: "int", nullable: false),
                    DueAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActorUserId = table.Column<int>(type: "int", nullable: true),
                    RepresentedUserId = table.Column<int>(type: "int", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalStages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalStages_Workflows_WorkflowId",
                        column: x => x.WorkflowId,
                        principalTable: "Workflows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Expenses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    BudgetId = table.Column<int>(type: "int", nullable: false),
                    EnrollmentId = table.Column<int>(type: "int", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Vendor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    WorkflowId = table.Column<int>(type: "int", nullable: true),
                    ReceiptName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ReceiptType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Receipt = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewerId = table.Column<int>(type: "int", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Expenses_Budgets_BudgetId",
                        column: x => x.BudgetId,
                        principalTable: "Budgets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_Enrollments_EnrollmentId",
                        column: x => x.EnrollmentId,
                        principalTable: "Enrollments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_Workflows_WorkflowId",
                        column: x => x.WorkflowId,
                        principalTable: "Workflows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LearningRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    ActivityId = table.Column<int>(type: "int", nullable: false),
                    ClientId = table.Column<int>(type: "int", nullable: true),
                    VerbIri = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Score = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    DurationSeconds = table.Column<int>(type: "int", nullable: false),
                    Progress = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    StatementJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StatementHash = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StoredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsVoided = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearningRecords_Activities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "Activities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LearningRecords_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WebhookAttempts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeliveryId = table.Column<long>(type: "bigint", nullable: false),
                    HttpStatus = table.Column<int>(type: "int", nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebhookAttempts_WebhookDeliveries_DeliveryId",
                        column: x => x.DeliveryId,
                        principalTable: "WebhookDeliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Courses_ApprovalPolicyId",
                table: "Courses",
                column: "ApprovalPolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_Courses_BudgetId",
                table: "Courses",
                column: "BudgetId");

            migrationBuilder.CreateIndex(
                name: "IX_Courses_SeriesId",
                table: "Courses",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_ProvisioningClientId_ProvisioningExternalId",
                table: "AppUsers",
                columns: new[] { "ProvisioningClientId", "ProvisioningExternalId" },
                unique: true,
                filter: "[ProvisioningExternalId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Approvals_WaitlistEntryId",
                table: "Approvals",
                column: "WaitlistEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_Approvals_WorkflowId",
                table: "Approvals",
                column: "WorkflowId");

            migrationBuilder.CreateIndex(
                name: "IX_Activities_ActivityIri",
                table: "Activities",
                column: "ActivityIri",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Activities_ClientId",
                table: "Activities",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_Activities_CourseId",
                table: "Activities",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalStages_WorkflowId_Position",
                table: "ApprovalStages",
                columns: new[] { "WorkflowId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_ExpensePolicyId",
                table: "Budgets",
                column: "ExpensePolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_OwnerUserId",
                table: "Budgets",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Commitments_BudgetId",
                table: "Commitments",
                column: "BudgetId");

            migrationBuilder.CreateIndex(
                name: "IX_Commitments_EnrollmentId_Attempt",
                table: "Commitments",
                columns: new[] { "EnrollmentId", "Attempt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CredentialRequirements_CredentialTypeId",
                table: "CredentialRequirements",
                column: "CredentialTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_CredentialRequirements_JobTitle_CredentialTypeId",
                table: "CredentialRequirements",
                columns: new[] { "JobTitle", "CredentialTypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CredentialTypes_Name",
                table: "CredentialTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Delegations_DelegateUserId",
                table: "Delegations",
                column: "DelegateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Delegations_PrincipalUserId",
                table: "Delegations",
                column: "PrincipalUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_BudgetId",
                table: "Expenses",
                column: "BudgetId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_EmployeeId",
                table: "Expenses",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_EnrollmentId",
                table: "Expenses",
                column: "EnrollmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_WorkflowId",
                table: "Expenses",
                column: "WorkflowId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalCredentials_CredentialTypeId_CredentialNumber",
                table: "ExternalCredentials",
                columns: new[] { "CredentialTypeId", "CredentialNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalCredentials_EmployeeId",
                table: "ExternalCredentials",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalCredentials_ReplacesId",
                table: "ExternalCredentials",
                column: "ReplacesId");

            migrationBuilder.CreateIndex(
                name: "IX_Goals_EmployeeId",
                table: "Goals",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Goals_SkillId",
                table: "Goals",
                column: "SkillId");

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationClients_TokenHash",
                table: "IntegrationClients",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobSkillRequirements_JobTitle_SkillId",
                table: "JobSkillRequirements",
                columns: new[] { "JobTitle", "SkillId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobSkillRequirements_SkillId",
                table: "JobSkillRequirements",
                column: "SkillId");

            migrationBuilder.CreateIndex(
                name: "IX_LearningRecords_ActivityId",
                table: "LearningRecords",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_LearningRecords_EmployeeId_ActivityId_StoredAt",
                table: "LearningRecords",
                columns: new[] { "EmployeeId", "ActivityId", "StoredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Meetings_CourseId",
                table: "Meetings",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_ProvisioningGroups_ClientId_ExternalId",
                table: "ProvisioningGroups",
                columns: new[] { "ClientId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SkillAssessments_EmployeeId",
                table: "SkillAssessments",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_SkillAssessments_SkillId",
                table: "SkillAssessments",
                column: "SkillId");

            migrationBuilder.CreateIndex(
                name: "IX_SkillCourses_CourseId",
                table: "SkillCourses",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_Skills_Name",
                table: "Skills",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Waitlist_CourseId_EmployeeId",
                table: "Waitlist",
                columns: new[] { "CourseId", "EmployeeId" },
                unique: true,
                filter: "[Status] IN ('Waiting','Offered')");

            migrationBuilder.CreateIndex(
                name: "IX_Waitlist_EmployeeId",
                table: "Waitlist",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookAttempts_DeliveryId",
                table: "WebhookAttempts",
                column: "DeliveryId");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_EventId_SubscriptionId",
                table: "WebhookDeliveries",
                columns: new[] { "EventId", "SubscriptionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_Status_NextAttemptAt",
                table: "WebhookDeliveries",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_SubscriptionId",
                table: "WebhookDeliveries",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_Workflows_EmployeeId",
                table: "Workflows",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Workflows_PolicyId",
                table: "Workflows",
                column: "PolicyId");

            migrationBuilder.AddForeignKey(
                name: "FK_Approvals_Waitlist_WaitlistEntryId",
                table: "Approvals",
                column: "WaitlistEntryId",
                principalTable: "Waitlist",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Approvals_Workflows_WorkflowId",
                table: "Approvals",
                column: "WorkflowId",
                principalTable: "Workflows",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AppUsers_IntegrationClients_ProvisioningClientId",
                table: "AppUsers",
                column: "ProvisioningClientId",
                principalTable: "IntegrationClients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Courses_ApprovalPolicies_ApprovalPolicyId",
                table: "Courses",
                column: "ApprovalPolicyId",
                principalTable: "ApprovalPolicies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Courses_Budgets_BudgetId",
                table: "Courses",
                column: "BudgetId",
                principalTable: "Budgets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Courses_CourseSeries_SeriesId",
                table: "Courses",
                column: "SeriesId",
                principalTable: "CourseSeries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Approvals_Waitlist_WaitlistEntryId",
                table: "Approvals");

            migrationBuilder.DropForeignKey(
                name: "FK_Approvals_Workflows_WorkflowId",
                table: "Approvals");

            migrationBuilder.DropForeignKey(
                name: "FK_AppUsers_IntegrationClients_ProvisioningClientId",
                table: "AppUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_Courses_ApprovalPolicies_ApprovalPolicyId",
                table: "Courses");

            migrationBuilder.DropForeignKey(
                name: "FK_Courses_Budgets_BudgetId",
                table: "Courses");

            migrationBuilder.DropForeignKey(
                name: "FK_Courses_CourseSeries_SeriesId",
                table: "Courses");

            migrationBuilder.DropTable(
                name: "ApprovalStages");

            migrationBuilder.DropTable(
                name: "Commitments");

            migrationBuilder.DropTable(
                name: "CourseSeries");

            migrationBuilder.DropTable(
                name: "CredentialRequirements");

            migrationBuilder.DropTable(
                name: "Delegations");

            migrationBuilder.DropTable(
                name: "Expenses");

            migrationBuilder.DropTable(
                name: "ExternalCredentials");

            migrationBuilder.DropTable(
                name: "Goals");

            migrationBuilder.DropTable(
                name: "HrSyncSchedules");

            migrationBuilder.DropTable(
                name: "JobSkillRequirements");

            migrationBuilder.DropTable(
                name: "LearningRecords");

            migrationBuilder.DropTable(
                name: "Meetings");

            migrationBuilder.DropTable(
                name: "ProvisioningGroups");

            migrationBuilder.DropTable(
                name: "ProvisioningLogs");

            migrationBuilder.DropTable(
                name: "SkillAssessments");

            migrationBuilder.DropTable(
                name: "SkillCourses");

            migrationBuilder.DropTable(
                name: "Waitlist");

            migrationBuilder.DropTable(
                name: "WebhookAttempts");

            migrationBuilder.DropTable(
                name: "Budgets");

            migrationBuilder.DropTable(
                name: "Workflows");

            migrationBuilder.DropTable(
                name: "CredentialTypes");

            migrationBuilder.DropTable(
                name: "Activities");

            migrationBuilder.DropTable(
                name: "Skills");

            migrationBuilder.DropTable(
                name: "WebhookDeliveries");

            migrationBuilder.DropTable(
                name: "ApprovalPolicies");

            migrationBuilder.DropTable(
                name: "IntegrationClients");

            migrationBuilder.DropTable(
                name: "IntegrationEvents");

            migrationBuilder.DropTable(
                name: "WebhookSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_Courses_ApprovalPolicyId",
                table: "Courses");

            migrationBuilder.DropIndex(
                name: "IX_Courses_BudgetId",
                table: "Courses");

            migrationBuilder.DropIndex(
                name: "IX_Courses_SeriesId",
                table: "Courses");

            migrationBuilder.DropIndex(
                name: "IX_AppUsers_ProvisioningClientId_ProvisioningExternalId",
                table: "AppUsers");

            migrationBuilder.DropIndex(
                name: "IX_Approvals_WaitlistEntryId",
                table: "Approvals");

            migrationBuilder.DropIndex(
                name: "IX_Approvals_WorkflowId",
                table: "Approvals");

            migrationBuilder.DropColumn(
                name: "ApprovalPolicyId",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "BudgetId",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "CheckScheduleConflicts",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "SeriesId",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "ProvisioningClientId",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "ProvisioningExternalId",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "RequestedCost",
                table: "Approvals");

            migrationBuilder.DropColumn(
                name: "WaitlistEntryId",
                table: "Approvals");

            migrationBuilder.DropColumn(
                name: "WorkflowId",
                table: "Approvals");
        }
    }
}
