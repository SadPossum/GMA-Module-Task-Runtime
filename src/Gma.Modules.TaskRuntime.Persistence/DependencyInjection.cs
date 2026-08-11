namespace Gma.Modules.TaskRuntime.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Gma.Framework.Tasks;
using Gma.Framework.Cqrs.UnitOfWork;
using Gma.Framework.ModuleComposition;
using Gma.Framework.Persistence.EntityFrameworkCore;
using Gma.Modules.TaskRuntime.Contracts;

public static class DependencyInjection
{
    public static IHostApplicationBuilder AddTaskRuntimePersistence(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddPersistenceOptions(builder.Configuration);
        builder.Services.TryAddModuleDbContext<TaskRuntimeDbContext>(options =>
            options.UseConfiguredProvider(
                builder.Configuration,
                TaskRuntimeMigrations.SqlServerAssembly,
                TaskRuntimeMigrations.PostgreSqlAssembly,
                TaskRuntimeMigrations.Schema,
                TaskRuntimeMigrations.HistoryTable));
        builder.Services
            .AddOptions<TaskRuntimeRetentionOptions>()
            .Bind(builder.Configuration.GetSection(TaskRuntimeRetentionOptions.SectionName))
            .ValidateOnStart();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IValidateOptions<TaskRuntimeRetentionOptions>,
                TaskRuntimeRetentionOptionsValidator>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, TaskRuntimeRetentionService>());
        builder.Services.TryAddSingleton<TaskRuntimeRetentionMetrics>();

        builder.Services.TryAddScoped<ITaskRunStore, TaskRuntimeRunStore>();
        builder.Services.TryAddScoped<ITaskRuntimeReporter>(provider => provider.GetRequiredService<ITaskRunStore>());
        builder.Services.TryAddScoped<ITaskControlChannel>(provider => provider.GetRequiredService<ITaskRunStore>());
        builder.Services.TryAddScoped<
            ITaskRuntimeScopeLifecycle,
            TaskRuntimeScopeLifecycleService>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IUnitOfWork, TaskRuntimeUnitOfWork>());
        builder.ProvideFeature(TasksCompositionFeatures.RunStoreProvided("Gma.Modules.TaskRuntime.Persistence"));
        builder.ProvideFeature(TasksCompositionFeatures.RuntimeReporterProvided("Gma.Modules.TaskRuntime.Persistence"));
        builder.ProvideFeature(TasksCompositionFeatures.ControlChannelProvided("Gma.Modules.TaskRuntime.Persistence"));

        return builder;
    }
}
