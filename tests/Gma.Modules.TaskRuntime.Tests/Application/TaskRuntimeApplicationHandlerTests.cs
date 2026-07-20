namespace Gma.Modules.TaskRuntime.Tests;

using Gma.Framework.Results;
using Gma.Framework.Cqrs;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Runtime.Time;
using Gma.Framework.Tasks;
using Gma.Modules.TaskRuntime.Application;
using Gma.Modules.TaskRuntime.Application.Commands;
using Gma.Modules.TaskRuntime.Application.Handlers;
using Gma.Modules.TaskRuntime.Application.Queries;
using Xunit;

[Trait("Category", "Unit")]
public sealed class TaskRuntimeApplicationHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 20, 0, 0, TimeSpan.Zero);
    private static readonly Guid RequestedRunId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CanonicalRunId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task Enqueue_returns_the_canonical_deduplicated_run()
    {
        TaskRunDetails canonical = CreateDetails(CanonicalRunId, TaskRunStatus.Queued);
        StubTaskRunStore store = new()
        {
            EnqueueResult = new TaskRunEnqueueResult(canonical, Created: false),
        };
        EnqueueTaskRunCommandHandler handler = new(store, new FixedIdGenerator(RequestedRunId), new FixedClock(Now));

        Result<TaskRunDetails> result = await handler.HandleAsync(
            CreateEnqueueCommand(RequestedRunId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CanonicalRunId, result.Value.Summary.RunId);
        Assert.Equal(RequestedRunId, store.LastEnqueueRequest?.RunId);
    }

    [Fact]
    public async Task Enqueue_maps_shared_contract_validation_to_a_stable_error()
    {
        StubTaskRunStore store = new();
        EnqueueTaskRunCommandHandler handler = new(store, new FixedIdGenerator(RequestedRunId), new FixedClock(Now));
        EnqueueTaskRunCommand command = CreateEnqueueCommand(RequestedRunId) with { MaxAttempts = 0 };

        Result<TaskRunDetails> result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(TaskRuntimeApplicationErrors.InvalidRunRequest, result.Error);
        Assert.Null(store.LastEnqueueRequest);
    }

    [Theory]
    [InlineData(TaskRunMutationOutcome.Applied, true, null)]
    [InlineData(TaskRunMutationOutcome.AlreadyApplied, true, null)]
    [InlineData(TaskRunMutationOutcome.NotFound, false, "TaskRuntime.RunNotFound")]
    [InlineData(TaskRunMutationOutcome.InvalidState, false, "TaskRuntime.RunCannotBeCanceled")]
    [InlineData(TaskRunMutationOutcome.Conflict, false, "TaskRuntime.ConcurrentMutation")]
    [InlineData(TaskRunMutationOutcome.InvalidRequest, false, "TaskRuntime.InvalidRunRequest")]
    public async Task Cancel_maps_atomic_store_outcomes(
        TaskRunMutationOutcome outcome,
        bool success,
        string? errorCode)
    {
        StubTaskRunStore store = new() { CancellationOutcome = outcome };
        CancelTaskRunCommandHandler handler = new(store, new FixedClock(Now));

        Result<Unit> result = await handler.HandleAsync(
            new CancelTaskRunCommand(RequestedRunId, "operator"),
            CancellationToken.None);

        Assert.Equal(success, result.IsSuccess);
        Assert.Equal(errorCode, result.IsFailure ? result.Error.Code : null);
    }

    [Theory]
    [InlineData(TaskControlMessageEnqueueOutcome.Enqueued, true, null)]
    [InlineData(TaskControlMessageEnqueueOutcome.AlreadyExists, true, null)]
    [InlineData(TaskControlMessageEnqueueOutcome.RunNotFound, false, "TaskRuntime.RunNotFound")]
    [InlineData(TaskControlMessageEnqueueOutcome.RunTerminal, false, "TaskRuntime.RunCannotBeControlled")]
    [InlineData(TaskControlMessageEnqueueOutcome.Conflict, false, "TaskRuntime.ConcurrentMutation")]
    public async Task Control_maps_atomic_store_outcomes(
        TaskControlMessageEnqueueOutcome outcome,
        bool success,
        string? errorCode)
    {
        StubTaskRunStore store = new() { ControlOutcome = outcome };
        SendTaskControlMessageCommandHandler handler = new(
            store,
            new FixedIdGenerator(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")),
            new FixedClock(Now));

        Result<TaskControlMessage> result = await handler.HandleAsync(
            new SendTaskControlMessageCommand(
                RequestedRunId,
                "tasks.pause",
                "{}",
                Now.AddMinutes(1),
                "operator"),
            CancellationToken.None);

        Assert.Equal(success, result.IsSuccess);
        Assert.Equal(errorCode, result.IsFailure ? result.Error.Code : null);
    }

    [Theory]
    [InlineData(TaskRunMutationOutcome.Applied, true, null)]
    [InlineData(TaskRunMutationOutcome.NotFound, false, "TaskRuntime.RunNotFound")]
    [InlineData(TaskRunMutationOutcome.InvalidState, false, "TaskRuntime.RunCannotBeRetried")]
    [InlineData(TaskRunMutationOutcome.Conflict, false, "TaskRuntime.ConcurrentMutation")]
    [InlineData(TaskRunMutationOutcome.InvalidRequest, false, "TaskRuntime.InvalidRunRequest")]
    public async Task Retry_maps_atomic_store_outcomes(
        TaskRunMutationOutcome outcome,
        bool success,
        string? errorCode)
    {
        StubTaskRunStore store = new() { RetryOutcome = outcome };
        RetryTaskRunCommandHandler handler = new(store, new FixedClock(Now));

        Result<Unit> result = await handler.HandleAsync(
            new RetryTaskRunCommand(RequestedRunId, "operator", Now.AddMinutes(1)),
            CancellationToken.None);

        Assert.Equal(success, result.IsSuccess);
        Assert.Equal(errorCode, result.IsFailure ? result.Error.Code : null);
    }

    [Fact]
    public async Task List_maps_invalid_filter_to_a_stable_error()
    {
        StubTaskRunStore store = new();
        ListTaskRunsQueryHandler handler = new(store);

        Result<TaskRunPage> result = await handler.HandleAsync(
            new ListTaskRunsQuery(
                new string('a', TaskNames.ModuleNameMaxLength + 1),
                null,
                null,
                null,
                null,
                1,
                50),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(TaskRuntimeApplicationErrors.InvalidRunFilter, result.Error);
    }

    [Fact]
    public async Task Stats_maps_invalid_filter_to_a_stable_error()
    {
        StubTaskRunStore store = new();
        GetTaskRunStatsQueryHandler handler = new(store);

        Result<TaskRunStats> result = await handler.HandleAsync(
            new GetTaskRunStatsQuery(null, null, "not valid", null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(TaskRuntimeApplicationErrors.InvalidRunFilter, result.Error);
    }

    private static EnqueueTaskRunCommand CreateEnqueueCommand(Guid runId) =>
        new(
            runId,
            "task-tests",
            "generate-report",
            "{}",
            Now,
            TaskWorkerGroups.Default,
            "tenant-a",
            null,
            "operator",
            3,
            1,
            "daily-report");

    private static TaskRunDetails CreateDetails(Guid runId, TaskRunStatus status) =>
        new(
            new TaskRunSummary(
                runId,
                "task-tests",
                "generate-report",
                TaskWorkerGroups.Default,
                1,
                status,
                "tenant-a",
                null,
                Now,
                Now,
                null,
                null,
                0,
                3,
                null,
                null,
                null,
                null,
                null,
                null,
                "operator",
                "daily-report"),
            "{}",
            null,
            null,
            null,
            null,
            null);

    private sealed class StubTaskRunStore : ITaskRunStore
    {
        public TaskRunEnqueueResult EnqueueResult { get; init; } =
            new(CreateDetails(RequestedRunId, TaskRunStatus.Queued), Created: true);
        public TaskRunMutationOutcome CancellationOutcome { get; init; } = TaskRunMutationOutcome.Applied;
        public TaskRunMutationOutcome RetryOutcome { get; init; } = TaskRunMutationOutcome.Applied;
        public TaskControlMessageEnqueueOutcome ControlOutcome { get; init; } = TaskControlMessageEnqueueOutcome.Enqueued;
        public TaskRunRequest? LastEnqueueRequest { get; private set; }

        public Task<TaskRunEnqueueResult> EnqueueAsync(TaskRunRequest request, CancellationToken cancellationToken)
        {
            this.LastEnqueueRequest = request;
            return Task.FromResult(this.EnqueueResult);
        }

        public Task<TaskRunMutationOutcome> RequestCancellationAsync(
            Guid runId,
            string? requestedBy,
            DateTimeOffset requestedAtUtc,
            CancellationToken cancellationToken) => Task.FromResult(this.CancellationOutcome);

        public Task<TaskControlMessageEnqueueOutcome> EnqueueControlMessageAsync(
            TaskControlMessage message,
            CancellationToken cancellationToken) => Task.FromResult(this.ControlOutcome);

        public Task<TaskRunPage> ListAsync(TaskRunFilter filter, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<TaskRunDetails?> GetAsync(Guid runId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<TaskRunStats> GetStatsAsync(TaskRunStatsFilter filter, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<TaskRunLease>> ClaimReadyAsync(TaskWorkerClaim claim, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<TaskRunMutationOutcome> MarkStartedAsync(TaskExecutionContext context, DateTimeOffset startedAtUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<TaskRunMutationOutcome> MarkSucceededAsync(TaskExecutionContext context, DateTimeOffset completedAtUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<TaskRunMutationOutcome> MarkCanceledAsync(TaskExecutionContext context, DateTimeOffset canceledAtUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<TaskRunMutationOutcome> MarkFailedAsync(TaskExecutionContext context, string error, DateTimeOffset failedAtUtc, DateTimeOffset? retryAtUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<TaskRunMutationOutcome> RetryAsync(Guid runId, string? requestedBy, DateTimeOffset scheduledAtUtc, CancellationToken cancellationToken) =>
            Task.FromResult(this.RetryOutcome);
        public Task<IReadOnlyList<TaskRunSummary>> MarkStaleTimedOutAsync(DateTimeOffset nowUtc, TimeSpan staleAfter, int maxRuns, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<TaskRunMutationOutcome> ReportHeartbeatAsync(TaskExecutionContext context, DateTimeOffset observedAtUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<TaskRunMutationOutcome> ReportProgressAsync(TaskExecutionContext context, TaskProgress progress, DateTimeOffset observedAtUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<TaskControlMessage>> ReadPendingAsync(TaskExecutionContext context, int maxMessages, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<TaskRunMutationOutcome> MarkHandledAsync(TaskExecutionContext context, Guid messageId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<TaskRunMutationOutcome> MarkFailedAsync(TaskExecutionContext context, Guid messageId, string error, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : ISystemClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class FixedIdGenerator(Guid id) : IIdGenerator
    {
        public Guid NewId() => id;
    }
}
