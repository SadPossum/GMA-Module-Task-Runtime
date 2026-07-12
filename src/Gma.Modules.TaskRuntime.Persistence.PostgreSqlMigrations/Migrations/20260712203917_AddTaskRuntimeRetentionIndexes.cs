using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gma.Modules.TaskRuntime.Persistence.PostgreSqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskRuntimeRetentionIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_task_runs_Status_CompletedAtUtc",
                schema: "tasks",
                table: "task_runs",
                columns: new[] { "Status", "CompletedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_task_control_messages_Status_CompletedAtUtc",
                schema: "tasks",
                table: "task_control_messages",
                columns: new[] { "Status", "CompletedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_task_runs_Status_CompletedAtUtc",
                schema: "tasks",
                table: "task_runs");

            migrationBuilder.DropIndex(
                name: "IX_task_control_messages_Status_CompletedAtUtc",
                schema: "tasks",
                table: "task_control_messages");
        }
    }
}
