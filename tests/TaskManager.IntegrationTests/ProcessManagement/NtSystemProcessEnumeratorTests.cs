using System.Diagnostics;
using TaskManager.Domain.Services;

namespace TaskManager.IntegrationTests.ProcessManagement
{
    public class NtSystemProcessEnumeratorTests
    {
        [Fact]
        public void Capture_IncludesSelfPid_WithName()
        {
            var snapshots = new NtSystemProcessEnumerator().Capture();

            var self = snapshots.SingleOrDefault(s => s.Pid == Environment.ProcessId);
            self.ShouldNotBeNull();
            self.Name.ShouldNotBeEmpty();
            self.ThreadCount.ShouldBeGreaterThan(0);
            self.Ppid.ShouldNotBeNull();
            self.BasePriority.ShouldNotBeNull();
        }

        [Fact]
        public void Capture_PidsAreUnique()
        {
            var snapshots = new NtSystemProcessEnumerator().Capture();

            snapshots.Select(s => s.Pid).ShouldBeUnique();
        }

        [Fact]
        public void Capture_AtLeastMatchesHandleEnumerationCount()
        {
            // protected/system processes appear here but cannot be opened by handle-based enumeration
            var snapshotPids = new NtSystemProcessEnumerator().Capture().Select(s => s.Pid).ToHashSet();
            int handleVisible = System.Diagnostics.Process.GetProcesses().Count(p =>
            {
                try { return !string.IsNullOrEmpty(p.ProcessName); } catch { return false; }
            });

            snapshotPids.Count.ShouldBeGreaterThanOrEqualTo(handleVisible);
        }
    }
}
