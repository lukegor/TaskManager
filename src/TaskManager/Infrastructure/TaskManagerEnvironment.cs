namespace TaskManager.Infrastructure
{
    /// <summary>Well-known environment variables enabling test/portable redirections.</summary>
    internal static class TaskManagerEnvironment
    {
        public const string SettingsDir = "TASKMANAGER_SETTINGS_DIR";
        public const string LogDir = "TASKMANAGER_LOG_DIR";
        public const string InstanceName = "TASKMANAGER_INSTANCE_NAME";
    }
}
