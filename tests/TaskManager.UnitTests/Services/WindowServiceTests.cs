using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Diagnostics;
using System.IO;
using System.Windows;
using TaskManager.Abstractions;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Primitives;
using TaskManager.Domain.Services.DataExport;
using TaskManager.Services;
using TaskManager.Services.ErrorHandling;
using TaskManager.ViewModels;
using DataTypeEnum = TaskManager.Domain.Primitives.DataType;
using Process = TaskManager.Domain.Models.Process;

namespace TaskManager.UnitTests.Services
{
    /// <summary>
    /// Dialog wiring contract: factories produce correctly paired window+viewmodel, and the
    /// close relay translates a ViewModel RequestClose into Window.Closed. Windows are never
    /// shown (WPF raises Closed even for a never-shown window being closed).
    /// </summary>
    public class WindowServiceTests
    {
        private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
        private readonly IErrorHandler _errorHandler = Substitute.For<IErrorHandler>();
        private readonly IMessageService _messages = Substitute.For<IMessageService>();
        private readonly IProcessListCatalog _catalog = Substitute.For<IProcessListCatalog>();
        private readonly IFolderPicker _folderPicker = Substitute.For<IFolderPicker>();

        public WindowServiceTests()
        {
            _settings.Current.Returns(AppSettings.Defaults);
        }

        private WindowService CreateService() => new(
            _settings,
            _errorHandler,
            _messages,
            _catalog,
            dataType => new TxtExporter(NewSettings(), NullLogger<TxtExporter>.Instance),
            _folderPicker);

        private static ISettingsService NewSettings()
        {
            var settings = Substitute.For<ISettingsService>();
            settings.Current.Returns(AppSettings.Defaults);
            return settings;
        }

        [WpfFact]
        public void CreateSettingsDialog_PairsWindowWithSettingsViewModel()
        {
            var (window, viewModel) = CreateService().CreateSettingsDialog();

            window.ShouldNotBeNull();
            window.DataContext.ShouldBe(viewModel);
            viewModel.ShouldNotBeNull();
        }

        [WpfFact]
        public async Task CreateExportDialog_SeedsWithMaterializedProcesses()
        {
            var directory = Path.Combine(Path.GetTempPath(), $"tm-winsvc-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            try
            {
                var processes = new List<Process>
                {
                    new() { Name = "p1", Pid = 1, Path = string.Empty },
                };
                var (window, viewModel) = CreateService().CreateExportDialog(processes);

                window.DataContext.ShouldBe(viewModel);
                viewModel.DirPath = directory;

                processes.Clear(); // caller-side mutation must not leak into the dialog

                (await viewModel.TryExportAsync(DataTypeEnum.Txt)).ShouldBeTrue();

                var written = File.ReadAllLines(Directory.GetFiles(directory, "record-*").Single());
                written.Count(line => line.Contains("p1")).ShouldBe(1); // snapshot survived the Clear()
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [WpfFact]
        public async Task CreatePriorityDialog_RequestClose_RaisesWindowClosed_AndMarksConfirmed()
        {
            var (window, viewModel) = CreateService().CreatePriorityDialog([1, 2]);
            var closed = false;
            window.Closed += (_, _) => closed = true;
            _catalog.SetPriorityAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<ProcessPriorityClass>())
                .Returns(ProcessOpSummary.Empty);
            viewModel.Priority = ProcessPriorityClass.Normal;

            await viewModel.OnConfirmCommand.ExecuteAsync(null);

            closed.ShouldBeTrue();              // relay translated RequestClose -> Close
            viewModel.Confirmed.ShouldBeTrue(); // ShowSetPriority will surface this
            window.DataContext.ShouldBe(viewModel);
        }
    }
}
