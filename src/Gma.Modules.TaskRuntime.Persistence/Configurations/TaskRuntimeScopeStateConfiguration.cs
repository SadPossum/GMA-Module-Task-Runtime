namespace Gma.Modules.TaskRuntime.Persistence.Configurations;

using Gma.Framework.Naming;
using Gma.Modules.TaskRuntime.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class TaskRuntimeScopeStateConfiguration
    : IEntityTypeConfiguration<TaskRuntimeScopeState>
{
    public void Configure(EntityTypeBuilder<TaskRuntimeScopeState> builder)
    {
        builder.ToTable("task_scope_states", table =>
        {
            table.HasTrigger("task_scope_states_closed_immutable");
            table.HasCheckConstraint(
                "CK_task_scope_states_revision",
                "\"SelectedRevision\" >= 0 AND \"ResultingRevision\" = \"SelectedRevision\" + 1");
            table.HasCheckConstraint(
                "CK_task_scope_states_status",
                "(\"Status\" = 1 AND \"ClosedAtUtc\" IS NULL) OR " +
                "(\"Status\" = 2 AND \"ClosedAtUtc\" IS NOT NULL AND " +
                "\"ClosedAtUtc\" = \"UpdatedAtUtc\")");
            table.HasCheckConstraint(
                "CK_task_scope_states_progress",
                "\"ConcurrencyVersion\" >= 1 AND " +
                "\"UpdatedAtUtc\" >= \"StartedAtUtc\"");
        });
        builder.HasKey(state => state.ScopeId);
        builder.Property(state => state.ScopeId)
            .HasMaxLength(ScopeIds.MaxLength)
            .IsRequired();
        builder.Property(state => state.RequestSha256)
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        builder.Property(state => state.Status).HasConversion<int>().IsRequired();
        builder.Property(state => state.ConcurrencyVersion)
            .IsConcurrencyToken()
            .IsRequired();
        builder.HasIndex(state => state.OperationId).IsUnique();
        builder.HasIndex(state => new { state.Status, state.ScopeId });
    }
}
