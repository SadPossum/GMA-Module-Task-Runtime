namespace Gma.Modules.TaskRuntime.Tests.Persistence;

using Gma.Framework.Runtime.Time;
using Gma.Framework.Tasks;
using Gma.Framework.Tasks.Infrastructure;
using Gma.Modules.TaskRuntime.Contracts;
using Gma.Modules.TaskRuntime.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

[Trait("Category", "Unit")]
public sealed class TaskRuntimeScopeLifecycleTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Missing_scope_closes_and_replays_exact_receipt()
    {
        await using TaskRuntimeDbContext dbContext = CreateDbContext();
        TestClock clock = new(Now);
        TaskRuntimeScopeLifecycleService service = new(dbContext, clock);
        TaskRuntimeScopeDestroyRequest request = new(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            "tenant-a",
            ExpectedRevision: 0,
            BatchSize: 10);

        TaskRuntimeScopeSnapshot before = await service.GetSnapshotAsync(
            "tenant-a",
            CancellationToken.None);
        TaskRuntimeScopeDestroyResult completed = await service.DestroyBatchAsync(
            request,
            CancellationToken.None);
        TaskRuntimeScopeDestroyResult replayed = await service.DestroyBatchAsync(
            request,
            CancellationToken.None);
        TaskRuntimeScopeSnapshot after = await service.GetSnapshotAsync(
            "tenant-a",
            CancellationToken.None);

        Assert.Equal(TaskRuntimeScopeStatus.Missing, before.Status);
        Assert.Equal(0, before.Revision);
        Assert.Equal(TaskRuntimeScopeDestroyStatus.Completed, completed.Status);
        Assert.NotNull(completed.Receipt);
        Assert.Equal(0, completed.Receipt.SelectedRevision);
        Assert.Equal(1, completed.Receipt.ResultingRevision);
        Assert.Equal(0, completed.Receipt.RemovedRecordCount);
        Assert.Equal(TaskRuntimeScopeDestroyStatus.Replayed, replayed.Status);
        Assert.Equal(completed.Receipt, replayed.Receipt);
        Assert.Equal(TaskRuntimeScopeStatus.Closed, after.Status);
        Assert.Equal(0, after.SelectedRevision);
        Assert.Equal(1, after.Revision);
    }

    [Fact]
    public async Task Scope_closure_fences_new_work_and_removes_only_target_scope()
    {
        await using TaskRuntimeDbContext dbContext = CreateDbContext();
        TestClock clock = new(Now);
        TaskRuntimeRunStore store = new(dbContext, clock);
        TaskRuntimeScopeLifecycleService service = new(dbContext, clock);
        TaskRunRequest targetRequest = CreateRequest(
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            "tenant-a");
        TaskRunRequest otherRequest = CreateRequest(
            Guid.Parse("20000000-0000-0000-0000-000000000002"),
            "tenant-b");
        _ = await store.EnqueueAsync(targetRequest, CancellationToken.None);
        _ = await store.EnqueueAsync(otherRequest, CancellationToken.None);
        TaskControlMessage targetMessage = new(
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            targetRequest.RunId,
            "tasks.pause",
            "{}",
            Now);
        Assert.Equal(
            TaskControlMessageEnqueueOutcome.Enqueued,
            await store.EnqueueControlMessageAsync(
                targetMessage,
                CancellationToken.None));

        TaskRuntimeScopeDestroyRequest destroy = new(
            Guid.Parse("40000000-0000-0000-0000-000000000001"),
            "tenant-a",
            ExpectedRevision: 0,
            BatchSize: 1);
        TaskRuntimeScopeDestroyResult first = await service.DestroyBatchAsync(
            destroy,
            CancellationToken.None);

        Assert.Equal(TaskRuntimeScopeDestroyStatus.InProgress, first.Status);
        await Assert.ThrowsAsync<TaskScopeNotAcceptingWorkException>(() =>
            store.EnqueueAsync(
                CreateRequest(
                    Guid.Parse("20000000-0000-0000-0000-000000000003"),
                    "tenant-a"),
                CancellationToken.None));
        Assert.Equal(
            TaskRunMutationOutcome.ScopeClosed,
            await store.RetryAsync(
                targetRequest.RunId,
                "operator",
                Now.AddMinutes(1),
                CancellationToken.None));
        Assert.Equal(
            TaskControlMessageEnqueueOutcome.ScopeClosed,
            await store.EnqueueControlMessageAsync(
                new TaskControlMessage(
                    Guid.Parse("30000000-0000-0000-0000-000000000002"),
                    targetRequest.RunId,
                    "tasks.resume",
                    "{}",
                    Now.AddSeconds(1)),
                CancellationToken.None));

        TaskRuntimeScopeDestroyResult current = first;
        for (int attempt = 0;
             attempt < 5 && current.Status != TaskRuntimeScopeDestroyStatus.Completed;
             attempt++)
        {
            clock.UtcNow = clock.UtcNow.AddSeconds(1);
            current = await service.DestroyBatchAsync(
                destroy,
                CancellationToken.None);
        }

        Assert.Equal(TaskRuntimeScopeDestroyStatus.Completed, current.Status);
        Assert.Equal(2, current.Receipt!.RemovedRecordCount);
        Assert.False(await dbContext.TaskRuns.AnyAsync(
            run => run.ScopeId == "tenant-a"));
        Assert.False(await dbContext.TaskControlMessages.AnyAsync(
            message => message.ScopeId == "tenant-a"));
        Assert.True(await dbContext.TaskRuns.AnyAsync(
            run => run.ScopeId == "tenant-b"));
    }

    [Fact]
    public async Task Active_lease_blocks_completion_but_can_finish_cancellation()
    {
        await using TaskRuntimeDbContext dbContext = CreateDbContext();
        TestClock clock = new(Now);
        TaskRuntimeScopeLifecycleService service = new(dbContext, clock);
        TaskRun run = TaskRun.Enqueue(CreateRequest(
            Guid.Parse("50000000-0000-0000-0000-000000000001"),
            "tenant-a"));
        TaskRunLease lease = run.Claim(new TaskWorkerClaim(
            "workers",
            "worker-a",
            "node-a",
            Now,
            maxRuns: 1,
            leaseDuration: TimeSpan.FromMinutes(1)));
        run.MarkStarted(lease.CreateExecutionContext(), Now.AddSeconds(1));
        dbContext.TaskRuns.Add(run);
        await dbContext.SaveChangesAsync();

        TaskRuntimeScopeDestroyRequest request = new(
            Guid.Parse("60000000-0000-0000-0000-000000000001"),
            "tenant-a",
            ExpectedRevision: 0,
            BatchSize: 10);
        clock.UtcNow = Now.AddSeconds(2);
        TaskRuntimeScopeDestroyResult busy = await service.DestroyBatchAsync(
            request,
            CancellationToken.None);

        Assert.Equal(TaskRuntimeScopeDestroyStatus.Busy, busy.Status);
        Assert.Equal(1, busy.Progress!.RemainingActiveRunCount);
        Assert.Equal(TaskRunStatus.CancellationRequested, run.Status);

        run.MarkCanceled(lease.CreateExecutionContext(), Now.AddSeconds(3));
        await dbContext.SaveChangesAsync();
        TaskRuntimeScopeDestroyResult current = busy;
        for (int attempt = 0;
             attempt < 4 && current.Status != TaskRuntimeScopeDestroyStatus.Completed;
             attempt++)
        {
            clock.UtcNow = clock.UtcNow.AddSeconds(1);
            current = await service.DestroyBatchAsync(
                request,
                CancellationToken.None);
        }

        Assert.Equal(TaskRuntimeScopeDestroyStatus.Completed, current.Status);
        Assert.Equal(1, current.Receipt!.RemovedRecordCount);
    }

    [Fact]
    public async Task Operation_or_scope_reuse_with_different_request_conflicts()
    {
        await using TaskRuntimeDbContext dbContext = CreateDbContext();
        TaskRuntimeScopeLifecycleService service = new(
            dbContext,
            new TestClock(Now));
        Guid operationId =
            Guid.Parse("70000000-0000-0000-0000-000000000001");
        TaskRuntimeScopeDestroyResult completed = await service.DestroyBatchAsync(
            new TaskRuntimeScopeDestroyRequest(
                operationId,
                "tenant-a",
                ExpectedRevision: 0,
                BatchSize: 10),
            CancellationToken.None);

        TaskRuntimeScopeDestroyResult changedBatch = await service.DestroyBatchAsync(
            new TaskRuntimeScopeDestroyRequest(
                operationId,
                "tenant-a",
                ExpectedRevision: 0,
                BatchSize: 11),
            CancellationToken.None);
        TaskRuntimeScopeDestroyResult changedScope = await service.DestroyBatchAsync(
            new TaskRuntimeScopeDestroyRequest(
                operationId,
                "tenant-b",
                ExpectedRevision: 0,
                BatchSize: 10),
            CancellationToken.None);

        Assert.Equal(TaskRuntimeScopeDestroyStatus.Completed, completed.Status);
        Assert.Equal(TaskRuntimeScopeDestroyStatus.Conflict, changedBatch.Status);
        Assert.Equal(TaskRuntimeScopeDestroyStatus.Conflict, changedScope.Status);
    }

    private static TaskRuntimeDbContext CreateDbContext()
    {
        DbContextOptions<TaskRuntimeDbContext> options =
            new DbContextOptionsBuilder<TaskRuntimeDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;
        return new TaskRuntimeDbContext(options);
    }

    private static TaskRunRequest CreateRequest(Guid runId, string scopeId) =>
        new(
            runId,
            "test-module",
            "test-task",
            "{}",
            Now,
            Now,
            workerGroup: "workers",
            scopeId: scopeId,
            maxAttempts: 2);

    private sealed class TestClock(DateTimeOffset utcNow) : ISystemClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
