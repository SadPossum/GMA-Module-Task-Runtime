using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gma.Modules.TaskRuntime.Persistence.PostgreSqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class HardenTaskRuntimeConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActiveDeduplicationIdentity",
                schema: "tasks",
                table: "task_runs",
                type: "character varying(768)",
                maxLength: 768,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConcurrencyVersion",
                schema: "tasks",
                table: "task_runs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LeaseGeneration",
                schema: "tasks",
                table: "task_runs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ConcurrencyVersion",
                schema: "tasks",
                table: "task_control_messages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                UPDATE tasks.task_runs
                SET "ActiveDeduplicationIdentity" =
                    "ModuleName" || chr(31) ||
                    "TaskName" || chr(31) ||
                    COALESCE("ScopeId", '') || chr(31) ||
                    "DeduplicationKey"
                WHERE "DeduplicationKey" IS NOT NULL
                  AND "Status" IN (1, 2, 3, 4, 5, 8);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_task_runs_ActiveDeduplicationIdentity",
                schema: "tasks",
                table: "task_runs",
                column: "ActiveDeduplicationIdentity",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_task_runs_ScopeId_CreatedAtUtc",
                schema: "tasks",
                table: "task_runs",
                columns: new[] { "ScopeId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_task_runs_WorkerGroup_ScheduledAtUtc_CreatedAtUtc_Id",
                schema: "tasks",
                table: "task_runs",
                columns: new[] { "WorkerGroup", "ScheduledAtUtc", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_task_control_messages_Status_ExpiresAtUtc",
                schema: "tasks",
                table: "task_control_messages",
                columns: new[] { "Status", "ExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_task_runs_ActiveDeduplicationIdentity",
                schema: "tasks",
                table: "task_runs");

            migrationBuilder.DropIndex(
                name: "IX_task_runs_ScopeId_CreatedAtUtc",
                schema: "tasks",
                table: "task_runs");

            migrationBuilder.DropIndex(
                name: "IX_task_runs_WorkerGroup_ScheduledAtUtc_CreatedAtUtc_Id",
                schema: "tasks",
                table: "task_runs");

            migrationBuilder.DropIndex(
                name: "IX_task_control_messages_Status_ExpiresAtUtc",
                schema: "tasks",
                table: "task_control_messages");

            migrationBuilder.DropColumn(
                name: "ActiveDeduplicationIdentity",
                schema: "tasks",
                table: "task_runs");

            migrationBuilder.DropColumn(
                name: "ConcurrencyVersion",
                schema: "tasks",
                table: "task_runs");

            migrationBuilder.DropColumn(
                name: "LeaseGeneration",
                schema: "tasks",
                table: "task_runs");

            migrationBuilder.DropColumn(
                name: "ConcurrencyVersion",
                schema: "tasks",
                table: "task_control_messages");
        }
    }
}
