using CommunityToolkit.Mvvm.ComponentModel;
using System.Reflection;
using System.Windows;

namespace TaskManager.UnitTests.ViewModels
{
    public class ViewViewmodelTests
    {
        private readonly Assembly _viewAssembly = typeof(TaskManager.UI.Views.MainWindow).Assembly;
        private readonly Assembly _vmAssembly = typeof(TaskManager.ViewModels.MainWindowViewModel).Assembly;

        private Type[] GetAllWindows() =>
            _viewAssembly.GetTypes()
                         .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(Window)) && t.Namespace == "TaskManager.UI.Views")
                         .ToArray();

        private Type[] GetAllViewModels() =>
            _vmAssembly.GetTypes()
                       .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(ObservableObject)) && t.Namespace == "TaskManager.ViewModels")
                       .ToArray();

        [Fact]
        public void EveryWindowHasCorrespondingViewModel()
        {
            var windows = GetAllWindows();
            var viewModels = GetAllViewModels();

            foreach (var window in windows)
            {
                var expectedVmName = window.Name + "ViewModel";
                viewModels.ShouldContain(vm => vm.Name == expectedVmName);
            }
        }

        [Fact]
        public void EveryViewModelHasCorrespondingWindow()
        {
            var windows = GetAllWindows();
            var viewModels = GetAllViewModels();

            foreach (var vm in viewModels)
            {
                if (!vm.Name.EndsWith("ViewModel")) continue;

                var expectedWindowName = vm.Name.Replace("ViewModel", "");
                windows.ShouldContain(w => w.Name == expectedWindowName);
            }
        }
    }
}
