namespace Gma.Modules.TaskRuntime.Persistence.Configurations;

using Gma.Framework.Naming;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Gma.Framework.Tasks;
using Gma.Framework.Tasks.Infrastructure;

internal sealed class TaskRunConfiguration : IEntityTypeConfiguration<TaskRun>
{
    public void Configure(EntityTypeBuilder<TaskRun> builder)
    {
        builder.ToTable("task_runs");
        builder.HasKey(taskRun => taskRun.Id);
        builder.Property(taskRun => taskRun.ModuleName).HasMaxLength(TaskRun.ModuleNameMaxLength).IsRequired();
        builder.Property(taskRun => taskRun.TaskName).HasMaxLength(TaskRun.TaskNameMaxLength).IsRequired();
        builder.Property(taskRun => taskRun.WorkerGroup).HasMaxLength(TaskRun.WorkerGroupMaxLength).IsRequired();
        builder.Property(taskRun => taskRun.PayloadVersion).IsRequired();
        builder.Property(taskRun => taskRun.Status).HasConversion<int>().IsRequired();
        builder.Property(taskRun => taskRun.Payload).HasMaxLength(TaskRunRequest.PayloadMaxLength).IsRequired();
        builder.Property(taskRun => taskRun.DeduplicationKey).HasMaxLength(TaskRun.DeduplicationKeyMaxLength);
        builder.Property(taskRun => taskRun.ActiveDeduplicationIdentity)
            .HasMaxLength(TaskRunRequest.DeduplicationIdentityMaxLength);
        builder.Property(taskRun => taskRun.ScopeId).HasMaxLength(ScopeIds.MaxLength);
        builder.Property(taskRun => taskRun.RequestedBy).HasMaxLength(TaskNames.ActorMaxLength);
        builder.Property(taskRun => taskRun.LockedBy).HasMaxLength(TaskRun.WorkerIdMaxLength);
        builder.Property(taskRun => taskRun.NodeId).HasMaxLength(TaskRun.WorkerIdMaxLength);
        builder.Property(taskRun => taskRun.ProgressMessage).HasMaxLength(TaskRun.ProgressMessageMaxLength);
        builder.Property(taskRun => taskRun.LastError).HasMaxLength(TaskRun.ErrorMaxLength);
        builder.Property(taskRun => taskRun.CancellationRequestedBy).HasMaxLength(TaskNames.ActorMaxLength);
        builder.Property(taskRun => taskRun.LeaseGeneration).IsRequired();
        builder.Property(taskRun => taskRun.ConcurrencyVersion).IsConcurrencyToken().IsRequired();
        builder.HasIndex(taskRun => taskRun.ActiveDeduplicationIdentity).IsUnique();
        builder.HasIndex(taskRun => new
        {
            taskRun.WorkerGroup,
            taskRun.Status,
            taskRun.ScheduledAtUtc,
            taskRun.NextAttemptAtUtc,
            taskRun.LockedUntilUtc
        });
        builder.HasIndex(taskRun => new
        {
            taskRun.WorkerGroup,
            taskRun.ScheduledAtUtc,
            taskRun.CreatedAtUtc,
            taskRun.Id
        });
        builder.HasIndex(taskRun => new { taskRun.ModuleName, taskRun.TaskName });
        builder.HasIndex(taskRun => new { taskRun.ScopeId, taskRun.CreatedAtUtc });
        builder.HasIndex(taskRun => new { taskRun.Status, taskRun.CompletedAtUtc });
        builder.HasIndex(taskRun => new
        {
            taskRun.ModuleName,
            taskRun.TaskName,
            taskRun.ScopeId,
            taskRun.DeduplicationKey,
            taskRun.Status
        });
    }
}
