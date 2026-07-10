namespace Gma.Modules.TaskRuntime.Contracts;

using Gma.Framework.Permissions;
using Gma.Framework.ModuleComposition;
using Gma.Framework.Modules;

public static class TaskRuntimeModuleMetadata
{
    public const string Name = "task-runtime";
    public const string Schema = "tasks";
    public const string AdminSurfaceName = "tasks";

    public static ModuleDescriptor Descriptor { get; } = ModuleDescriptor
        .Create(Name)
        .WithSchema(Schema)
        .WithAdminSurfaceName(AdminSurfaceName)
        .WithPermissions([
            new ModulePermissionDescriptor(TaskRuntimePermissionCodes.RunsRead, "Read task runs.", scopeRequirement: PermissionScopeRequirement.Global),
            new ModulePermissionDescriptor(TaskRuntimePermissionCodes.RunsCreate, "Create task runs.", scopeRequirement: PermissionScopeRequirement.Global),
            new ModulePermissionDescriptor(TaskRuntimePermissionCodes.RunsCancel, "Cancel task runs.", scopeRequirement: PermissionScopeRequirement.Global),
            new ModulePermissionDescriptor(TaskRuntimePermissionCodes.RunsRetry, "Retry task runs.", scopeRequirement: PermissionScopeRequirement.Global),
            new ModulePermissionDescriptor(TaskRuntimePermissionCodes.RunsControl, "Send task run control messages.", scopeRequirement: PermissionScopeRequirement.Global),
        ])
        .WithProfile(TaskRuntimeProfiles.Default)
        .Build();
}
