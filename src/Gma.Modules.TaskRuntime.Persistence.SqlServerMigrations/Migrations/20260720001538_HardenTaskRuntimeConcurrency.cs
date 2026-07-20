using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gma.Modules.TaskRuntime.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class HardenTaskRuntimeConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_task_runs_ModuleName_TaskName_ScopeId_DeduplicationKey_Status",
                schema: "tasks",
                table: "task_runs");

            migrationBuilder.AlterColumn<string>(
                name: "ScopeId",
                schema: "tasks",
                table: "task_runs",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActiveDeduplicationIdentity",
                schema: "tasks",
                table: "task_runs",
                type: "nvarchar(768)",
                maxLength: 768,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.AddColumn<int>(
                name: "ConcurrencyVersion",
                schema: "tasks",
                table: "task_runs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LeaseGeneration",
                schema: "tasks",
                table: "task_runs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ConcurrencyVersion",
                schema: "tasks",
                table: "task_control_messages",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                UPDATE [tasks].[task_runs]
                SET [ActiveDeduplicationIdentity] = CONCAT(
                    [ModuleName], NCHAR(31),
                    [TaskName], NCHAR(31),
                    COALESCE([ScopeId], N''), NCHAR(31),
                    [DeduplicationKey])
                WHERE [DeduplicationKey] IS NOT NULL
                  AND [Status] IN (1, 2, 3, 4, 5, 8);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_task_runs_ActiveDeduplicationIdentity",
                schema: "tasks",
                table: "task_runs",
                column: "ActiveDeduplicationIdentity",
                unique: true,
                filter: "[ActiveDeduplicationIdentity] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_task_runs_ScopeId_CreatedAtUtc",
                schema: "tasks",
                table: "task_runs",
                columns: new[] { "ScopeId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_task_runs_ModuleName_TaskName_ScopeId_DeduplicationKey_Status",
                schema: "tasks",
                table: "task_runs",
                columns: new[] { "ModuleName", "TaskName", "ScopeId", "DeduplicationKey", "Status" });

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
                name: "IX_task_runs_ModuleName_TaskName_ScopeId_DeduplicationKey_Status",
                schema: "tasks",
                table: "task_runs");

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

            migrationBuilder.AlterColumn<string>(
                name: "ScopeId",
                schema: "tasks",
                table: "task_runs",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldNullable: true,
                oldCollation: "Latin1_General_100_BIN2");

            migrationBuilder.CreateIndex(
                name: "IX_task_runs_ModuleName_TaskName_ScopeId_DeduplicationKey_Status",
                schema: "tasks",
                table: "task_runs",
                columns: new[] { "ModuleName", "TaskName", "ScopeId", "DeduplicationKey", "Status" });
        }
    }
}
