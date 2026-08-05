using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gma.Modules.TaskRuntime.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class HardenTaskRuntimeScopeLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_task_scope_states_status",
                schema: "tasks",
                table: "task_scope_states");

            migrationBuilder.DropCheckConstraint(
                name: "CK_task_scope_destroy_receipts_counts",
                schema: "tasks",
                table: "task_scope_destroy_receipts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_task_scope_destroy_operations_counts",
                schema: "tasks",
                table: "task_scope_destroy_operations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_task_scope_states_progress",
                schema: "tasks",
                table: "task_scope_states",
                sql: "\"ConcurrencyVersion\" >= 1 AND \"UpdatedAtUtc\" >= \"StartedAtUtc\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_task_scope_states_status",
                schema: "tasks",
                table: "task_scope_states",
                sql: "(\"Status\" = 1 AND \"ClosedAtUtc\" IS NULL) OR (\"Status\" = 2 AND \"ClosedAtUtc\" IS NOT NULL AND \"ClosedAtUtc\" = \"UpdatedAtUtc\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_task_scope_destroy_receipts_counts",
                schema: "tasks",
                table: "task_scope_destroy_receipts",
                sql: "((\"RemovedRecordCount\" = 0 AND \"CompletedBatchCount\" = 0) OR (\"RemovedRecordCount\" > 0 AND \"CompletedBatchCount\" > 0)) AND \"RemovalProofVersion\" = 1 AND \"CompletedAtUtc\" >= \"StartedAtUtc\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_task_scope_destroy_operations_counts",
                schema: "tasks",
                table: "task_scope_destroy_operations",
                sql: "((\"RemovedRecordCount\" = 0 AND \"CompletedBatchCount\" = 0) OR (\"RemovedRecordCount\" > 0 AND \"CompletedBatchCount\" > 0)) AND \"ProofVersion\" = 1 AND \"ConcurrencyVersion\" >= 1 AND \"UpdatedAtUtc\" >= \"StartedAtUtc\"");

            migrationBuilder.AddForeignKey(
                name: "FK_task_scope_destroy_operations_task_scope_states_ScopeId",
                schema: "tasks",
                table: "task_scope_destroy_operations",
                column: "ScopeId",
                principalSchema: "tasks",
                principalTable: "task_scope_states",
                principalColumn: "ScopeId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_task_scope_destroy_receipts_task_scope_states_ScopeId",
                schema: "tasks",
                table: "task_scope_destroy_receipts",
                column: "ScopeId",
                principalSchema: "tasks",
                principalTable: "task_scope_states",
                principalColumn: "ScopeId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER
                    [tasks].[task_scope_destroy_receipts_append_only]
                ON [tasks].[task_scope_destroy_receipts]
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51000,
                        'task-runtime scope destruction receipts are append-only',
                        1;
                END;
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER [tasks].[task_scope_states_closed_immutable]
                ON [tasks].[task_scope_states]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT 1
                        FROM deleted
                        WHERE [Status] = 2)
                    BEGIN
                        THROW 51000,
                            'closed task-runtime scope state is immutable',
                            1;
                    END;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS
                    [tasks].[task_scope_destroy_receipts_append_only];
                """);

            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS
                    [tasks].[task_scope_states_closed_immutable];
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_task_scope_destroy_operations_task_scope_states_ScopeId",
                schema: "tasks",
                table: "task_scope_destroy_operations");

            migrationBuilder.DropForeignKey(
                name: "FK_task_scope_destroy_receipts_task_scope_states_ScopeId",
                schema: "tasks",
                table: "task_scope_destroy_receipts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_task_scope_states_progress",
                schema: "tasks",
                table: "task_scope_states");

            migrationBuilder.DropCheckConstraint(
                name: "CK_task_scope_states_status",
                schema: "tasks",
                table: "task_scope_states");

            migrationBuilder.DropCheckConstraint(
                name: "CK_task_scope_destroy_receipts_counts",
                schema: "tasks",
                table: "task_scope_destroy_receipts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_task_scope_destroy_operations_counts",
                schema: "tasks",
                table: "task_scope_destroy_operations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_task_scope_states_status",
                schema: "tasks",
                table: "task_scope_states",
                sql: "\"Status\" IN (1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_task_scope_destroy_receipts_counts",
                schema: "tasks",
                table: "task_scope_destroy_receipts",
                sql: "\"RemovedRecordCount\" >= 0 AND \"CompletedBatchCount\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_task_scope_destroy_operations_counts",
                schema: "tasks",
                table: "task_scope_destroy_operations",
                sql: "\"RemovedRecordCount\" >= 0 AND \"CompletedBatchCount\" >= 0");
        }
    }
}
