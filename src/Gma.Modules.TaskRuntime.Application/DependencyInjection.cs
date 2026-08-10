namespace Gma.Modules.TaskRuntime.Application;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Gma.Framework.Application.Composition;
using Gma.Modules.TaskRuntime.Contracts;

public static class DependencyInjection
{
    public static IServiceCollection AddTaskRuntimeApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<TaskRunOperations>();
        services.TryAddScoped<ITaskRunEnqueuer>(provider =>
            provider.GetRequiredService<TaskRunOperations>());
        services.TryAddScoped<ITaskRunReader>(provider =>
            provider.GetRequiredService<TaskRunOperations>());
        services.TryAddScoped<ITaskRunController>(provider =>
            provider.GetRequiredService<TaskRunOperations>());
        services.AddApplicationServicesFromAssembly(typeof(DependencyInjection).Assembly);

        return services;
    }
}
