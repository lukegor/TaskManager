using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.IO;
using System.Windows;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Services.Data_Export;
using TaskManager.Services.ErrorHandling;
using TaskManager.Services.Factories;
using TaskManager.ViewModels;
using TaskManager.Domain.Primitives;
using DataTypeEnum = TaskManager.Domain.Primitives.DataType;
using ExportationTypeEnum = TaskManager.Domain.Primitives.ExportationType;

namespace TaskManager.Tests
{
    public class DataExportWindowViewModelTests : IDisposable
    {
        private readonly IServiceProvider _serviceProvider = Substitute.For<IServiceProvider>();
        private readonly IMessageService _messageService = Substitute.For<IMessageService>();
        private readonly DataExporterFactory _exporterFactory =
            Substitute.For<DataExporterFactory>(Substitute.For<IServiceProvider>());
        private readonly string _tempDirectory =
            Path.Combine(Path.GetTempPath(), $"tm-exportvm-tests-{Guid.NewGuid():N}");
        private readonly DataExportWindowViewModel _viewModel;

        public DataExportWindowViewModelTests()
        {
            Directory.CreateDirectory(_tempDirectory);
            // NSubstitute cannot intercept the GetRequiredService extension method,
            // so we configure the underlying GetService instance method it delegates to.
            _serviceProvider.GetService(typeof(DataExporterFactory)).Returns(_exporterFactory);
            _viewModel = new DataExportWindowViewModel(
                _serviceProvider,
                Substitute.For<ISettingsService>(),
                _messageService,
                new UiErrorHandler(NullLogger<UiErrorHandler>.Instance, Substitute.For<IMessageService>()),
                []);
            _viewModel.DirPath = _tempDirectory;
        }

        [Fact]
        public void TryExport_Success_WritesFileAndReturnsTrue()
        {
            var exporter = new TxtExporter(NewSettings(), NullLogger<BaseDataExporter>.Instance);
            _exporterFactory
                .CreateDataExporter(DataTypeEnum.Txt)
                .Returns(exporter);

            var success = _viewModel.TryExport(ExportationTypeEnum.Processes, DataTypeEnum.Txt);

            success.ShouldBeTrue();
            Directory.GetFiles(_tempDirectory, "record-*").ShouldNotBeEmpty();
        }

        [Fact]
        public void TryExport_Failure_ShowsSingleMessageAndReturnsFalse()
        {
            var exporter = new ThrowingExporter(NewSettings());
            _exporterFactory
                .CreateDataExporter(DataTypeEnum.Txt)
                .Returns(exporter);

            var success = _viewModel.TryExport(ExportationTypeEnum.Processes, DataTypeEnum.Txt);

            success.ShouldBeFalse();
            _messageService.Received(1).ShowMessage(
                Arg.Any<string>(), Arg.Any<string>(), MessageBoxButton.OK, MessageBoxImage.Error);
        }

        [Fact]
        public void TryExport_UnexpectedFactoryCrash_IsGuardedAndDoesNotThrow()
        {
            _exporterFactory
                .When(f => f.CreateDataExporter(DataTypeEnum.Txt))
                .Do(_ => throw new InvalidOperationException("factory exploded"));

            var success = _viewModel.TryExport(ExportationTypeEnum.Processes, DataTypeEnum.Txt);

            success.ShouldBeFalse();
        }

        [Fact]
        public void Constructor_HoldsMaterializedSnapshot_IgnoringLaterCallerMutations()
        {
            var processes = new List<Process>
            {
                new() { Name = "p1", Pid = 1, Path = string.Empty },
                new() { Name = "p2", Pid = 2, Path = string.Empty },
            };
            var exporter = new TxtExporter(NewSettings(), NullLogger<BaseDataExporter>.Instance);
            _exporterFactory.CreateDataExporter(DataTypeEnum.Txt).Returns(exporter);
            var vm = new DataExportWindowViewModel(
                _serviceProvider,
                Substitute.For<ISettingsService>(),
                _messageService,
                new UiErrorHandler(NullLogger<UiErrorHandler>.Instance, Substitute.For<IMessageService>()),
                processes);
            vm.DirPath = _tempDirectory;
            vm.Exportation = ExportationTypeEnum.Processes;
            vm.DataType = DataTypeEnum.Txt;

            processes.Clear(); // caller-side mutation after handoff must not leak into the dialog

            vm.TryExport(ExportationTypeEnum.Processes, DataTypeEnum.Txt).ShouldBeTrue();

            var written = File.ReadAllLines(Directory.GetFiles(_tempDirectory, "record-*").Single());
            written.Count(line => line.Contains("p1")).ShouldBe(1);
            written.Count(line => line.Contains("p2")).ShouldBe(1);
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

