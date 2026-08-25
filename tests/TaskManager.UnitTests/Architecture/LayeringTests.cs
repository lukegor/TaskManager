using System.Reflection;
using NetArchTest.Rules;

namespace TaskManager.UnitTests.Architecture;

/// <summary>
/// Executable form of the README dependency rule:
/// "only the WPF exe references WPF; Domain depends on the BCL plus NtApiDotNet and ClosedXML".
/// </summary>
public class LayeringTests
{
    private static readonly Assembly DomainAssembly = typeof(TaskManager.Domain.Primitives.RefreshFrequencyType).Assembly;
    private static readonly Assembly AppAssembly = typeof(TaskManager.App).Assembly;

    [Fact]
    public void Domain_does_not_depend_on_wpf_or_application_layers()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "System.Windows",          // WPF belongs to the exe only
                "TaskManager.Abstractions",
                "TaskManager.Infrastructure",
                "TaskManager.Presentation",
                "TaskManager.Services",
                "TaskManager.UI",
                "TaskManager.ViewModels")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailingTypes(result));
    }

    [Fact]
    public void Wpf_app_does_not_use_native_or_export_libraries_directly()
    {
        // NtApiDotNet and ClosedXML are Domain implementation details,
        // reachable only through Domain abstractions/services.
        var result = Types.InAssembly(AppAssembly)
            .ShouldNot()
            .HaveDependencyOnAny("NtApiDotNet", "ClosedXML")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailingTypes(result));
    }

    private static string FailingTypes(NetArchTest.Rules.TestResult result) =>
        result.FailingTypeNames is { Count: > 0 } failing
            ? "Violations: " + string.Join(", ", failing)
            : "unknown failure";
}
