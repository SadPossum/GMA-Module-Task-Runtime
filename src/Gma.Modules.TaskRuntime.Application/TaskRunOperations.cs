namespace Gma.Modules.TaskRuntime.Application;

using Gma.Framework.Cqrs;
using Gma.Framework.Results;
using Gma.Framework.Tasks;
using Gma.Modules.TaskRuntime.Application.Commands;
using Gma.Modules.TaskRuntime.Application.Queries;
using Gma.Modules.TaskRuntime.Contracts;

internal sealed class TaskRunOperations(IRequestDispatcher dispatcher) :
    ITaskRunEnqueuer,
    ITaskRunReader,
    ITaskRunController
{
    public Task<Result<TaskRunDetails>> EnqueueAsync(
        TaskRunEnqueueRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return dispatcher.SendAsync(
            new EnqueueTaskRunCommand(
                request.RunId,
                request.ModuleName,
                request.TaskName,
                request.PayloadJson,
                request.ScheduledAtUtc,
                request.WorkerGroup,
                request.ScopeId,
                request.CorrelationId,
                request.RequestedBy,
                request.MaxAttempts,
                request.PayloadVersion,
                request.DeduplicationKey),
            cancellationToken);
    }

    public Task<Result<TaskRunDetails>> GetAsync(
        Guid runId,
        CancellationToken cancellationToken = default) =>
        dispatcher.QueryAsync(new GetTaskRunQuery(runId), cancellationToken);

    public Task<Result<TaskRunPage>> ListAsync(
        TaskRunListRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return dispatcher.QueryAsync(
            new ListTaskRunsQuery(
                request.ModuleName,
                request.TaskName,
                request.WorkerGroup,
                request.Status,
                request.ScopeId,
                request.Page,
                request.PageSize),
            cancellationToken);
    }

    public Task<Result<TaskRunStats>> GetStatsAsync(
        TaskRunStatsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return dispatcher.QueryAsync(
            new GetTaskRunStatsQuery(
                request.ModuleName,
                request.TaskName,
                request.WorkerGroup,
                request.ScopeId),
            cancellationToken);
    }

    public async Task<Result> CancelAsync(
        Guid runId,
        string? requestedBy,
        CancellationToken cancellationToken = default)
    {
        Result<Unit> result = await dispatcher.SendAsync(
                new CancelTaskRunCommand(runId, requestedBy),
                cancellationToken)
            .ConfigureAwait(false);
        return ToResult(result);
    }

    public async Task<Result> RetryAsync(
        Guid runId,
        string? requestedBy,
        DateTimeOffset? scheduledAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        Result<Unit> result = await dispatcher.SendAsync(
                new RetryTaskRunCommand(runId, requestedBy, scheduledAtUtc),
                cancellationToken)
            .ConfigureAwait(false);
        return ToResult(result);
    }

    public Task<Result<TaskControlMessage>> SendControlMessageAsync(
        TaskRunControlRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return dispatcher.SendAsync(
            new SendTaskControlMessageCommand(
                request.RunId,
                request.CommandName,
                request.PayloadJson,
                request.ExpiresAtUtc,
                request.RequestedBy),
            cancellationToken);
    }

    private static Result ToResult(Result<Unit> result) =>
        result.IsFailure ? Result.Failure(result.Error) : Result.Success();
}
