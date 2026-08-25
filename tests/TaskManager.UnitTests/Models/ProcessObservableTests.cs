using TaskManager.Domain.Models;
using TaskManager.Domain.Primitives;

namespace TaskManager.UnitTests.Models
{
    public class ProcessObservableTests
    {
        [Fact]
        public void ChangedValue_RaisesPropertyChangedForThatProperty()
        {
            var changed = new List<string>();
            var process = new Process { Name = "a", Path = string.Empty };
            process.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

            process.Name = "b";

            changed.ShouldHaveSingleItem().ShouldBe(nameof(Process.Name));
            process.Name.ShouldBe("b");
        }

        [Theory]
        [InlineData(10, 10, false)]
        [InlineData(10, 6, true)]
        public void Priority_RaisesOnlyOnActualChange(int initial, int updated, bool expectEvent)
        {
            var changed = new List<string>();
            var process = new Process { Name = "a", Path = string.Empty, Priority = initial };
            process.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

            process.Priority = updated;

            changed.Count.ShouldBe(expectEvent ? 1 : 0);
        }

        [Fact]
        public void ArchitectureChange_RaisesBothDataAndDisplayNotifications()
        {
            var changed = new List<string>();
            var process = new Process { Name = "a", Path = string.Empty };
            process.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

            process.ArchitectureType = ArchitectureType.Bit64;

            changed.ShouldBe(new[] { nameof(Process.ArchitectureType), nameof(Process.ArchitectureTypeDisplay) });
        }

        [Fact]
        public void DelimitedString_UnchangedByRefactor()
        {
            var process = new Process { Name = "app", Pid = 42, Path = @"C:\app.exe", Priority = 8, ThreadCount = 3, Ppid = 4 };

            process.ToDelimitedString(',').ShouldBe("app,42,C:\\app.exe,8,3,4");
        }
    }
}
