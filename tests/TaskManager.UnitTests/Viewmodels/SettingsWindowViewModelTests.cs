using NSubstitute;
using TaskManager.Domain.Abstractions;
using TaskManager.Domain.Models;
using TaskManager.Services.ErrorHandling;
using TaskManager.Domain.Primitives;
using TaskManager.ViewModels;

namespace TaskManager.UnitTests.ViewModels
{
    /// <summary>
    /// Dialog contract: edits stay local until Save commits a full snapshot via Update,
    /// and Restore Defaults flows through the same pipeline so the change propagates.
    /// </summary>
    public class SettingsWindowViewModelTests
    {
        private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
        private readonly IErrorHandler _errorHandler = Substitute.For<IErrorHandler>();

        private SettingsWindowViewModel CreateViewModel() => new(_settings, _errorHandler);

        [Fact]
        public void Constructor_InitializesEditableStateFromCurrentSnapshot()
        {
            var current = AppSettings.Defaults with
            {
                Language = "polski",
                ProcessesRefreshFrequency = RefreshFrequencyType.Paused,
                DateTimeFormat = "MM_dd_yyyy--HH_mm_ss"
            };
            _settings.Current.Returns(current);

            var vm = CreateViewModel();

            vm.Language.ShouldBe("polski");
            vm.ProcessesRefreshFrequency.ShouldBe(RefreshFrequencyType.Paused);
            vm.DateTimeFormat.ShouldBe("MM_dd_yyyy--HH_mm_ss");
            vm.DateTimeFormats.ShouldBe(AppSettings.AllowedDateTimeFormats);
        }

        [Fact]
        public void SaveSettings_CommitsEditedValuesAsOneSnapshot()
        {
            _settings.Current.Returns(AppSettings.Defaults);
            var vm = CreateViewModel();
            vm.Language = "polski";
            vm.ProcessesRefreshFrequency = RefreshFrequencyType.High;

            vm.SaveSettings();

            Received.InOrder(() =>
            {
                _settings.Update(Arg.Is<AppSettings>(s =>
                    s.Language == "polski" &&
                    s.ProcessesRefreshFrequency == RefreshFrequencyType.High &&
                    s.DateTimeFormat == AppSettings.Defaults.DateTimeFormat));
            });
        }

        [Fact]
        public void RestoreDefaults_ResetsDialogAndCommitsDefaults()
        {
            var current = AppSettings.Defaults with { Language = "polski" };
            _settings.Current.Returns(current);
            var vm = CreateViewModel();

            vm.RestoreDefaults();

            vm.Language.ShouldBe(AppSettings.Defaults.Language);
            vm.ProcessesRefreshFrequency.ShouldBe(AppSettings.Defaults.ProcessesRefreshFrequency);
            vm.DateTimeFormat.ShouldBe(AppSettings.Defaults.DateTimeFormat);
            _settings.Received(1).Update(AppSettings.Defaults);
        }

        [Fact]
        public void SaveSettings_WhenServiceRejects_RoutesToErrorHandler()
        {
            _settings.Current.Returns(AppSettings.Defaults);
            var vm = CreateViewModel();
            vm.Language = "klingon"; // whitelist violation surfaces only at the validation gate
            _settings.When(s => s.Update(Arg.Any<AppSettings>()))
                .Throw(new SettingsValidationException("unknown language 'klingon'"));

            vm.SaveSettings();

            _errorHandler.Received(1).Handle(
                Arg.Any<SettingsValidationException>(), Arg.Any<string>());
        }
    }
}
