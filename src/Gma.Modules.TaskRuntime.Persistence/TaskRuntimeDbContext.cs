namespace Gma.Modules.TaskRuntime.Persistence;

using Microsoft.EntityFrameworkCore;
using Gma.Framework.Persistence.EntityFrameworkCore;
using Gma.Framework.Tasks.Infrastructure;
using Gma.Modules.TaskRuntime.Persistence.Entities;

public sealed class TaskRuntimeDbContext(DbContextOptions<TaskRuntimeDbContext> options) : DbContext(options)
{
    public DbSet<TaskRun> TaskRuns => this.Set<TaskRun>();
    public DbSet<TaskControlMessageState> TaskControlMessages => this.Set<TaskControlMessageState>();
    internal DbSet<TaskRuntimeScopeState> TaskScopeStates => this.Set<TaskRuntimeScopeState>();
    internal DbSet<TaskRuntimeScopeDestroyOperation> TaskScopeDestroyOperations =>
        this.Set<TaskRuntimeScopeDestroyOperation>();
    internal DbSet<TaskRuntimeScopeDestroyReceipt> TaskScopeDestroyReceipts =>
        this.Set<TaskRuntimeScopeDestroyReceipt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(TaskRuntimeMigrations.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TaskRuntimeDbContext).Assembly);
        modelBuilder.ApplyOrdinalScopeIdConventions(this);

        modelBuilder.Entity<TaskRun>()
            .Property(taskRun => taskRun.ActiveDeduplicationIdentity)
            .UseOrdinalStringComparison(this);
    }
}
