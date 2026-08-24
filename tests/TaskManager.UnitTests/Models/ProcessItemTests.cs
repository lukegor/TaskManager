using TaskManager.Domain.Models;

namespace TaskManager.UnitTests.Models
{
    public class ProcessItemTests
    {
        [Fact]
        public void IsSelected_Change_RaisesPropertyChanged()
        {
            var item = new ProcessItem(new Process { Name = "a", Pid = 1, Path = string.Empty });
            var raised = new List<string?>();
            item.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            item.IsSelected = true;

            item.IsSelected.ShouldBeTrue();
            raised.ShouldContain(nameof(ProcessItem.IsSelected));
        }

        [Fact]
        public void IsSelected_SameValue_DoesNotRaisePropertyChanged()
        {
            var item = new ProcessItem(new Process { Name = "a", Pid = 1, Path = string.Empty }) { IsSelected = true };
            var raised = 0;
            item.PropertyChanged += (_, _) => raised++;

            item.IsSelected = true;

            raised.ShouldBe(0);
        }
    }
}
