using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.IO;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Domain.Services.DataExport;
using TaskManager.Domain.Primitives;

namespace TaskManager.UnitTests
{
    public class BaseDataExporterTests : IDisposable
    {
        private const string FileNamePrefix = @"\record-";

        private readonly string _tempDirectory =
            Path.Combine(Path.GetTempPath(), $"tm-export-tests-{Guid.NewGuid():N}");

        private static ISettingsService CreateSettings()
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

        [Fact]
        public void Export_Success_ReturnsFilePath()
        {
            Directory.CreateDirectory(_tempDirectory);
            var exporter = new TxtExporter(CreateSettings(), NullLogger<TxtExporter>.Instance);

            var result = exporter.Export<DummyRecord>(_tempDirectory, []);

            result.IsSuccess.ShouldBeTrue();
            result.FilePath.ShouldNotBeNull();
            result.FilePath.ShouldStartWith(_tempDirectory);
            result.FilePath.ShouldContain(FileNamePrefix);
            result.FilePath.ShouldEndWith(".txt");
        }

        [Theory]
        [InlineData(typeof(IOException), ExportFailureReason.IoError)]
        [InlineData(typeof(UnauthorizedAccessException), ExportFailureReason.AccessDenied)]
        [InlineData(typeof(ArgumentException), ExportFailureReason.InvalidPath)]
        [InlineData(typeof(NotSupportedException), ExportFailureReason.InvalidPath)]
        public void Export_MapsExpectedIoFailuresToOutcomeData(Type exceptionType, ExportFailureReason expectedReason)
        {
            var failure = (Exception)Activator.CreateInstance(exceptionType, "simulated io failure")!;
            var exporter = new ThrowingExporter(CreateSettings(), failure);

            var result = exporter.Export<DummyRecord>("C:\\dir", []);

            result.IsSuccess.ShouldBeFalse();
            result.FailureReason.ShouldBe(expectedReason);
        }

        [Fact]
        public void Export_LetsUnexpectedExceptionsPropagate()
        {
            var exporter = new ThrowingExporter(CreateSettings(), new InvalidOperationException("a bug"));

            Should.Throw<InvalidOperationException>(
                () => exporter.Export<DummyRecord>("C:\\dir", []));
        }

        /// <summary>Satisfies the IExportable generic constraint; never instantiated (empty record lists).</summary>
        private sealed class DummyRecord : IExportable
        {
            public string ToDelimitedString(char separator) => string.Empty;
        }

        private sealed class ThrowingExporter : BaseDataExporter
        {
            private readonly Exception _failureToThrow;

            public ThrowingExporter(ISettingsService settings, Exception failureToThrow)
                : base(settings, NullLogger<TxtExporter>.Instance)
            {
                _failureToThrow = failureToThrow;
            }

            protected override string Extension => "txt";

            protected override void PerformExport<T>(string fullFileName, IEnumerable<string> strings)
                => throw _failureToThrow;
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

