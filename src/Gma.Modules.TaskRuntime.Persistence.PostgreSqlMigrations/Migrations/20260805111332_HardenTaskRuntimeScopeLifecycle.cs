using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gma.Modules.TaskRuntime.Persistence.PostgreSqlMigrations.Migrations
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
                CREATE FUNCTION tasks.reject_task_scope_destroy_receipt_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    RAISE EXCEPTION
                        'task-runtime scope destruction receipts are append-only';
                END;
                $$;

                CREATE TRIGGER task_scope_destroy_receipts_append_only
                BEFORE UPDATE OR DELETE
                ON tasks.task_scope_destroy_receipts
                FOR EACH ROW
                EXECUTE FUNCTION
                    tasks.reject_task_scope_destroy_receipt_mutation();

                CREATE FUNCTION tasks.reject_closed_task_scope_state_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF OLD."Status" = 2 THEN
                        RAISE EXCEPTION
                            'closed task-runtime scope state is immutable';
                    END IF;

                    IF TG_OP = 'DELETE' THEN
                        RETURN OLD;
                    END IF;

                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER task_scope_states_closed_immutable
                BEFORE UPDATE OR DELETE
                ON tasks.task_scope_states
                FOR EACH ROW
                EXECUTE FUNCTION
                    tasks.reject_closed_task_scope_state_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS task_scope_destroy_receipts_append_only
                ON tasks.task_scope_destroy_receipts;

                DROP FUNCTION IF EXISTS
                    tasks.reject_task_scope_destroy_receipt_mutation();

                DROP TRIGGER IF EXISTS task_scope_states_closed_immutable
                ON tasks.task_scope_states;

                DROP FUNCTION IF EXISTS
                    tasks.reject_closed_task_scope_state_mutation();
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
