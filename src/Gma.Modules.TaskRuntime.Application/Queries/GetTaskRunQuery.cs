namespace Gma.Modules.TaskRuntime.Application.Queries;

using Gma.Framework.Cqrs;
using Gma.Framework.Tasks;

internal sealed record GetTaskRunQuery(Guid RunId) : IQuery<TaskRunDetails>;
