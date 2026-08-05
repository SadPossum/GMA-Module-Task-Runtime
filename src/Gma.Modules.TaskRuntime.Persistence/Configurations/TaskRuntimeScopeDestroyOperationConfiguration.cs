namespace Gma.Modules.TaskRuntime.Persistence.Configurations;

using Gma.Framework.Naming;
using Gma.Modules.TaskRuntime.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class TaskRuntimeScopeDestroyOperationConfiguration
    : IEntityTypeConfiguration<TaskRuntimeScopeDestroyOperation>
{
    public void Configure(
        EntityTypeBuilder<TaskRuntimeScopeDestroyOperation> builder)
    {
        builder.ToTable("task_scope_destroy_operations", table =>
        {
            table.HasCheckConstraint(
                "CK_task_scope_destroy_operations_revision",
                "\"SelectedRevision\" >= 0 AND \"ResultingRevision\" = \"SelectedRevision\" + 1");
            table.HasCheckConstraint(
                "CK_task_scope_destroy_operations_batch",
                "\"BatchSize\" >= 1 AND \"BatchSize\" <= 1000");
            table.HasCheckConstraint(
                "CK_task_scope_destroy_operations_stage",
                "\"Stage\" >= 1 AND \"Stage\" <= 4");
            table.HasCheckConstraint(
                "CK_task_scope_destroy_operations_counts",
                "((\"RemovedRecordCount\" = 0 AND " +
                "\"CompletedBatchCount\" = 0) OR " +
                "(\"RemovedRecordCount\" > 0 AND " +
                "\"CompletedBatchCount\" > 0)) AND " +
                "\"ProofVersion\" = 1 AND " +
                "\"ConcurrencyVersion\" >= 1 AND " +
                "\"UpdatedAtUtc\" >= \"StartedAtUtc\"");
        });
        builder.HasKey(operation => operation.OperationId);
        builder.Property(operation => operation.ScopeId)
            .HasMaxLength(ScopeIds.MaxLength)
            .IsRequired();
        builder.Property(operation => operation.RequestSha256)
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        builder.Property(operation => operation.Stage)
            .HasConversion<int>()
            .IsRequired();
        builder.Property(operation => operation.RemovalProofSha256)
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        builder.Property(operation => operation.ConcurrencyVersion)
            .IsConcurrencyToken()
            .IsRequired();
        builder.HasIndex(operation => operation.ScopeId).IsUnique();
        builder.HasOne<TaskRuntimeScopeState>()
            .WithMany()
            .HasForeignKey(operation => operation.ScopeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
