namespace Gma.Modules.TaskRuntime.Persistence.Configurations;

using Gma.Framework.Naming;
using Gma.Modules.TaskRuntime.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class TaskRuntimeScopeDestroyReceiptConfiguration
    : IEntityTypeConfiguration<TaskRuntimeScopeDestroyReceipt>
{
    public void Configure(
        EntityTypeBuilder<TaskRuntimeScopeDestroyReceipt> builder)
    {
        builder.ToTable("task_scope_destroy_receipts", table =>
        {
            table.HasTrigger("task_scope_destroy_receipts_append_only");
            table.HasCheckConstraint(
                "CK_task_scope_destroy_receipts_revision",
                "\"SelectedRevision\" >= 0 AND \"ResultingRevision\" = \"SelectedRevision\" + 1");
            table.HasCheckConstraint(
                "CK_task_scope_destroy_receipts_batch",
                "\"BatchSize\" >= 1 AND \"BatchSize\" <= 1000");
            table.HasCheckConstraint(
                "CK_task_scope_destroy_receipts_counts",
                "((\"RemovedRecordCount\" = 0 AND " +
                "\"CompletedBatchCount\" = 0) OR " +
                "(\"RemovedRecordCount\" > 0 AND " +
                "\"CompletedBatchCount\" > 0)) AND " +
                "\"RemovalProofVersion\" = 1 AND " +
                "\"CompletedAtUtc\" >= \"StartedAtUtc\"");
        });
        builder.HasKey(receipt => receipt.OperationId);
        builder.Property(receipt => receipt.ScopeId)
            .HasMaxLength(ScopeIds.MaxLength)
            .IsRequired();
        builder.Property(receipt => receipt.RequestSha256)
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        builder.Property(receipt => receipt.RemovalProofSha256)
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        builder.HasIndex(receipt => receipt.ScopeId).IsUnique();
        builder.HasOne<TaskRuntimeScopeState>()
            .WithMany()
            .HasForeignKey(receipt => receipt.ScopeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
