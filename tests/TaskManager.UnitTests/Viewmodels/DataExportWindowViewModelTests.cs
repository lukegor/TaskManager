using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.IO;
using System.Windows;
using TaskManager.Abstractions;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Services.DataExport;
using TaskManager.Services.ErrorHandling;
using TaskManager.ViewModels;
using TaskManager.Domain.Primitives;
using DataTypeEnum = TaskManager.Domain.Primitives.DataType;

namespace TaskManager.UnitTests.ViewModels
{
    public class DataExportWindowViewModelTests : IDisposable
    {
        private readonly IMessageService _messageService = Substitute.For<IMessageService>();
        private readonly Dictionary<DataTypeEnum, BaseDataExporter> _registeredExporters = new();
        private Exception? _selectorCrash;
        private readonly IFolderPicker _folderPicker = Substitute.For<IFolderPicker>();
        private readonly string _tempDirectory =
            Path.Combine(Path.GetTempPath(), $"tm-exportvm-tests-{Guid.NewGuid():N}");
        private readonly DataExportWindowViewModel _viewModel;

        public DataExportWindowViewModelTests()
        {
            Directory.CreateDirectory(_tempDirectory);
            _viewModel = CreateViewModel([]);
            _viewModel.DirPath = _tempDirectory;
        }

        private DataExportWindowViewModel CreateViewModel(IReadOnlyList<Process> processes)
        {
            return new DataExportWindowViewModel(
                _messageService,
                new UiErrorHandler(NullLogger<UiErrorHandler>.Instance, Substitute.For<IMessageService>()),
                dataType =>
                {
                    if (_selectorCrash is not null)
                    {
                        throw _selectorCrash;
                    }

                    return _registeredExporters[dataType];
                },
                _folderPicker,
                processes);
        }

        [Fact]
        public void TryExport_Success_WritesFileAndReturnsTrue()
        {
            _registeredExporters[DataTypeEnum.Txt] =
                new TxtExporter(NewSettings(), NullLogger<BaseDataExporter>.Instance);

            var success = _viewModel.TryExport(DataTypeEnum.Txt);

            success.ShouldBeTrue();
            Directory.GetFiles(_tempDirectory, "record-*").ShouldNotBeEmpty();
        }

        [Fact]
        public void TryExport_Failure_ShowsSingleMessageAndReturnsFalse()
        {
            _registeredExporters[DataTypeEnum.Txt] = new ThrowingExporter(NewSettings());

            var success = _viewModel.TryExport(DataTypeEnum.Txt);

            success.ShouldBeFalse();
            _messageService.Received(1).ShowMessage(
                Arg.Any<string>(), Arg.Any<string>(), MessageBoxButton.OK, MessageBoxImage.Error);
        }

        [Fact]
        public void TryExport_UnexpectedFactoryCrash_IsGuardedAndReturnsFalse()
        {
            _selectorCrash = new InvalidOperationException("factory exploded");

            _viewModel.TryExport(DataTypeEnum.Txt).ShouldBeFalse();
        }

        [Fact]
        public void Constructor_HoldsMaterializedSnapshot_IgnoringLaterCallerMutations()
        {
            var processes = new List<Process>
            {
                new() { Name = "p1", Pid = 1, Path = string.Empty },
                new() { Name = "p2", Pid = 2, Path = string.Empty },
            };
            _registeredExporters[DataTypeEnum.Txt] =
                new TxtExporter(NewSettings(), NullLogger<BaseDataExporter>.Instance);
            var vm = CreateViewModel(processes);
            vm.DirPath = _tempDirectory;

            processes.Clear(); // caller-side mutation after handoff must not leak into the dialog

            vm.TryExport(DataTypeEnum.Txt).ShouldBeTrue();

            var written = File.ReadAllLines(Directory.GetFiles(_tempDirectory, "record-*").Single());
            written.Count(line => line.Contains("p1")).ShouldBe(1);
            written.Count(line => line.Contains("p2")).ShouldBe(1);
        }

        [Fact]
        public void SelectFolder_PickedResult_AssignedToDirPath()
        {
            var picked = Path.Combine(_tempDirectory, "picked");
            Directory.CreateDirectory(picked);
            _folderPicker.PickFolder().Returns(picked);

            _viewModel.SelectFolderCommand.Execute(null);

            _viewModel.DirPath.ShouldBe(picked);
        }

        [Fact]
        public void SelectFolder_Cancelled_ClearsDirPath_PreservingPreviousUx()
        {
            _folderPicker.PickFolder().Returns((string?)null);

            _viewModel.SelectFolderCommand.Execute(null);

            _viewModel.DirPath.ShouldBeEmpty();
        }

        [Fact]
        public void OnConfirm_MissingOptions_ShowsError_DoesNotClose()
        {
            var closed = false;
            _viewModel.RequestClose += (_, _) => closed = true;

            _viewModel.OnConfirmClick.Execute(null);

            _messageService.Received(1).ShowMessage(
                Arg.Any<string>(), Arg.Any<string>(), MessageBoxButton.OK, MessageBoxImage.Error);
            closed.ShouldBeFalse();
            _viewModel.Confirmed.ShouldBeFalse();
        }

        private static ISettingsService NewSettings()
        {
            var settings = Substitute.For<ISettingsService>();
            settings.Current.Returns(new AppSettings
            {
                Language = "English",
                ProcessesRefreshFrequency = RefreshFrequencyType.Low,
                DateTimeFormat = "yyyyMMdd_HHmmss"
            });
            return settings;
        }

        private sealed class ThrowingExporter : TxtExporter
        {
            public ThrowingExporter(ISettingsService settings)
                : base(settings, NullLogger<BaseDataExporter>.Instance)
            {
            }

            protected override void PerformExport<T>(string fullFileName, IEnumerable<string> strings)
                => throw new IOException("target locked");
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
    }
}
