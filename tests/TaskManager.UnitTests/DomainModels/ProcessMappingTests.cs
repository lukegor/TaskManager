using TaskManager.Domain.Models;
using TaskManager.Domain.Primitives;
using TaskManager.Domain.Services;

namespace TaskManager.UnitTests.DomainModels
{
    /// <summary>
    /// Contract: one mapping point for snapshot->model, in-place runtime updates raise
    /// per-field notification, and deep copies detach completely from the source row.
    /// </summary>
    public class ProcessMappingTests
    {
        private static readonly ProcessSnapshot Snapshot =
            new(Pid: 42, Name: "alpha", ThreadCount: 7, Ppid: 4, BasePriority: 8);

        private static readonly ProcessEnrichment Enrichment =
            new(@"C:\windows\alpha.exe", ArchitectureType.Bit64);

        [Fact]
        public void FromSnapshot_MapsEveryField()
        {
            var process = Process.FromSnapshot(Snapshot, Enrichment);

            process.Pid.ShouldBe(42);
            process.Name.ShouldBe("alpha");
            process.ThreadCount.ShouldBe(7);
            process.Ppid.ShouldBe(4);
            process.Priority.ShouldBe(8);
            process.Path.ShouldBe(@"C:\windows\alpha.exe");
            process.ArchitectureType.ShouldBe(ArchitectureType.Bit64);
        }

        [Fact]
        public void ApplySnapshot_UpdatesRuntimeMutableFields_AndRaisesNotification()
        {
            var process = Process.FromSnapshot(Snapshot, Enrichment);
            var notified = new List<string>();
            process.PropertyChanged += (_, e) => notified.Add(e.PropertyName!);

            process.ApplySnapshot(new ProcessSnapshot(42, "beta", ThreadCount: 9, Ppid: 4, BasePriority: 6));

            process.Name.ShouldBe("beta");
            process.ThreadCount.ShouldBe(9);
            process.Priority.ShouldBe(6);
            notified.ShouldContain(nameof(Process.Name));
            notified.ShouldContain(nameof(Process.ThreadCount));
            notified.ShouldContain(nameof(Process.Priority));
            process.Path.ShouldBe(@"C:\windows\alpha.exe"); // enrichment fields untouched
        }

        [Fact]
        public void DeepCopy_IsFullyDetached()
        {
            var original = Process.FromSnapshot(Snapshot, Enrichment);

            var copy = original.DeepCopy();
            copy.Priority = 4;
            copy.Name = "mutated";

            original.Priority.ShouldBe(8);
            original.Name.ShouldBe("alpha");
            copy.Pid.ShouldBe(original.Pid);
        }
    }
}
