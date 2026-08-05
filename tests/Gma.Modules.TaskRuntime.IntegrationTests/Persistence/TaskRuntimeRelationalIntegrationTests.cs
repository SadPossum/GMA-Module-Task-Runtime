namespace Gma.Modules.TaskRuntime.IntegrationTests;

using Gma.Framework.Runtime.Time;
using Gma.Framework.Tasks;
using Gma.Modules.TaskRuntime.IntegrationTests.Support;
using Gma.Modules.TaskRuntime.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit;

[Trait("Category", "Docker")]
[Trait("Category", "Integration")]
public sealed partial class TaskRuntimeRelationalIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 21, 0, 0, TimeSpan.Zero);

    [DockerFact]
    public async Task PostgreSql_enforces_task_runtime_concurrency_and_retention()
    {
        await using PostgreSqlContainer postgreSql = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("task_runtime_hardening_tests")
            .Build();
        await postgreSql.StartAsync();

        await RunScenarioAsync("PostgreSql", postgreSql.GetConnectionString());
    }

    [DockerFact]
    public async Task SqlServer_enforces_task_runtime_concurrency_and_retention()
    {
        await using MsSqlContainer sqlServer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await sqlServer.StartAsync();

        await RunScenarioAsync("SqlServer", sqlServer.GetConnectionString());
    }

    private static async Task RunScenarioAsync(string provider, string connectionString)
    {
        DbContextOptions<TaskRuntimeDbContext> options = CreateOptions(provider, connectionString);
        await using (TaskRuntimeDbContext migrationContext = new(options))
        {
            await migrationContext.Database.MigrateAsync();
        }

        MutableClock clock = new(Now);
        const string deduplicationKey = "nightly-report";
        TaskRunRequest[] duplicateRequests = Enumerable.Range(0, 8)
            .Select(index => CreateRequest(
                Guid.CreateVersion7(),
                Now,
                Now.AddHours(1),
                deduplicationKey,
                $"{{\"request\":{index}}}"))
            .ToArray();
        TaskRunEnqueueResult[] duplicateResults = await Task.WhenAll(
            duplicateRequests.Select(request => EnqueueAsync(options, clock, request)));

        Assert.Single(duplicateResults.Select(result => result.Run.Summary.RunId).Distinct());
        Assert.Equal(1, duplicateResults.Count(result => result.Created));

        await using (TaskRuntimeDbContext assertionContext = new(options))
        {
            Assert.Equal(1, await assertionContext.TaskRuns.CountAsync(run =>
                run.DeduplicationKey == deduplicationKey &&
                run.Status == TaskRunStatus.Queued));
        }

        TaskRunEnqueueResult upperScope = await EnqueueAsync(
            options,
            clock,
            CreateRequest(
                Guid.CreateVersion7(),
                Now,
                Now.AddHours(1),
                "scope-case",
                "{}",
                scopeId: "Tenant-A"));
        TaskRunEnqueueResult lowerScope = await EnqueueAsync(
            options,
            clock,
            CreateRequest(
                Guid.CreateVersion7(),
                Now,
                Now.AddHours(1),
                "scope-case",
                "{}",
                scopeId: "tenant-a"));

        Assert.True(upperScope.Created);
        Assert.True(lowerScope.Created);
        Assert.NotEqual(upperScope.Run.Summary.RunId, lowerScope.Run.Summary.RunId);

        TaskRunRequest[] readyRequests = Enumerable.Range(0, 8)
            .Select(index => CreateRequest(
                Guid.CreateVersion7(),
                Now.AddSeconds(index),
                Now.AddSeconds(index),
                deduplicationKey: null,
                $"{{\"ready\":{index}}}"))
            .ToArray();
        await Task.WhenAll(readyRequests.Select(request => EnqueueAsync(options, clock, request)));

        IReadOnlyList<TaskRunLease>[] claims = await Task.WhenAll(
            ClaimAsync(options, clock, "worker-one", "node-one", Now.AddSeconds(10), maxRuns: 4),
            ClaimAsync(options, clock, "worker-two", "node-two", Now.AddSeconds(10), maxRuns: 4));

        Assert.Equal(4, claims[0].Count);
        Assert.Equal(4, claims[1].Count);
        Assert.Empty(claims[0].Select(lease => lease.RunId).Intersect(claims[1].Select(lease => lease.RunId)));

        Guid reclaimedRunId = Guid.CreateVersion7();
        await EnqueueAsync(
            options,
            clock,
            CreateRequest(reclaimedRunId, Now.AddSeconds(20), Now.AddSeconds(20), null, "{}"));
        TaskRunLease firstLease = Assert.Single(await ClaimAsync(
            options,
            clock,
            "worker-reused",
            "node-reused",
            Now.AddSeconds(20),
            maxRuns: 1,
            leaseDuration: TimeSpan.FromSeconds(1)));
        TaskRunLease secondLease = Assert.Single(await ClaimAsync(
            options,
            clock,
            "worker-reused",
            "node-reused",
            Now.AddSeconds(22),
            maxRuns: 1,
            leaseDuration: TimeSpan.FromSeconds(10)));

        TaskRunMutationOutcome staleStart = await MarkStartedAsync(
            options,
            clock,
            firstLease.CreateExecutionContext(),
            Now.AddSeconds(22));
        TaskRunMutationOutcome currentStart = await MarkStartedAsync(
            options,
            clock,
            secondLease.CreateExecutionContext(),
            Now.AddSeconds(22));

        Assert.Equal(firstLease.LeaseGeneration + 1, secondLease.LeaseGeneration);
        Assert.Equal(TaskRunMutationOutcome.LeaseLost, staleStart);
        Assert.Equal(TaskRunMutationOutcome.Applied, currentStart);

        Guid controlId = Guid.CreateVersion7();
        TaskControlMessage control = new(
            controlId,
            reclaimedRunId,
            "tasks.pause",
            "{}",
            Now.AddSeconds(22),
            "operator",
            Now.AddSeconds(23));
        TaskControlMessageEnqueueOutcome controlOutcome = await EnqueueControlAsync(options, clock, control);
        clock.UtcNow = Now.AddSeconds(22);
        IReadOnlyList<TaskControlMessage> staleReadable = await ReadControlsAsync(
            options,
            clock,
            firstLease.CreateExecutionContext());
        IReadOnlyList<TaskControlMessage> currentReadable = await ReadControlsAsync(
            options,
            clock,
            secondLease.CreateExecutionContext());
        clock.UtcNow = Now.AddSeconds(24);
        IReadOnlyList<TaskControlMessage> readable = await ReadControlsAsync(
            options,
            clock,
            secondLease.CreateExecutionContext());

        Assert.Equal(TaskControlMessageEnqueueOutcome.Enqueued, controlOutcome);
        Assert.Empty(staleReadable);
        Assert.Equal(controlId, Assert.Single(currentReadable).MessageId);
        Assert.Empty(readable);
        await using (TaskRuntimeDbContext assertionContext = new(options))
        {
            Assert.Equal(
                TaskControlMessageStatus.Expired,
                await assertionContext.TaskControlMessages
                    .Where(message => message.Id == controlId)
                    .Select(message => message.Status)
                    .SingleAsync());
        }

        Guid concurrentControlId = Guid.CreateVersion7();
        TaskControlMessageEnqueueOutcome concurrentControlOutcome = await EnqueueControlAsync(
            options,
            clock,
            new TaskControlMessage(
                concurrentControlId,
                reclaimedRunId,
                "tasks.pause",
                "{}",
                Now.AddSeconds(24),
                "operator",
                Now.AddSeconds(30)));
        TaskRunMutationOutcome concurrentHeartbeatOutcome = TaskRunMutationOutcome.Conflict;
        DbContextOptionsBuilder<TaskRuntimeDbContext> pollingOptions = new();
        ConfigureProvider(pollingOptions, provider, connectionString);
        pollingOptions.AddInterceptors(new OneShotSaveChangesInterceptor(async cancellationToken =>
        {
            concurrentHeartbeatOutcome = await ReportHeartbeatAsync(
                options,
                clock,
                secondLease.CreateExecutionContext(),
                Now.AddSeconds(24));
        }));
        await using (TaskRuntimeDbContext pollingContext = new(pollingOptions.Options))
        {
            IReadOnlyList<TaskControlMessage> concurrentlyRead = await new TaskRuntimeRunStore(pollingContext, clock)
                .ReadPendingAsync(secondLease.CreateExecutionContext(), 10, CancellationToken.None);

            Assert.Equal(TaskControlMessageEnqueueOutcome.Enqueued, concurrentControlOutcome);
            Assert.Equal(TaskRunMutationOutcome.Applied, concurrentHeartbeatOutcome);
            Assert.Equal(concurrentControlId, Assert.Single(concurrentlyRead).MessageId);
        }

        Guid unreadExpiredControlId = Guid.CreateVersion7();
        TaskControlMessageEnqueueOutcome unreadExpiredOutcome = await EnqueueControlAsync(
            options,
            clock,
            new TaskControlMessage(
                unreadExpiredControlId,
                reclaimedRunId,
                "tasks.drain",
                "{}",
                Now.AddSeconds(24),
                "operator",
                Now.AddSeconds(25)));
        Assert.Equal(TaskControlMessageEnqueueOutcome.Enqueued, unreadExpiredOutcome);

        clock.UtcNow = Now.AddDays(40);
        await RunRetentionAsync(provider, connectionString, clock);
        await using (TaskRuntimeDbContext assertionContext = new(options))
        {
            Assert.False(await assertionContext.TaskControlMessages.AnyAsync(message => message.Id == controlId));
            Assert.False(await assertionContext.TaskControlMessages.AnyAsync(
                message => message.Id == unreadExpiredControlId));
        }

        await RunTransitionRacesAsync(options, clock);

        await using (TaskRuntimeDbContext pageContext = new(options))
        {
            TaskRuntimeRunStore store = new(pageContext, clock);
            TaskRunPage page = await store.ListAsync(new TaskRunFilter(page: 1, pageSize: 5), CancellationToken.None);
            Assert.Equal(5, page.Items.Count);
            Assert.True(page.TotalCount >= 10);
            Assert.Equal(1, page.Page);
        }
    }

    private static TaskRunRequest CreateRequest(
        Guid runId,
        DateTimeOffset createdAtUtc,
        DateTimeOffset scheduledAtUtc,
        string? deduplicationKey,
        string payload,
        string scopeId = "tenant-a",
        string workerGroup = "task-tests") =>
        new(
            runId,
            "task-tests",
            "generate-report",
            payload,
            createdAtUtc,
            scheduledAtUtc,
            workerGroup: workerGroup,
            scopeId: scopeId,
            requestedBy: "operator",
            maxAttempts: 3,
            deduplicationKey: deduplicationKey);

    private static async Task<TaskRunEnqueueResult> EnqueueAsync(
        DbContextOptions<TaskRuntimeDbContext> options,
        ISystemClock clock,
        TaskRunRequest request)
    {
        await using TaskRuntimeDbContext dbContext = new(options);
        return await new TaskRuntimeRunStore(dbContext, clock)
            .EnqueueAsync(request, CancellationToken.None);
    }

    private static async Task<IReadOnlyList<TaskRunLease>> ClaimAsync(
        DbContextOptions<TaskRuntimeDbContext> options,
        ISystemClock clock,
        string workerId,
        string nodeId,
        DateTimeOffset claimedAtUtc,
        int maxRuns,
        TimeSpan? leaseDuration = null,
        string workerGroup = "task-tests")
    {
        await using TaskRuntimeDbContext dbContext = new(options);
        return await new TaskRuntimeRunStore(dbContext, clock).ClaimReadyAsync(
            new TaskWorkerClaim(
                workerGroup,
                workerId,
                nodeId,
                claimedAtUtc,
                maxRuns,
                leaseDuration ?? TimeSpan.FromMinutes(1)),
            CancellationToken.None);
    }

    private static async Task RunTransitionRacesAsync(
        DbContextOptions<TaskRuntimeDbContext> options,
        MutableClock clock)
    {
        DateTimeOffset raceStart = Now.AddDays(41);
        clock.UtcNow = raceStart;

        Guid heartbeatRunId = Guid.CreateVersion7();
        await EnqueueAsync(
            options,
            clock,
            CreateRequest(
                heartbeatRunId,
                raceStart,
                raceStart,
                null,
                "{}",
                workerGroup: "race-tests"));
        TaskRunLease heartbeatLease = Assert.Single(await ClaimAsync(
            options,
            clock,
            "race-worker",
            "race-node",
            raceStart,
            1,
            TimeSpan.FromSeconds(1),
            "race-tests"));
        TaskExecutionContext heartbeatContext = heartbeatLease.CreateExecutionContext();
        Assert.Equal(
            TaskRunMutationOutcome.Applied,
            await MarkStartedAsync(options, clock, heartbeatContext, raceStart));

        DateTimeOffset raceAt = raceStart.AddSeconds(10);
        Task<TaskRunMutationOutcome> heartbeat = ReportHeartbeatAsync(
            options,
            clock,
            heartbeatContext,
            raceAt);
        Task<IReadOnlyList<TaskRunSummary>> timeout = MarkStaleAsync(
            options,
            clock,
            raceAt,
            TimeSpan.FromSeconds(1));
        await Task.WhenAll(heartbeat, timeout);

        bool heartbeatTimedOut = timeout.Result.Any(run => run.RunId == heartbeatRunId);
        Assert.Contains(
            heartbeat.Result,
            new[]
            {
                TaskRunMutationOutcome.Applied,
                TaskRunMutationOutcome.LeaseLost,
                TaskRunMutationOutcome.Conflict
            });
        Assert.Equal(
            heartbeatTimedOut ? TaskRunStatus.TimedOut : TaskRunStatus.Running,
            await GetStatusAsync(options, heartbeatRunId));

        Guid completionRunId = Guid.CreateVersion7();
        await EnqueueAsync(
            options,
            clock,
            CreateRequest(
                completionRunId,
                raceAt,
                raceAt,
                null,
                "{}",
                workerGroup: "race-tests"));
        TaskRunLease completionLease = Assert.Single(await ClaimAsync(
            options,
            clock,
            "completion-worker",
            "race-node",
            raceAt,
            1,
            workerGroup: "race-tests"));
        TaskExecutionContext completionContext = completionLease.CreateExecutionContext();
        Assert.Equal(
            TaskRunMutationOutcome.Applied,
            await MarkStartedAsync(options, clock, completionContext, raceAt));

        Task<TaskRunMutationOutcome> completion = MarkSucceededAsync(
            options,
            clock,
            completionContext,
            raceAt.AddSeconds(1));
        Task<TaskRunMutationOutcome> cancellation = RequestCancellationAsync(
            options,
            clock,
            completionRunId,
            raceAt.AddSeconds(1));
        await Task.WhenAll(completion, cancellation);

        Assert.Equal(TaskRunMutationOutcome.Applied, completion.Result);
        Assert.Contains(
            cancellation.Result,
            new[] { TaskRunMutationOutcome.Applied, TaskRunMutationOutcome.InvalidState });
        Assert.Equal(TaskRunStatus.Succeeded, await GetStatusAsync(options, completionRunId));

        Guid retryRunId = Guid.CreateVersion7();
        await EnqueueAsync(
            options,
            clock,
            CreateRequest(
                retryRunId,
                raceAt,
                raceAt,
                null,
                "{}",
                workerGroup: "race-tests"));
        TaskRunLease retryLease = Assert.Single(await ClaimAsync(
            options,
            clock,
            "retry-worker",
            "race-node",
            raceAt,
            1,
            workerGroup: "race-tests"));
        TaskExecutionContext retryContext = retryLease.CreateExecutionContext();
        Assert.Equal(
            TaskRunMutationOutcome.Applied,
            await MarkFailedAsync(options, clock, retryContext, raceAt.AddSeconds(1)));

        TaskRunMutationOutcome[] retries = await Task.WhenAll(
            RetryAsync(options, clock, retryRunId, raceAt.AddSeconds(2)),
            RetryAsync(options, clock, retryRunId, raceAt.AddSeconds(2)));
        Assert.Equal(1, retries.Count(outcome => outcome == TaskRunMutationOutcome.Applied));
        Assert.Equal(1, retries.Count(outcome => outcome == TaskRunMutationOutcome.InvalidState));
        Assert.Equal(TaskRunStatus.Queued, await GetStatusAsync(options, retryRunId));
    }

    private static async Task<TaskRunMutationOutcome> MarkStartedAsync(
        DbContextOptions<TaskRuntimeDbContext> options,
        ISystemClock clock,
        TaskExecutionContext context,
        DateTimeOffset startedAtUtc)
    {
        await using TaskRuntimeDbContext dbContext = new(options);
        return await new TaskRuntimeRunStore(dbContext, clock)
            .MarkStartedAsync(context, startedAtUtc, CancellationToken.None);
    }

    private static async Task<TaskRunMutationOutcome> MarkSucceededAsync(
        DbContextOptions<TaskRuntimeDbContext> options,
        ISystemClock clock,
        TaskExecutionContext context,
        DateTimeOffset completedAtUtc)
    {
        await using TaskRuntimeDbContext dbContext = new(options);
        return await new TaskRuntimeRunStore(dbContext, clock)
            .MarkSucceededAsync(context, completedAtUtc, CancellationToken.None);
    }

    private static async Task<TaskRunMutationOutcome> MarkFailedAsync(
        DbContextOptions<TaskRuntimeDbContext> options,
        ISystemClock clock,
        TaskExecutionContext context,
        DateTimeOffset failedAtUtc)
    {
        await using TaskRuntimeDbContext dbContext = new(options);
        return await new TaskRuntimeRunStore(dbContext, clock)
            .MarkFailedAsync(context, "expected test failure", failedAtUtc, null, CancellationToken.None);
    }

    private static async Task<TaskRunMutationOutcome> ReportHeartbeatAsync(
        DbContextOptions<TaskRuntimeDbContext> options,
        ISystemClock clock,
        TaskExecutionContext context,
        DateTimeOffset observedAtUtc)
    {
        await using TaskRuntimeDbContext dbContext = new(options);
        return await new TaskRuntimeRunStore(dbContext, clock)
            .ReportHeartbeatAsync(context, observedAtUtc, CancellationToken.None);
    }

    private static async Task<IReadOnlyList<TaskRunSummary>> MarkStaleAsync(
        DbContextOptions<TaskRuntimeDbContext> options,
        ISystemClock clock,
        DateTimeOffset nowUtc,
        TimeSpan staleAfter)
    {
        await using TaskRuntimeDbContext dbContext = new(options);
        return await new TaskRuntimeRunStore(dbContext, clock)
            .MarkStaleTimedOutAsync(nowUtc, staleAfter, 500, CancellationToken.None);
    }

    private static async Task<TaskRunMutationOutcome> RequestCancellationAsync(
        DbContextOptions<TaskRuntimeDbContext> options,
        ISystemClock clock,
        Guid runId,
        DateTimeOffset requestedAtUtc)
    {
        await using TaskRuntimeDbContext dbContext = new(options);
        return await new TaskRuntimeRunStore(dbContext, clock)
            .RequestCancellationAsync(runId, "operator", requestedAtUtc, CancellationToken.None);
    }

    private static async Task<TaskRunMutationOutcome> RetryAsync(
        DbContextOptions<TaskRuntimeDbContext> options,
        ISystemClock clock,
        Guid runId,
        DateTimeOffset scheduledAtUtc)
    {
        await using TaskRuntimeDbContext dbContext = new(options);
        return await new TaskRuntimeRunStore(dbContext, clock)
            .RetryAsync(runId, "operator", scheduledAtUtc, CancellationToken.None);
    }

    private static async Task<TaskRunStatus> GetStatusAsync(
        DbContextOptions<TaskRuntimeDbContext> options,
        Guid runId)
    {
        await using TaskRuntimeDbContext dbContext = new(options);
        return await dbContext.TaskRuns
            .Where(run => run.Id == runId)
            .Select(run => run.Status)
            .SingleAsync();
    }

    private static async Task<TaskControlMessageEnqueueOutcome> EnqueueControlAsync(
        DbContextOptions<TaskRuntimeDbContext> options,
        ISystemClock clock,
        TaskControlMessage message)
    {
        await using TaskRuntimeDbContext dbContext = new(options);
        return await new TaskRuntimeRunStore(dbContext, clock)
            .EnqueueControlMessageAsync(message, CancellationToken.None);
    }

    private static async Task<IReadOnlyList<TaskControlMessage>> ReadControlsAsync(
        DbContextOptions<TaskRuntimeDbContext> options,
        ISystemClock clock,
        TaskExecutionContext context)
    {
        await using TaskRuntimeDbContext dbContext = new(options);
        return await new TaskRuntimeRunStore(dbContext, clock)
            .ReadPendingAsync(context, 10, CancellationToken.None);
    }

    private static async Task RunRetentionAsync(
        string provider,
        string connectionString,
        MutableClock clock)
    {
        ServiceCollection services = new();
        services.AddSingleton<ISystemClock>(clock);
        services.AddDbContext<TaskRuntimeDbContext>(options => ConfigureProvider(options, provider, connectionString));
        await using ServiceProvider serviceProvider = services.BuildServiceProvider(validateScopes: true);
        TaskRuntimeRetentionOptions retentionOptions = new()
        {
            ExpiredControlRetention = TimeSpan.FromDays(30),
            HandledControlRetention = TimeSpan.FromDays(365),
            FailedControlRetention = TimeSpan.FromDays(365),
            SucceededRunRetention = TimeSpan.FromDays(365),
            FailedRunRetention = TimeSpan.FromDays(365),
            CanceledRunRetention = TimeSpan.FromDays(365),
            TimedOutRunRetention = TimeSpan.FromDays(365),
            BatchSize = 10,
            MaxBatchesPerStatusPerCycle = 2,
        };
        TaskRuntimeRetentionService retention = new(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(retentionOptions),
            NullLogger<TaskRuntimeRetentionService>.Instance);

        await retention.CleanupOnceAsync(retentionOptions, CancellationToken.None);
    }

    private static DbContextOptions<TaskRuntimeDbContext> CreateOptions(string provider, string connectionString)
    {
        DbContextOptionsBuilder<TaskRuntimeDbContext> builder = new();
        ConfigureProvider(builder, provider, connectionString);
        return builder.Options;
    }

    private static void ConfigureProvider(
        DbContextOptionsBuilder options,
        string provider,
        string connectionString)
    {
        if (string.Equals(provider, "PostgreSql", StringComparison.Ordinal))
        {
            options.UseNpgsql(
                connectionString,
                database =>
                {
                    database.MigrationsAssembly(TaskRuntimeMigrations.PostgreSqlAssembly);
                    database.MigrationsHistoryTable(
                        TaskRuntimeMigrations.HistoryTable,
                        TaskRuntimeMigrations.Schema);
                });
            return;
        }

        options.UseSqlServer(
            connectionString,
            database =>
            {
                database.MigrationsAssembly(TaskRuntimeMigrations.SqlServerAssembly);
                database.MigrationsHistoryTable(
                    TaskRuntimeMigrations.HistoryTable,
                    TaskRuntimeMigrations.Schema);
            });
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : ISystemClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class OneShotSaveChangesInterceptor(Func<CancellationToken, Task> beforeFirstSave)
        : SaveChangesInterceptor
    {
        private int invoked;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref this.invoked, 1) == 0)
            {
                await beforeFirstSave(cancellationToken);
            }

            return result;
        }
    }
}
