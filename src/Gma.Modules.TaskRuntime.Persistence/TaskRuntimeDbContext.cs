namespace Gma.Modules.TaskRuntime.Persistence;

using Microsoft.EntityFrameworkCore;
using Gma.Framework.Tasks.Infrastructure;

public sealed class TaskRuntimeDbContext(DbContextOptions<TaskRuntimeDbContext> options) : DbContext(options)
{
    private const string SqlServerOrdinalCollation = "Latin1_General_100_BIN2";

    public DbSet<TaskRun> TaskRuns => this.Set<TaskRun>();
    public DbSet<TaskControlMessageState> TaskControlMessages => this.Set<TaskControlMessageState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(TaskRuntimeMigrations.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TaskRuntimeDbContext).Assembly);

        if (this.Database.IsSqlServer())
        {
            modelBuilder.Entity<TaskRun>()
                .Property(taskRun => taskRun.ScopeId)
                .UseCollation(SqlServerOrdinalCollation);
            modelBuilder.Entity<TaskRun>()
                .Property(taskRun => taskRun.ActiveDeduplicationIdentity)
                .UseCollation(SqlServerOrdinalCollation);
        }
    }
}
