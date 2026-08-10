namespace Gma.Modules.TaskRuntime.Tests;

using Gma.Framework.Cqrs;
using Gma.Framework.Results;
using Gma.Framework.Tasks;
using Gma.Modules.TaskRuntime.Application;
using Gma.Modules.TaskRuntime.Application.Commands;
using Gma.Modules.TaskRuntime.Application.Queries;
using Gma.Modules.TaskRuntime.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[Trait("Category", "Unit")]
public sealed class TaskRunOperationsTests
{
    private static readonly Guid RunId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Application_registration_is_idempotent_and_exposes_contract_facades()
    {
        ServiceCollection services = new();

        services.AddTaskRuntimeApplication();
        services.AddTaskRuntimeApplication();

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(TaskRunOperations));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ITaskRunEnqueuer));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ITaskRunReader));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ITaskRunController));
    }

    [Fact]
    public void Application_does_not_export_cqrs_use_case_messages()
    {
        Type[] exportedMessages = typeof(DependencyInjection).Assembly
            .GetExportedTypes()
            .Where(type =>
                type.Namespace is not null &&
                (type.Namespace.EndsWith(".Commands", StringComparison.Ordinal) ||
                 type.Namespace.EndsWith(".Queries", StringComparison.Ordinal)))
            .ToArray();

        Assert.Empty(exportedMessages);
    }

    [Fact]
    public async Task Contract_facades_dispatch_the_existing_use_cases_without_changing_coordinates()
    {
        TaskRunDetails details = CreateDetails();
        TaskControlMessage message = new(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            RunId,
            "tasks.pause",
            "{}",
            Now,
            "operator",
            Now.AddMinutes(5));
        RecordingRequestDispatcher dispatcher = new(
            Result.Success(details),
            Result.Success(details),
            Result.Success(new TaskRunPage([details.Summary], 1, 2, 25)),
            Result.Success(new TaskRunStats([new TaskRunStatusCount(TaskRunStatus.Queued, 1)])),
            Result.Success(Unit.Value),
            Result.Success(Unit.Value),
            Result.Success(message));
        TaskRunOperations operations = new(dispatcher);
        CancellationTokenSource cancellation = new();

        Result<TaskRunDetails> enqueued = await operations.EnqueueAsync(
            new TaskRunEnqueueRequest(
                "reports",
                "daily",
                "{\"day\":1}",
                Now.AddMinutes(1),
                "reports-workers",
                "tenant-a",
                Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                "operator",
                3,
                2,
                "daily-1",
                RunId),
            cancellation.Token);
        Result<TaskRunDetails> loaded = await operations.GetAsync(RunId, cancellation.Token);
        Result<TaskRunPage> listed = await operations.ListAsync(
            new TaskRunListRequest("reports", "daily", "reports-workers", TaskRunStatus.Queued, "tenant-a", 2, 25),
            cancellation.Token);
        Result<TaskRunStats> stats = await operations.GetStatsAsync(
            new TaskRunStatsRequest("reports", "daily", "reports-workers", "tenant-a"),
            cancellation.Token);
        Result canceled = await operations.CancelAsync(RunId, "operator", cancellation.Token);
        Result retried = await operations.RetryAsync(RunId, "operator", Now.AddMinutes(2), cancellation.Token);
        Result<TaskControlMessage> controlled = await operations.SendControlMessageAsync(
            new TaskRunControlRequest(RunId, "tasks.pause", "{}", Now.AddMinutes(5), "operator"),
            cancellation.Token);

        Assert.All(
            [enqueued.IsSuccess, loaded.IsSuccess, listed.IsSuccess, stats.IsSuccess, canceled.IsSuccess, retried.IsSuccess, controlled.IsSuccess],
            Assert.True);
        Assert.Collection(
            dispatcher.Requests,
            request =>
            {
                EnqueueTaskRunCommand command = Assert.IsType<EnqueueTaskRunCommand>(request);
                Assert.Equal(RunId, command.RunId);
                Assert.Equal("reports", command.ModuleName);
                Assert.Equal("daily", command.TaskName);
                Assert.Equal("daily-1", command.DeduplicationKey);
            },
            request => Assert.Equal(RunId, Assert.IsType<GetTaskRunQuery>(request).RunId),
            request =>
            {
                ListTaskRunsQuery query = Assert.IsType<ListTaskRunsQuery>(request);
                Assert.Equal(TaskRunStatus.Queued, query.Status);
                Assert.Equal(2, query.Page);
                Assert.Equal(25, query.PageSize);
            },
            request => Assert.Equal("tenant-a", Assert.IsType<GetTaskRunStatsQuery>(request).ScopeId),
            request => Assert.Equal("operator", Assert.IsType<CancelTaskRunCommand>(request).RequestedBy),
            request => Assert.Equal(Now.AddMinutes(2), Assert.IsType<RetryTaskRunCommand>(request).ScheduledAtUtc),
            request => Assert.Equal("tasks.pause", Assert.IsType<SendTaskControlMessageCommand>(request).CommandName));
        Assert.All(dispatcher.CancellationTokens, token => Assert.Equal(cancellation.Token, token));
    }

    private static TaskRunDetails CreateDetails() =>
        new(
            new TaskRunSummary(
                RunId,
                "reports",
                "daily",
                "reports-workers",
                2,
                TaskRunStatus.Queued,
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
                "daily-1"),
            "{\"day\":1}",
            null,
            null,
            null,
            null,
            null);

    private sealed class RecordingRequestDispatcher(params object[] results) : IRequestDispatcher
    {
        private readonly Queue<object> results = new(results);

        public List<object> Requests { get; } = [];
        public List<CancellationToken> CancellationTokens { get; } = [];

        public Task<Result<TResponse>> SendAsync<TResponse>(
            ICommand<TResponse> command,
            CancellationToken cancellationToken = default) =>
            this.Record<TResponse>(command, cancellationToken);

        public Task<Result<TResponse>> QueryAsync<TResponse>(
            IQuery<TResponse> query,
            CancellationToken cancellationToken = default) =>
            this.Record<TResponse>(query, cancellationToken);

        private Task<Result<TResponse>> Record<TResponse>(object request, CancellationToken cancellationToken)
        {
            this.Requests.Add(request);
            this.CancellationTokens.Add(cancellationToken);
            return Task.FromResult((Result<TResponse>)this.results.Dequeue());
        }
    }
}
