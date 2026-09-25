using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotorDecisao.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "usebens_motor_decisao");

            migrationBuilder.CreateTable(
                name: "decision_flows",
                schema: "usebens_motor_decisao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_decision_flows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "flow_versions",
                schema: "usebens_motor_decisao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DecisionFlowId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ChangeLog = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flow_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_flow_versions_decision_flows_DecisionFlowId",
                        column: x => x.DecisionFlowId,
                        principalSchema: "usebens_motor_decisao",
                        principalTable: "decision_flows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "decision_executions",
                schema: "usebens_motor_decisao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FlowVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposalReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    InputData = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Score = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_decision_executions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_decision_executions_flow_versions_FlowVersionId",
                        column: x => x.FlowVersionId,
                        principalSchema: "usebens_motor_decisao",
                        principalTable: "flow_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "flow_edges",
                schema: "usebens_motor_decisao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FlowVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EdgeKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SourceNodeKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TargetNodeKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SourceHandle = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flow_edges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_flow_edges_flow_versions_FlowVersionId",
                        column: x => x.FlowVersionId,
                        principalSchema: "usebens_motor_decisao",
                        principalTable: "flow_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "formulas",
                schema: "usebens_motor_decisao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FlowVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Expression = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_formulas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_formulas_flow_versions_FlowVersionId",
                        column: x => x.FlowVersionId,
                        principalSchema: "usebens_motor_decisao",
                        principalTable: "flow_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rulesets",
                schema: "usebens_motor_decisao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FlowVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ApprovalThreshold = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rulesets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rulesets_flow_versions_FlowVersionId",
                        column: x => x.FlowVersionId,
                        principalSchema: "usebens_motor_decisao",
                        principalTable: "flow_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "execution_traces",
                schema: "usebens_motor_decisao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DecisionExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    NodeKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NodeLabel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Expression = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Result = table.Column<string>(type: "jsonb", nullable: true),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_execution_traces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_execution_traces_decision_executions_DecisionExecutionId",
                        column: x => x.DecisionExecutionId,
                        principalSchema: "usebens_motor_decisao",
                        principalTable: "decision_executions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "flow_nodes",
                schema: "usebens_motor_decisao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FlowVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    NodeKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PositionX = table.Column<double>(type: "double precision", nullable: false),
                    PositionY = table.Column<double>(type: "double precision", nullable: false),
                    Config = table.Column<string>(type: "jsonb", nullable: false),
                    RulesetId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flow_nodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_flow_nodes_flow_versions_FlowVersionId",
                        column: x => x.FlowVersionId,
                        principalSchema: "usebens_motor_decisao",
                        principalTable: "flow_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_flow_nodes_rulesets_RulesetId",
                        column: x => x.RulesetId,
                        principalSchema: "usebens_motor_decisao",
                        principalTable: "rulesets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "rules",
                schema: "usebens_motor_decisao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RulesetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ConditionExpression = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Effect = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ScoreWeight = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    ForcedOutcome = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rules_rulesets_RulesetId",
                        column: x => x.RulesetId,
                        principalSchema: "usebens_motor_decisao",
                        principalTable: "rulesets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_decision_executions_CreatedAt",
                schema: "usebens_motor_decisao",
                table: "decision_executions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_decision_executions_FlowVersionId",
                schema: "usebens_motor_decisao",
                table: "decision_executions",
                column: "FlowVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_decision_executions_ProposalReference",
                schema: "usebens_motor_decisao",
                table: "decision_executions",
                column: "ProposalReference");

            migrationBuilder.CreateIndex(
                name: "IX_decision_flows_Name",
                schema: "usebens_motor_decisao",
                table: "decision_flows",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_execution_traces_DecisionExecutionId_Sequence",
                schema: "usebens_motor_decisao",
                table: "execution_traces",
                columns: new[] { "DecisionExecutionId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_flow_edges_FlowVersionId_EdgeKey",
                schema: "usebens_motor_decisao",
                table: "flow_edges",
                columns: new[] { "FlowVersionId", "EdgeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_flow_nodes_FlowVersionId_NodeKey",
                schema: "usebens_motor_decisao",
                table: "flow_nodes",
                columns: new[] { "FlowVersionId", "NodeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_flow_nodes_RulesetId",
                schema: "usebens_motor_decisao",
                table: "flow_nodes",
                column: "RulesetId");

            migrationBuilder.CreateIndex(
                name: "IX_flow_versions_DecisionFlowId_VersionNumber",
                schema: "usebens_motor_decisao",
                table: "flow_versions",
                columns: new[] { "DecisionFlowId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_formulas_FlowVersionId_Key",
                schema: "usebens_motor_decisao",
                table: "formulas",
                columns: new[] { "FlowVersionId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rules_RulesetId_Order",
                schema: "usebens_motor_decisao",
                table: "rules",
                columns: new[] { "RulesetId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_rulesets_FlowVersionId",
                schema: "usebens_motor_decisao",
                table: "rulesets",
                column: "FlowVersionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "execution_traces",
                schema: "usebens_motor_decisao");

            migrationBuilder.DropTable(
                name: "flow_edges",
                schema: "usebens_motor_decisao");

            migrationBuilder.DropTable(
                name: "flow_nodes",
                schema: "usebens_motor_decisao");

            migrationBuilder.DropTable(
                name: "formulas",
                schema: "usebens_motor_decisao");

            migrationBuilder.DropTable(
                name: "rules",
                schema: "usebens_motor_decisao");

            migrationBuilder.DropTable(
                name: "decision_executions",
                schema: "usebens_motor_decisao");

            migrationBuilder.DropTable(
                name: "rulesets",
                schema: "usebens_motor_decisao");

            migrationBuilder.DropTable(
                name: "flow_versions",
                schema: "usebens_motor_decisao");

            migrationBuilder.DropTable(
                name: "decision_flows",
                schema: "usebens_motor_decisao");
        }
    }
}
