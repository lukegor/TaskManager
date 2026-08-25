# Engineering Guardrails Wave 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **Validation policy:** never launch the GUI during automated steps; interactive checks (starting the app, UAC prompts) require explicit human request first. See README "Development notes".

**Goal:** Enforce build quality mechanically (warnings-as-errors, architecture rules), stamp versions, dispose the DI container on exit, and add a GitHub Actions CI pipeline with coverage.

**Architecture:** Solution-wide MSBuild properties in `Directory.Build.props` govern every project; NetArchTest unit tests turn the README's dependency rule into a failing test; one workflow file runs restore → versioned build → unit tests + cobertura coverage on `windows-latest`.

**Tech Stack:** .NET 10 SDK (pinned by `global.json`), MSBuild/Central Package Management, NetArchTest.Rules, coverlet.msbuild, GitHub Actions.

## Global Constraints

- SDK is pinned by `global.json` (`10.0.303`, `rollForward: latestFeature`); target framework `net10.0-windows` comes from `Directory.Build.props` — never set per-csproj.
- All package versions live exclusively in `Directory.Packages.props` (CPM with `CentralPackageTransitivePinningEnabled=true`). Never put `Version=` on a `PackageReference`.
- Dependency rule (from README): only the WPF exe references WPF; Domain depends on the BCL plus NtApiDotNet and ClosedXML.
- New dependencies in this plan are test-only: `NetArchTest.Rules` and `coverlet.msbuild` (which replaces `coverlet.collector`). No production dependency changes.
- Solution file is `TaskManager.slnx`; default branch is `main`; dev platform is Windows (pwsh 7).
- Commit messages: lowercase conventional prefixes matching history (`feat:`, `fix:`, `build:`, `chore:`, `test:`, `ci:`).
- Every task ends green: `dotnet test tests/TaskManager.UnitTests` passes.

### Out of scope (deferred)

Wave 2 items from `docs/engineering-infrastructure-backlog.md` (async file logger L1, logging conventions L2, release packaging B5, CHANGELOG B6) get their own plan after this one lands. File-scoped namespace conversion is deliberately NOT included — it would churn ~80 files inside a guardrails wave; existing block-scoped namespaces stay. `.editorconfig` was explicitly descoped by the product owner; consequently `EnforceCodeStyleInBuild` and the CI format gate are also omitted (both depend on it).

---

### Task 1: Harden Directory.Build.props (warnings-as-errors, analyzers, determinism, version prefix)

**Files:**
- Modify: `Directory.Build.props` (entire file — it currently contains only TargetFramework/ImplicitUsings/Nullable)

**Interfaces:**
- Consumes: nothing.
- Produces (consumed by Task 4's CI):
  - `<VersionPrefix>0.1.0</VersionPrefix>` — CI overrides via `-p:Version=`
  - `<ContinuousIntegrationBuild>` auto-enabled when env var `CI` is set
  - Global `TreatWarningsAsErrors=true` + `AnalysisLevel latest-recommended`

- [ ] **Step 1: Replace `Directory.Build.props` contents**

```xml
<Project>

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>

    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-recommended</AnalysisLevel>

    <!-- Reproducible builds: CI sets CI=true; deterministic + normalized paths -->
    <Deterministic>true</Deterministic>
    <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>

    <!-- Local fallback version; CI injects -p:Version (tag or build number) -->
    <VersionPrefix>0.1.0</VersionPrefix>

    <!--
      Deliberate global waivers — each must carry a justification:
      - CA1848 (use LoggerMessage source-gen delegates): real perf win but requires
        rewriting every ILogger call site; deferred to its own batch. Structured
        message templates are already used everywhere, so we keep the benefit that matters.
      - CA1515 (make types internal): WPF requires public App/views; test assemblies
        anchor public types by design.
    -->
    <NoWarn>$(NoWarn);CA1848;CA1515</NoWarn>
  </PropertyGroup>

</Project>
```

- [ ] **Step 2: Build and clear the warning wave (policy-driven)**

Run:

```bash
dotnet build TaskManager.slnx
```

Every diagnostic now surfaces as an error. Fix each according to this policy:

| Diagnostic | Policy | Concrete action |
|---|---|---|
| Real bug / correctness (nullable, CA1062, CA1849 sync-in-async, …) | Fix the code | e.g. add null guard, use awaited API |
| CA1031 "catch general exception" at an **application boundary** | Suppress at site | The three funnels in `App.RegisterGlobalExceptionHandlers()`, `UiErrorHandler`, and the polling catch in `ProcessListCatalog.RunPollingLoopAsync`/`SafePollingRefreshAsync` intentionally swallow everything. Wrap in pragma: |

```csharp
#pragma warning disable CA1031 // top-level handler: must not crash the app; failure is logged
            catch (Exception ex)
#pragma warning restore CA1031 // top-level handler: must not crash the app; failure is logged
```

| Other style/design suggestions elevated by `latest-recommended` where fixing is mechanical | Fix the code | Follow the message; keep diffs minimal |
| A rule that fires >10 times with no cheap correct fix | Stop, do NOT bulk-suppress | Downgrade `AnalysisLevel` to `latest` in `Directory.Build.props`, record the decision in the commit body, continue |

Do not add anything else to global `NoWarn` beyond what Step 1 pre-declares.

- [ ] **Step 3: Run relevant validation**

```bash
dotnet build TaskManager.slnx -c Release && dotnet test tests/TaskManager.UnitTests
```

Both must pass with zero warnings emitted.

- [ ] **Step 4: Commit**

```bash
git add Directory.Build.props src tests
git commit -m "build: enforce warnings-as-errors, latest analyzers, deterministic builds"
```

(If Step 2 touched many source files, split into two commits: first `build: raise analyzer strictness` with props only if it builds clean — realistically the wave lands together; prefer one honest commit.)

---

### Task 2: Architecture enforcement tests (NetArchTest)

**Files:**
- Create: `tests/TaskManager.UnitTests/Architecture/LayeringTests.cs`
- Modify: `Directory.Packages.props` (add one `PackageVersion`)
- Modify: `tests/TaskManager.UnitTests/TaskManager.UnitTests.csproj` (add one `PackageReference`)

**Interfaces:**
- Consumes: existing project references (`UnitTests` → both `TaskManager` and `TaskManager.Domain`) and global usings `Xunit`/`Shouldly` declared in the csproj.
- Produces: two permanent regression tests guarding the README dependency rule. Nothing downstream consumes them programmatically.

- [ ] **Step 1: Register the package under the tests label in `Directory.Packages.props`**

Inside the `<ItemGroup Label="tests">`, add alphabetically before NSubstitute:

```xml
    <PackageVersion Include="NetArchTest.Rules" Version="1.3.2" />
```

Then in `TaskManager.UnitTests.csproj`, add next to the other PackageReferences:

```xml
    <PackageReference Include="NetArchTest.Rules" />
```

If restore fails because 1.3.2 does not exist in your feed, find the latest stable with `dotnet package search NetArchTest.Rules --exact-match` and pin that instead.

- [ ] **Step 2: Implement the tests**

Create `tests/TaskManager.UnitTests/Architecture/LayeringTests.cs`:

```csharp
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

    private static string FailingTypes(TestResult result) =>
        result.FailingTypeNames is { Length: > 0 } failing
            ? "Violations: " + string.Join(", ", failing)
            : "unknown failure";
}
```

Notes for the implementer:
- Do NOT add bare `"TaskManager"` to the forbidden list — NetArchTest matches namespace *prefixes*, so it would also match `TaskManager.Domain.*`.
- `Microsoft.Extensions.Logging.Abstractions` usage in Domain is allowed on purpose (README's rule restricts WPF, not infrastructure abstractions).

- [ ] **Step 3: Run relevant validation**

```bash
dotnet test tests/TaskManager.UnitTests --filter "FullyQualifiedName~Architecture"
```

Both tests must pass. If `Domain_does_not_depend_on_wpf_or_application_layers` fails, the violation is REAL — open the listed type, move the offending dependency behind a Domain abstraction or into the app project, and re-run until green. Do not weaken the rule to make it pass without fixing the underlying dependency direction.

Sanity-check the harness catches violations (temporary, do not commit):

```bash
# temporarily add 'using System.Windows;' anywhere in TaskManager.Domain, expect red, then revert
```

- [ ] **Step 4: Commit**

```bash
git add Directory.Packages.props tests/TaskManager.UnitTests
git commit -m "test: enforce domain/ui layering with architecture tests"
```

---

### Task 3: Graceful shutdown — dispose the service provider on exit

**Files:**
- Modify: `src/TaskManager/App.xaml.cs` (add `OnExit` override directly below `RegisterGlobalExceptionHandlers()`, around line 59)

**Interfaces:**
- Consumes: `_serviceProvider` field (`IServiceProvider`, initialized in `OnStartup`); `Microsoft.Extensions.Logging` (already imported).
- Produces: guaranteed disposal of all registered `IDisposable` services at exit. The Wave 2 async logger will rely on this hook to flush pending entries.

- [ ] **Step 1: Add the override**

Insert into `App` after `RegisterGlobalExceptionHandlers()`:

```csharp
        /// <summary>
        /// Logs intent while the log pipeline is still alive, then disposes the
        /// container so every registered IDisposable (logger providers included)
        /// can flush/release. Must stay last-write-wins over any shutdown work.
        /// </summary>
        protected override void OnExit(ExitEventArgs e)
        {
            _serviceProvider.GetRequiredService<ILogger<App>>()
                .LogInformation("Application exiting with code {ExitCode}", e.ExitCode);

            _serviceProvider.Dispose();

            base.OnExit(e);
        }
```

The log line intentionally precedes `Dispose()` — after disposal the logger is gone.

- [ ] **Step 2: Run relevant validation (manual smoke — UI lifecycle, no unit test warranted)**

```bash
dotnet build TaskManager.slnx && dotnet run --project src/TaskManager
```

Close the window normally, then:

```powershell
Get-Content "$env:LOCALAPPDATA\TaskManager\logs\tm-$(Get-Date -Format yyyyMMdd).log" -Tail 3
Get-Process TaskManager -ErrorAction SilentlyContinue   # expected: empty
```

The log tail must contain `Application exiting with code 0` and no process may survive.

Then confirm the suite is still green:

```bash
dotnet test tests/TaskManager.UnitTests
```

- [ ] **Step 3: Commit**

```bash
git add src/TaskManager/App.xaml.cs
git commit -m "feat: dispose service provider and log exit on application shutdown"
```

---

### Task 4: GitHub Actions CI — versioned build, unit tests with coverage

**Files:**
- Create: `.github/workflows/ci.yml`
- Modify: `Directory.Packages.props` (swap `coverlet.collector` → `coverlet.msbuild`)
- Modify: `tests/TaskManager.UnitTests/TaskManager.UnitTests.csproj` (same swap)
- Modify: `README.md` (CI badge)

**Interfaces:**
- Consumes: `.editorconfig` (Task 1) for the format gate; `TreatWarningsAsErrors`/`Deterministic`/`ContinuousIntegrationBuild`/`VersionPrefix` (Task 2) for the build; `main` as default branch.
- Produces: green/red status badge on `main`; `coverage` artifact (`artifacts/coverage/*.cobertura.xml`) on every run. Wave 2's release workflow will reuse the version-computation step pattern.

- [ ] **Step 1: Swap coverlet.collector for coverlet.msbuild (runner-independent collection)**

Rationale: `coverlet.collector` integrates with VSTest data collectors; this repo runs xunit.v3 on Microsoft.Testing.Platform (declared in `global.json`), where collector behavior differs between SDK versions. `coverlet.msbuild` instruments at the MSBuild level and works identically under either runner — one less moving part in CI.

In `Directory.Packages.props` (tests label): remove the `coverlet.collector` line and add:

```xml
    <PackageVersion Include="coverlet.msbuild" Version="10.0.1" />
```

Keep alphabetical order within the label. If restore fails because 10.0.1 does not exist, pin the latest stable found via `dotnet package search coverlet.msbuild --exact-match`.

In `TaskManager.UnitTests.csproj`: replace the `coverlet.collector` PackageReference with:

```xml
    <PackageReference Include="coverlet.msbuild">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
```

- [ ] **Step 2: Verify coverage collection locally**

```bash
dotnet test tests/TaskManager.UnitTests -p:CollectCoverage=true -p:CoverletOutput=./artifacts/coverage/ -p:CoverletOutputFormat=cobertura
Test-Path ./artifacts/coverage/coverage.cobertura.xml   # expected: True
```

(`artifacts/` and `coverage*.xml` are already gitignored.)

- [ ] **Step 3: Create `.github/workflows/ci.yml`**

```yaml
name: CI

on:
  push:
    branches: [ main ]
  pull_request:
  workflow_dispatch:

jobs:
  build-and-test:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v5

      # Installs the exact SDK requested by global.json (10.0.303, rollForward latestFeature)
      - name: Setup .NET SDK
        uses: actions/setup-dotnet@v4

      - name: Compute version
        id: ver
        shell: pwsh
        run: |
          if ($env:GITHUB_REF -match '^refs/tags/v(\d+\.\d+\.\d+)$') {
            $version = $Matches[1]
          } else {
            $version = "0.${{ github.run_number }}.0"
          }
          "version=$version" | Out-File -FilePath $env:GITHUB_OUTPUT -Append

      - name: Restore
        run: dotnet restore TaskManager.slnx

      # CI=true makes ContinuousIntegrationBuild deterministic (set in Directory.Build.props);
      # TreatWarningsAsErrors turns any warning into a failed run.
      - name: Build
        run: >
          dotnet build TaskManager.slnx -c Release --no-restore --nologo
          -p:Version=${{ steps.ver.outputs.version }}
          -p:SourceRevisionId=${{ github.sha }}

      - name: Unit tests with coverage
        run: >
          dotnet test tests/TaskManager.UnitTests -c Release --no-build --nologo
          -p:CollectCoverage=true
          -p:CoverletOutput=${{ github.workspace }}/artifacts/coverage/
          -p:CoverletOutputFormat=cobertura

      - name: Upload coverage artifact
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: coverage
          path: artifacts/coverage/*.cobertura.xml
          if-no-files-found: error
```

Deliberate choices (keep if asked):
- Integration suite excluded: it mutates live system state; scheduled/manual runs are a Wave 2+ concern.
- Version scheme: tag `v1.2.3` → `1.2.3`, otherwise `0.<run_number>.0` (falls back onto `VersionPrefix` semantics).
- `SourceRevisionId` embeds the commit hash into `InformationalVersion`, which a future About dialog reads.

- [ ] **Step 4: Validate the workflow locally as far as possible**

Mirror each step on this machine (SDK is already pinned):

```bash
dotnet restore TaskManager.slnx
dotnet build TaskManager.slnx -c Release --no-restore --nologo -p:Version=0.999.0
dotnet test tests/TaskManager.UnitTests -c Release --no-build --nologo -p:CollectCoverage=true -p:CoverletOutput=./artifacts/coverage/ -p:CoverletOutputFormat=cobertura
```

All three must pass. YAML syntax sanity check:

```powershell
pwsh -NoProfile -Command "ConvertFrom-Yaml (Get-Content .github/workflows/ci.yml -Raw)"
```

(Skip if the yaml module is unavailable — the first push validates for real.)

Also verify the stamped version landed:

```powershell
(Get-Item bin/Release/TaskManager.dll).VersionInfo.InformationalVersion   # expect 0.999.0 + commit hash
```

- [ ] **Step 5: Add the README badge**

Get the slug:

```bash
git remote get-url origin
```

Insert directly under `# Task Manager` in `README.md` (substitute OWNER/REPO from the command output):

```markdown
[![CI](https://github.com/OWNER/REPO/actions/workflows/ci.yml/badge.svg)](https://github.com/OWNER/REPO/actions/workflows/ci.yml)
```

- [ ] **Step 6: Commit**

```bash
git add .github/workflows/ci.yml Directory.Packages.props tests/TaskManager.UnitTests/TaskManager.UnitTests.csproj README.md
git commit -m "ci: versioned release-style build with coverage artifact"
```

Push to a branch and open a PR to watch the pipeline execute end-to-end; fix any runner-specific issue (most likely: action version drift — bump the pinned major tag) in a follow-up commit on the same branch.

---

## Self-Review Record

- **Spec coverage (backlog W1):** B1 → Task 1; B3 → Task 1 (VersionPrefix) + Task 4 (tag/run-number injection, SourceRevisionId); B4 → Task 4; T1 → Task 2; H1 → Task 3. B2 (.editorconfig) and T3 (format gate) were descoped by the product owner along with their downstream hooks (`EnforceCodeStyleInBuild`, CI format step). ✔
- **Placeholder scan:** compiler-driven warning fixes (Task 1) are policy-tabled with concrete suppression examples rather than free-form instructions; package-version contingencies name the exact search command; badge OWNER/REPO is derived by an explicit `git remote get-url origin` step. ✔
- **Type consistency:** `steps.ver.outputs.version` produced/consumed within Task 4 only; `ContinuousIntegrationBuild` condition `'$(CI)' == 'true'` matches GitHub's automatic env var; NetArchTest anchors (`RefreshFrequencyType`, `App`) exist and are public; `FailingTypes(TestResult)` defined once and used twice. ✔
