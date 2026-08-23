using TaskManager.Domain.Models;

namespace TaskManager.Tests.Models
{
    public class ProcessItemTests
    {
        [Fact]
        public void IsSelected_Change_RaisesPropertyChanged()
        {
            var item = new ProcessItem(new Process { Name = "a", Pid = 1 });
            var raised = new List<string?>();
            item.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            item.IsSelected = true;

            Assert.True(item.IsSelected);
            Assert.Contains(nameof(ProcessItem.IsSelected), raised);
        }

        [Fact]
        public void IsSelected_SameValue_DoesNotRaisePropertyChanged()
        {
            var item = new ProcessItem(new Process { Name = "a", Pid = 1 }) { IsSelected = true };
            var raised = 0;
            item.PropertyChanged += (_, _) => raised++;

            item.IsSelected = true;

            Assert.Equal(0, raised);
        }
    }
}
