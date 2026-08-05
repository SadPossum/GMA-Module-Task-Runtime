namespace Gma.Modules.TaskRuntime.IntegrationTests;

using Gma.Framework.Runtime.Time;
using Gma.Framework.Tasks;
using Gma.Modules.TaskRuntime.Contracts;
using Gma.Modules.TaskRuntime.IntegrationTests.Support;
using Gma.Modules.TaskRuntime.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit;

public sealed partial class TaskRuntimeRelationalIntegrationTests
{
    [DockerFact]
    public async Task SqlServer_persists_terminal_scope_lifecycle_proof()
    {
        await using MsSqlContainer sqlServer = new MsSqlBuilder(
            "mcr.microsoft.com/mssql/server:2022-latest").Build();
        await sqlServer.StartAsync();

        DbContextOptions<TaskRuntimeDbContext> options = CreateOptions(
            "SqlServer",
            sqlServer.GetConnectionString());
        await using (TaskRuntimeDbContext migrationContext = new(options))
        {
            await migrationContext.Database.MigrateAsync();
        }

        MutableClock clock = new(Now);
        const string scopeId = "scope-lifecycle-sql-server";
        TaskRuntimeScopeDestroyResult completed = await DestroyScopeAsync(
            options,
            clock,
            new TaskRuntimeScopeDestroyRequest(
                Guid.CreateVersion7(),
                scopeId,
                ExpectedRevision: 0,
                BatchSize: 1));

        Assert.Equal(TaskRuntimeScopeDestroyStatus.Completed, completed.Status);
        Assert.Equal(0, completed.Receipt?.RemovedRecordCount);
        Assert.Equal(0, completed.Receipt?.CompletedBatchCount);

        await using TaskRuntimeDbContext assertionContext = new(options);
        Assert.Contains(
            "20260805111355_HardenTaskRuntimeScopeLifecycle",
            await assertionContext.Database.GetAppliedMigrationsAsync());
        Assert.Single(await assertionContext.TaskScopeStates.ToArrayAsync());
        Assert.Single(
            await assertionContext.TaskScopeDestroyReceipts.ToArrayAsync());
        Assert.Empty(
            await assertionContext.TaskScopeDestroyOperations.ToArrayAsync());
    }

    [DockerFact]
    public async Task PostgreSql_enforces_exact_scope_lifecycle()
    {
        await using PostgreSqlContainer postgreSql = new PostgreSqlBuilder(
                "postgres:16-alpine")
            .WithDatabase("task_runtime_scope_lifecycle_tests")
            .Build();
        await postgreSql.StartAsync();

        DbContextOptions<TaskRuntimeDbContext> options = CreateOptions(
            "PostgreSql",
            postgreSql.GetConnectionString());
        const string targetScope = "scope-lifecycle-target";
        const string otherScope = "scope-lifecycle-other";
        const string dedupScope = "scope-lifecycle-dedup";
        Guid legacyRunId = Guid.CreateVersion7();
        Guid legacyControlId = Guid.CreateVersion7();
        Guid orphanControlId = Guid.CreateVersion7();

        await MigrateLegacyRowsAsync(
            options,
            targetScope,
            legacyRunId,
            legacyControlId,
            orphanControlId);

        await using (TaskRuntimeDbContext assertionContext = new(options))
        {
            Assert.Equal(
                targetScope,
                await assertionContext.TaskControlMessages
                    .Where(message => message.Id == legacyControlId)
                    .Select(message => message.ScopeId)
                    .SingleAsync());
            Assert.False(await assertionContext.TaskControlMessages.AnyAsync(
                message => message.Id == orphanControlId));
        }

        MutableClock clock = new(Now);
        TaskRunRequest[] duplicateRequests = Enumerable.Range(0, 4)
            .Select(index => CreateRequest(
                Guid.CreateVersion7(),
                Now,
                Now,
                "scope-lifecycle-deduplication",
                $"{{\"request\":{index}}}",
                scopeId: dedupScope,
                workerGroup: "scope-lifecycle-dedup"))
            .ToArray();
        TaskRunEnqueueResult[] duplicateResults = await Task.WhenAll(
            duplicateRequests.Select(request =>
                EnqueueAsync(options, clock, request)));

        Assert.Single(duplicateResults
            .Select(result => result.Run.Summary.RunId)
            .Distinct());
        Assert.Equal(1, duplicateResults.Count(result => result.Created));

        Guid otherRunId = Guid.CreateVersion7();
        await EnqueueAsync(
            options,
            clock,
            CreateRequest(
                otherRunId,
                Now,
                Now,
                deduplicationKey: null,
                "{}",
                scopeId: otherScope,
                workerGroup: "scope-lifecycle-other"));
        Guid otherControlId = Guid.CreateVersion7();
        Assert.Equal(
            TaskControlMessageEnqueueOutcome.Enqueued,
            await EnqueueControlAsync(
                options,
                clock,
                new TaskControlMessage(
                    otherControlId,
                    otherRunId,
                    "tasks.pause",
                    "{}",
                    Now,
                    "operator")));

        TaskRunLease targetLease = Assert.Single(await ClaimAsync(
            options,
            clock,
            "scope-lifecycle-worker",
            "scope-lifecycle-node",
            Now,
            maxRuns: 1,
            leaseDuration: TimeSpan.FromMinutes(1),
            workerGroup: "scope-lifecycle-target"));
        Assert.Equal(legacyRunId, targetLease.RunId);

        Guid operationId = Guid.CreateVersion7();
        TaskRuntimeScopeDestroyRequest destroyRequest = new(
            operationId,
            targetScope,
            ExpectedRevision: 0,
            BatchSize: 1);
        TaskRuntimeScopeDestroyResult busy = await DestroyScopeAsync(
            options,
            clock,
            destroyRequest);

        Assert.Equal(TaskRuntimeScopeDestroyStatus.Busy, busy.Status);
        Assert.Equal(1, busy.Progress?.RemainingActiveRunCount);
        Assert.Equal(
            TaskRuntimeScopeDestructionStage.QuiesceRuns,
            busy.Progress?.Stage);

        await Assert.ThrowsAsync<TaskScopeNotAcceptingWorkException>(() =>
            EnqueueAsync(
                options,
                clock,
                CreateRequest(
                    Guid.CreateVersion7(),
                    Now,
                    Now,
                    deduplicationKey: null,
                    "{}",
                    scopeId: targetScope,
                    workerGroup: "scope-lifecycle-target")));

        Assert.Empty(await ClaimAsync(
            options,
            clock,
            "scope-lifecycle-reclaimer",
            "scope-lifecycle-node",
            Now.AddMinutes(2),
            maxRuns: 1,
            workerGroup: "scope-lifecycle-target"));

        Assert.Equal(
            TaskRunMutationOutcome.Applied,
            await MarkCanceledAsync(
                options,
                clock,
                targetLease.CreateExecutionContext(),
                Now.AddSeconds(10)));
        Assert.Equal(
            TaskRunMutationOutcome.ScopeClosed,
            await RetryAsync(
                options,
                clock,
                legacyRunId,
                Now.AddSeconds(11)));
        Assert.Equal(
            TaskControlMessageEnqueueOutcome.ScopeClosed,
            await EnqueueControlAsync(
                options,
                clock,
                new TaskControlMessage(
                    Guid.CreateVersion7(),
                    legacyRunId,
                    "tasks.pause",
                    "{}",
                    Now.AddSeconds(11),
                    "operator")));

        clock.UtcNow = Now.AddSeconds(12);
        TaskRuntimeScopeDestroyResult completed = busy;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            completed = await DestroyScopeAsync(options, clock, destroyRequest);
            if (completed.Status == TaskRuntimeScopeDestroyStatus.Completed)
            {
                break;
            }

            Assert.Equal(
                TaskRuntimeScopeDestroyStatus.InProgress,
                completed.Status);
        }

        Assert.Equal(TaskRuntimeScopeDestroyStatus.Completed, completed.Status);
        Assert.Equal(2, completed.Receipt?.RemovedRecordCount);
        Assert.Equal(2, completed.Receipt?.CompletedBatchCount);
        Assert.Equal(0, completed.Receipt?.SelectedRevision);
        Assert.Equal(1, completed.Receipt?.ResultingRevision);

        TaskRuntimeScopeSnapshot closed = await GetScopeSnapshotAsync(
            options,
            clock,
            targetScope);
        Assert.Equal(TaskRuntimeScopeStatus.Closed, closed.Status);
        Assert.Equal(0, closed.TotalRunCount);
        Assert.Equal(0, closed.ControlMessageCount);

        TaskRuntimeScopeDestroyResult replay = await DestroyScopeAsync(
            options,
            clock,
            destroyRequest);
        TaskRuntimeScopeDestroyResult conflict = await DestroyScopeAsync(
            options,
            clock,
            destroyRequest with
            {
                OperationId = Guid.CreateVersion7()
            });
        Assert.Equal(TaskRuntimeScopeDestroyStatus.Replayed, replay.Status);
        Assert.Equal(
            completed.Receipt?.RemovalProofSha256,
            replay.Receipt?.RemovalProofSha256);
        Assert.Equal(TaskRuntimeScopeDestroyStatus.Conflict, conflict.Status);

        await using (TaskRuntimeDbContext finalContext = new(options))
        {
            Assert.Contains(
                "20260805111332_HardenTaskRuntimeScopeLifecycle",
                await finalContext.Database.GetAppliedMigrationsAsync());
            Assert.True(await finalContext.TaskRuns.AnyAsync(
                run => run.Id == otherRunId && run.ScopeId == otherScope));
            Assert.True(await finalContext.TaskControlMessages.AnyAsync(
                message =>
                    message.Id == otherControlId &&
                    message.ScopeId == otherScope));
            Assert.True(await finalContext.TaskRuns.AnyAsync(
                run => run.ScopeId == dedupScope));
            Assert.False(await finalContext.TaskRuns.AnyAsync(
                run => run.ScopeId == targetScope));
            Assert.False(await finalContext.TaskControlMessages.AnyAsync(
                message => message.ScopeId == targetScope));

            PostgresException receiptMutation =
                await Assert.ThrowsAsync<PostgresException>(
                    () => finalContext.Database.ExecuteSqlRawAsync(
                        """
                        UPDATE tasks.task_scope_destroy_receipts
                        SET "RemovedRecordCount" = "RemovedRecordCount"
                        WHERE "ScopeId" = 'scope-lifecycle-target'
                        """));
            Assert.Equal("P0001", receiptMutation.SqlState);
            Assert.Contains("append-only", receiptMutation.MessageText);

            PostgresException stateReopen =
                await Assert.ThrowsAsync<PostgresException>(
                    () => finalContext.Database.ExecuteSqlRawAsync(
                        """
                        UPDATE tasks.task_scope_states
                        SET "Status" = 1,
                            "ClosedAtUtc" = NULL
                        WHERE "ScopeId" = 'scope-lifecycle-target'
                        """));
            Assert.Equal("P0001", stateReopen.SqlState);
            Assert.Contains("immutable", stateReopen.MessageText);
        }
    }

    private static async Task MigrateLegacyRowsAsync(
        DbContextOptions<TaskRuntimeDbContext> options,
        string targetScope,
        Guid runId,
        Guid controlId,
        Guid orphanControlId)
    {
        await using TaskRuntimeDbContext context = new(options);
        IMigrator migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(
            "20260720001525_HardenTaskRuntimeConcurrency");

        const string payload = "{}";
        int queued = (int)TaskRunStatus.Queued;
        int pending = (int)TaskControlMessageStatus.Pending;
        await context.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO tasks.task_runs
                ("Id", "ModuleName", "TaskName", "WorkerGroup",
                 "PayloadVersion", "Status", "Payload", "ScopeId",
                 "CreatedAtUtc", "ScheduledAtUtc", "Attempts", "MaxAttempts",
                 "LeaseGeneration", "ConcurrencyVersion")
            VALUES
                ({{runId}}, {{"task-tests"}}, {{"legacy-run"}},
                 {{"scope-lifecycle-target"}}, 1, {{queued}}, {{payload}},
                 {{targetScope}}, {{Now}}, {{Now}}, 0, 3, 0, 0);

            INSERT INTO tasks.task_control_messages
                ("Id", "RunId", "CommandName", "Payload", "EnqueuedAtUtc",
                 "Status", "ConcurrencyVersion")
            VALUES
                ({{controlId}}, {{runId}}, {{"tasks.pause"}}, {{payload}},
                 {{Now}}, {{pending}}, 0),
                ({{orphanControlId}}, {{Guid.CreateVersion7()}},
                 {{"tasks.pause"}}, {{payload}}, {{Now}}, {{pending}}, 0);
            """);

        await migrator.MigrateAsync();
    }

    private static async Task<TaskRuntimeScopeSnapshot> GetScopeSnapshotAsync(
        DbContextOptions<TaskRuntimeDbContext> options,
        ISystemClock clock,
        string scopeId)
    {
        await using TaskRuntimeDbContext context = new(options);
        return await new TaskRuntimeScopeLifecycleService(context, clock)
            .GetSnapshotAsync(scopeId, CancellationToken.None);
    }

    private static async Task<TaskRuntimeScopeDestroyResult> DestroyScopeAsync(
        DbContextOptions<TaskRuntimeDbContext> options,
        ISystemClock clock,
        TaskRuntimeScopeDestroyRequest request)
    {
        await using TaskRuntimeDbContext context = new(options);
        return await new TaskRuntimeScopeLifecycleService(context, clock)
            .DestroyBatchAsync(request, CancellationToken.None);
    }

    private static async Task<TaskRunMutationOutcome> MarkCanceledAsync(
        DbContextOptions<TaskRuntimeDbContext> options,
        ISystemClock clock,
        TaskExecutionContext executionContext,
        DateTimeOffset canceledAtUtc)
    {
        await using TaskRuntimeDbContext context = new(options);
        return await new TaskRuntimeRunStore(context, clock)
            .MarkCanceledAsync(
                executionContext,
                canceledAtUtc,
                CancellationToken.None);
    }
}
