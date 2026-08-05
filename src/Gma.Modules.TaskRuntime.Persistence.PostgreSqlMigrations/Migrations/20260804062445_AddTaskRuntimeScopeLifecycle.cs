using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gma.Modules.TaskRuntime.Persistence.PostgreSqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskRuntimeScopeLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ScopeId",
                schema: "tasks",
                table: "task_control_messages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE tasks.task_control_messages AS message
                SET "ScopeId" = run."ScopeId"
                FROM tasks.task_runs AS run
                WHERE run."Id" = message."RunId";

                DELETE FROM tasks.task_control_messages AS message
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM tasks.task_runs AS run
                    WHERE run."Id" = message."RunId");
                """);

            migrationBuilder.CreateTable(
                name: "task_scope_destroy_operations",
                schema: "tasks",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    SelectedRevision = table.Column<long>(type: "bigint", nullable: false),
                    ResultingRevision = table.Column<long>(type: "bigint", nullable: false),
                    BatchSize = table.Column<int>(type: "integer", nullable: false),
                    Stage = table.Column<int>(type: "integer", nullable: false),
                    RemovedRecordCount = table.Column<long>(type: "bigint", nullable: false),
                    CompletedBatchCount = table.Column<int>(type: "integer", nullable: false),
                    ProofVersion = table.Column<int>(type: "integer", nullable: false),
                    RemovalProofSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_scope_destroy_operations", x => x.OperationId);
                    table.CheckConstraint("CK_task_scope_destroy_operations_batch", "\"BatchSize\" >= 1 AND \"BatchSize\" <= 1000");
                    table.CheckConstraint("CK_task_scope_destroy_operations_counts", "\"RemovedRecordCount\" >= 0 AND \"CompletedBatchCount\" >= 0");
                    table.CheckConstraint("CK_task_scope_destroy_operations_revision", "\"SelectedRevision\" >= 0 AND \"ResultingRevision\" = \"SelectedRevision\" + 1");
                    table.CheckConstraint("CK_task_scope_destroy_operations_stage", "\"Stage\" >= 1 AND \"Stage\" <= 4");
                });

            migrationBuilder.CreateTable(
                name: "task_scope_destroy_receipts",
                schema: "tasks",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    SelectedRevision = table.Column<long>(type: "bigint", nullable: false),
                    ResultingRevision = table.Column<long>(type: "bigint", nullable: false),
                    BatchSize = table.Column<int>(type: "integer", nullable: false),
                    RemovedRecordCount = table.Column<long>(type: "bigint", nullable: false),
                    CompletedBatchCount = table.Column<int>(type: "integer", nullable: false),
                    RemovalProofVersion = table.Column<int>(type: "integer", nullable: false),
                    RemovalProofSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_scope_destroy_receipts", x => x.OperationId);
                    table.CheckConstraint("CK_task_scope_destroy_receipts_batch", "\"BatchSize\" >= 1 AND \"BatchSize\" <= 1000");
                    table.CheckConstraint("CK_task_scope_destroy_receipts_counts", "\"RemovedRecordCount\" >= 0 AND \"CompletedBatchCount\" >= 0");
                    table.CheckConstraint("CK_task_scope_destroy_receipts_revision", "\"SelectedRevision\" >= 0 AND \"ResultingRevision\" = \"SelectedRevision\" + 1");
                });

            migrationBuilder.CreateTable(
                name: "task_scope_states",
                schema: "tasks",
                columns: table => new
                {
                    ScopeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    SelectedRevision = table.Column<long>(type: "bigint", nullable: false),
                    ResultingRevision = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConcurrencyVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_scope_states", x => x.ScopeId);
                    table.CheckConstraint("CK_task_scope_states_revision", "\"SelectedRevision\" >= 0 AND \"ResultingRevision\" = \"SelectedRevision\" + 1");
                    table.CheckConstraint("CK_task_scope_states_status", "\"Status\" IN (1, 2)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_task_control_messages_ScopeId_Id",
                schema: "tasks",
                table: "task_control_messages",
                columns: new[] { "ScopeId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_task_scope_destroy_operations_ScopeId",
                schema: "tasks",
                table: "task_scope_destroy_operations",
                column: "ScopeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_task_scope_destroy_receipts_ScopeId",
                schema: "tasks",
                table: "task_scope_destroy_receipts",
                column: "ScopeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_task_scope_states_OperationId",
                schema: "tasks",
                table: "task_scope_states",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_task_scope_states_Status_ScopeId",
                schema: "tasks",
                table: "task_scope_states",
                columns: new[] { "Status", "ScopeId" });

            migrationBuilder.AddForeignKey(
                name: "FK_task_control_messages_task_runs_RunId",
                schema: "tasks",
                table: "task_control_messages",
                column: "RunId",
                principalSchema: "tasks",
                principalTable: "task_runs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_task_control_messages_task_runs_RunId",
                schema: "tasks",
                table: "task_control_messages");

            migrationBuilder.DropTable(
                name: "task_scope_destroy_operations",
                schema: "tasks");

            migrationBuilder.DropTable(
                name: "task_scope_destroy_receipts",
                schema: "tasks");

            migrationBuilder.DropTable(
                name: "task_scope_states",
                schema: "tasks");

            migrationBuilder.DropIndex(
                name: "IX_task_control_messages_ScopeId_Id",
                schema: "tasks",
                table: "task_control_messages");

            migrationBuilder.DropColumn(
                name: "ScopeId",
                schema: "tasks",
                table: "task_control_messages");
        }
    }
}
