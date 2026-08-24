using TaskManager.Domain.Models;
using TaskManager.Domain.Services;

namespace TaskManager.Tests.ProcessPipeline
{
    public class ProcessDiffEngineTests
    {
        private static Process Stored(int pid, string name = "n", int threads = 1, int? priority = 8, int? ppid = 4) =>
            new() { Name = name, Pid = pid, ThreadCount = threads, Priority = priority, Ppid = ppid, Path = string.Empty };

        private static ProcessSnapshot Snap(int pid, string name = "n", int threads = 1, int? priority = 8, int? ppid = 4) =>
            new(pid, name, threads, ppid, priority);

        [Fact]
        public void EmptyStore_AllSnapshotsAreAdded()
        {
            var diff = ProcessDiffEngine.Compute(
                new Dictionary<int, Process>(),
                [Snap(1), Snap(2)]);

            diff.Added.Select(s => s.Pid).ShouldBe(new[] { 1, 2 });
            diff.Removed.ShouldBeEmpty();
            diff.Updated.ShouldBeEmpty();
            diff.IsEmpty.ShouldBeFalse();
        }

        [Fact]
        public void VanishedPids_AreRemoved_InAscendingOrder()
        {
            var current = new Dictionary<int, Process> { [9] = Stored(9), [5] = Stored(5), [7] = Stored(7) };

            var diff = ProcessDiffEngine.Compute(current, [Snap(7)]);

            diff.Removed.ShouldBe(new[] { 5, 9 });
            diff.Added.ShouldBeEmpty();
            diff.Updated.ShouldBeEmpty();
        }

        [Fact]
        public void IdenticalState_IsEmpty()
        {
            var current = new Dictionary<int, Process> { [1] = Stored(1), [2] = Stored(2) };

            var diff = ProcessDiffEngine.Compute(current, [Snap(1), Snap(2)]);

            diff.IsEmpty.ShouldBeTrue();
        }

        [Theory]
        [InlineData("other", 1, 8, 4)]   // name changed
        [InlineData("n", 3, 8, 4)]       // thread count changed
        [InlineData("n", 1, 6, 4)]       // priority changed
        [InlineData("n", 1, 8, 100)]     // ppid changed
        public void AnyFieldDelta_QualifiesAsUpdated(string name, int threads, int? priority, int? ppid)
        {
            var current = new Dictionary<int, Process> { [1] = Stored(1) };

            var diff = ProcessDiffEngine.Compute(current, [Snap(1, name, threads, priority, ppid)]);

            diff.Updated.Select(s => s.Pid).ShouldBe(new[] { 1 });
        }

        [Fact]
        public void MixedCycle_ClassifiesEachBucket()
        {
            var current = new Dictionary<int, Process>
            {
                [1] = Stored(1),
                [2] = Stored(2, name: "old"),
                [3] = Stored(3),
            };

            var diff = ProcessDiffEngine.Compute(current,
                [Snap(2, name: "new"), Snap(3), Snap(4)]);

            diff.Added.Select(s => s.Pid).ShouldBe(new[] { 4 });
            diff.Removed.ShouldBe(new[] { 1 });
            diff.Updated.Select(s => s.Pid).ShouldBe(new[] { 2 });
        }
    }
}
