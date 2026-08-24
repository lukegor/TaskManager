# Project Structure Consolidation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Collapse the 5-project solution into `src/TaskManager` (WPF exe) + `src/TaskManager.Domain` + two test projects, dissolving `TaskManager.Shared` and `TaskManager.Utility` into their real owners.

**Architecture:** Mechanical code-motion refactor. Domain becomes UI-free (BCL + NtApiDotNet + ClosedXML only); localization and WPF helpers move into the exe; integration tests get their own project. Spec: `docs/superpowers/specs/2026-08-24-project-structure-consolidation-design.md`.

**Tech Stack:** .NET 10 (net10.0-windows), WPF, CPM (`Directory.Packages.props`), xunit.v3, PowerShell 7 (pwsh).

## Global Constraints

- Target framework: `net10.0-windows` (inherited from root `Directory.Build.props`; never set per-project).
- Package versions come ONLY from central `Directory.Packages.props` — never add `Version=` to a `PackageReference`.
- WPF (`UseWPF`) allowed ONLY in the `TaskManager` exe project.
- Every file's namespace must match its folder path under the project root namespace.
- No binaries (`dll/exe/pdb`) ever staged; `obj/` and `bin/` are gitignored.
- Use `git mv` for moves so history follows the files.
- Commit style: conventional commits (`refactor:`, `build:`, `docs:`), one commit per task.
- All shell snippets are pwsh 7, run from the repo root unless stated otherwise.
- Build check: `dotnet build TaskManager.slnx`; unit tests: `dotnet test tests/TaskManager.UnitTests` (must be green before every commit).

---

### Task 1: Solution restructure to src/tests layout + csproj hygiene + SDK pin

**Files:**
- Modify: `TaskManager.slnx`
- Create: `src/`, `tests/` (via `git mv`)
- Rename: `TaskManager.Tests/` → `tests/TaskManager.UnitTests/`, csproj renamed accordingly
- Modify: `src/TaskManager/TaskManager.csproj` (dedupe OutputPath)
- Modify: `global.json` (SDK pin)

**Interfaces:**
- Produces: canonical layout used by every later task — projects live at `src/TaskManager`, `src/TaskManager.Domain`, `src/TaskManager.Shared`, `src/TaskManager.Utility`, `tests/TaskManager.UnitTests`. Solution builds and all unit tests pass before any code motion starts.

- [ ] **Step 1: Move projects**

```bash
mkdir src, tests
git mv TaskManager src/TaskManager
git mv TaskManager.Domain src/TaskManager.Domain
git mv TaskManager.Shared src/TaskManager.Shared
git mv TaskManager.Utility src/TaskManager.Utility
git mv TaskManager.Tests tests/TaskManager.UnitTests
git mv tests/TaskManager.UnitTests/TaskManager.Tests.csproj tests/TaskManager.UnitTests/TaskManager.UnitTests.csproj
```

- [ ] **Step 2: Rewrite `TaskManager.slnx`**

Replace the whole file with:

```xml
<Solution>
  <Configurations>
    <Platform Name="Any CPU" />
    <Platform Name="x64" />
    <Platform Name="x86" />
  </Configurations>
  <Project Path="src/TaskManager.Domain/TaskManager.Domain.csproj" />
  <Project Path="src/TaskManager.Shared/TaskManager.Shared.csproj" />
  <Project Path="src/TaskManager.Utility/TaskManager.Utility.csproj" />
  <Project Path="src/TaskManager/TaskManager.csproj" />
  <Project Path="tests/TaskManager.UnitTests/TaskManager.UnitTests.csproj" />
</Solution>
```

(The IntegrationTests project does not exist until Task 5; its solution entry is added there.)

- [ ] **Step 3: Dedupe `OutputPath` in `src/TaskManager/TaskManager.csproj`**

Replace the two conflicting `<PropertyGroup>` blocks at the top with ONE:

```xml
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <RootNamespace>TaskManager</RootNamespace>
    <UseWPF>true</UseWPF>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>
    <OutputPath>$(SolutionDir)\bin\$(Configuration)</OutputPath>
  </PropertyGroup>
```

(The second PropertyGroup containing `<OutputPath>` and `<AssemblyName>` is deleted entirely; AssemblyName defaults to the project name.)

- [ ] **Step 4: Pin SDK in `global.json`**

Replace whole file with:

```json
{
  "sdk": {
    "version": "10.0.303",
    "rollForward": "latestFeature"
  },
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
```

(Verify `dotnet --version` returns `10.0.303` first; if it differs, pin what it prints.)

- [ ] **Step 5: Validate**

```bash
dotnet build TaskManager.slnx
dotnet test tests/TaskManager.UnitTests
```

Both must succeed. If the exe fails to find output path because `$(SolutionDir)` is undefined when building the csproj alone, that is pre-existing behavior — building via the solution is the supported path.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "build: restructure solution into src/tests layout, pin SDK, dedupe OutputPath"
```

---

### Task 2: Dissolve TaskManager.Utility

**Files:**
- Create: `src/TaskManager.Domain/Primitives/{RefreshFrequencyType.cs, ProcessBasePriority.cs}`
- Move+modify: `src/TaskManager.Utility/Utility/{ArchitectureType,DataType,ExportationType,EnumExtensions,LanguageDictionary,IgnoreSerialization}.cs` → `src/TaskManager.Domain/Primitives/`
- Create: `src/TaskManager/UI/Localization/{EnumHelper.cs,PriorityTypeHelper.cs,RefreshFrequencyTypeHelper.cs}`
- Move+modify: `src/TaskManager/UI/Controls/VisualTreeUtilityHelper.cs`
- Create: `src/TaskManager/ViewModels/Preconditions.cs`
- Delete: `OperationType.cs`, then the whole `src/TaskManager.Utility/` directory
- Modify: `src/TaskManager/TaskManager.csproj`, `src/TaskManager.Domain/TaskManager.Domain.csproj`, `tests/TaskManager.UnitTests/TaskManager.UnitTests.csproj` (drop Utility references)
- Modify: every `.cs` importing `TaskManager.Utility.Utility` (bulk replace, then targeted fixes listed below)
- Modify: `TaskManager.slnx` (remove Utility entry)

**Interfaces:**
- Consumes: nothing new; consumes existing `Strings` resource class (still at `TaskManager.Shared.Resources.Languages` until Task 3).
- Produces (later tasks rely on these exact names):
  - `TaskManager.Domain.Primitives.RefreshFrequencyType` (enum) and `TaskManager.Domain.Primitives.RefreshFrequencies.SecondsMapping` (`Dictionary<RefreshFrequencyType,int>`)
  - `TaskManager.Domain.Primitives.ProcessBasePriority.Get(System.Diagnostics.ProcessPriorityClass) : int`
  - `TaskManager.UI.Localization.PriorityTypeHelper` / `RefreshFrequencyTypeHelper` / `EnumHelper` (same member signatures as today, minus pure members that moved to Domain)
  - `TaskManager.ViewModels.Preconditions` (Flags enum, unchanged values)
  - `TaskManager.UI.Controls.VisualTreeUtilityHelper` (members unchanged)

- [ ] **Step 1: Move pure primitives into Domain**

```bash
mkdir src/TaskManager.Domain/Primitives
git mv src/TaskManager.Utility/Utility/ArchitectureType.cs src/TaskManager.Domain/Primitives/ArchitectureType.cs
git mv src/TaskManager.Utility/Utility/DataType.cs src/TaskManager.Domain/Primitives/DataType.cs
git mv src/TaskManager.Utility/Utility/ExportationType.cs src/TaskManager.Domain/Primitives/ExportationType.cs
git mv src/TaskManager.Utility/Utility/EnumExtensions.cs src/TaskManager.Domain/Primitives/EnumExtensions.cs
git mv src/TaskManager.Utility/Utility/LanguageDictionary.cs src/TaskManager.Domain/Primitives/LanguageDictionary.cs
git mv src/TaskManager.Utility/Utility/IgnoreSerialization.cs src/TaskManager.Domain/Primitives/IgnoreSerialization.cs
git rm src/TaskManager.Utility/Utility/OperationType.cs
```

In each of the six moved files, replace the namespace declaration `TaskManager.Utility.Utility` with `TaskManager.Domain.Primitives`:

```pwsh
Get-ChildItem src/TaskManager.Domain/Primitives/*.cs |
  ForEach-Object { (Get-Content $_) -replace 'TaskManager\.Utility\.Utility','TaskManager.Domain.Primitives' | Set-Content $_ }
```

(`EnumExtensions.cs` and `IgnoreSerialization.cs` use tabs/odd indentation — only the namespace line changes.)

- [ ] **Step 2: Split RefreshFrequencyType — Domain half**

Create `src/TaskManager.Domain/Primitives/RefreshFrequencyType.cs`:

```csharp
namespace TaskManager.Domain.Primitives
{
    /// <summary>
    /// Specifies the frequency at which a refresh operation occurs for processes
    /// </summary>
    public enum RefreshFrequencyType
    {
        High,
        Medium,
        Low,
        Paused,
    }

    public static class RefreshFrequencies
    {
        public static readonly Dictionary<RefreshFrequencyType, int> SecondsMapping = new Dictionary<RefreshFrequencyType, int>
        {
            { RefreshFrequencyType.High, 5 },
            { RefreshFrequencyType.Medium, 7 },
            { RefreshFrequencyType.Low, 10 },
            { RefreshFrequencyType.Paused, 0 },
        };
    }
}
```

- [ ] **Step 3: Split PriorityTypeHelper — Domain half**

Create `src/TaskManager.Domain/Primitives/ProcessBasePriority.cs`:

```csharp
using System.Diagnostics;

namespace TaskManager.Domain.Primitives
{
    public static class ProcessBasePriority
    {
        private static readonly Dictionary<ProcessPriorityClass, int> BasePriorityMap = new Dictionary<ProcessPriorityClass, int>
        {
            { ProcessPriorityClass.Idle, 4 },
            { ProcessPriorityClass.BelowNormal, 6 },
            { ProcessPriorityClass.Normal, 8 },
            { ProcessPriorityClass.AboveNormal, 10 },
            { ProcessPriorityClass.High, 13 },
            { ProcessPriorityClass.RealTime, 24 },
        };

        /// <summary>Maps a ProcessPriorityClass to its Windows base priority value.</summary>
        public static int Get(ProcessPriorityClass priority)
        {
            return BasePriorityMap.TryGetValue(priority, out int basePriority)
                ? basePriority
                : (int)priority;
        }
    }
}
```

- [ ] **Step 4: Localization helpers move into the app**

```bash
mkdir src/TaskManager/UI/Localization
git mv src/TaskManager.Utility/Utility/EnumHelper.cs src/TaskManager/UI/Localization/EnumHelper.cs
```

In `src/TaskManager/UI/Localization/EnumHelper.cs`, change namespace to `TaskManager.UI.Localization`. Content otherwise unchanged:

```csharp
namespace TaskManager.UI.Localization
{
    public static class EnumHelper
    {
        public static TEnum MapLocalStringToEnum<TEnum>(string input, Dictionary<string, TEnum> mapping) where TEnum : struct, Enum
        {
            if (!typeof(TEnum).IsEnum)
            {
                throw new ArgumentException("TEnum must be an enum type.");
            }

            if (mapping.TryGetValue(input, out TEnum value))
            {
                return value;
            }
            else
            {
                throw new InvalidOperationException("Invalid value for the given enum type.");
            }
        }

        public static string MapEnumToLocalString<TEnum>(TEnum enumValue, Dictionary<string, TEnum> mapping) where TEnum : struct, Enum
        {
            foreach (var kvp in mapping)
            {
                if (EqualityComparer<TEnum>.Default.Equals(kvp.Value, enumValue))
                {
                    return kvp.Key;
                }
            }

            throw new NotImplementedException("Mapping not found for the provided enum value.");
        }

        public static IEnumerable<string> GetAllLocalizedOptions<TEnum>(Dictionary<string, TEnum> mapping)
            where TEnum : struct, Enum
        {
            return Enum.GetValues(typeof(TEnum))
                       .Cast<TEnum>()
                       .Select(enumValue => MapEnumToLocalString(enumValue, mapping));
        }

        public static IEnumerable<string> GetLocalizedOptions<TEnum>(IEnumerable<TEnum> enumValues, Dictionary<string, TEnum> mapping) where TEnum : struct, Enum
        {
            return enumValues.Select(enumValue => MapEnumToLocalString(enumValue, mapping));
        }
    }
}
```

Create `src/TaskManager/UI/Localization/PriorityTypeHelper.cs` (localized members only):

```csharp
using System.Diagnostics;
using TaskManager.Shared.Resources.Languages;

namespace TaskManager.UI.Localization
{
    public static class PriorityTypeHelper
    {
        private static readonly Dictionary<string, ProcessPriorityClass> ProcessPriorityTypeMapping = new Dictionary<string, ProcessPriorityClass>
        {
            { Strings.RealTime, ProcessPriorityClass.RealTime },
            { Strings.High_m, ProcessPriorityClass.High },
            { Strings.AboveNormal, ProcessPriorityClass.AboveNormal },
            { Strings.Normal, ProcessPriorityClass.Normal },
            { Strings.BelowNormal, ProcessPriorityClass.BelowNormal },
            { Strings.Idle, ProcessPriorityClass.Idle },
        };

        public static ProcessPriorityClass MapLocalStringToEnum(string input)
        {
            return EnumHelper.MapLocalStringToEnum(input, ProcessPriorityTypeMapping);
        }

        public static string MapEnumToLocalString(ProcessPriorityClass priority)
        {
            return EnumHelper.MapEnumToLocalString(priority, ProcessPriorityTypeMapping);
        }

        public static IEnumerable<string> GetAllLocalized()
        {
            return EnumHelper.GetAllLocalizedOptions(ProcessPriorityTypeMapping);
        }

        public static IEnumerable<string> GetLocalized(IEnumerable<ProcessPriorityClass> priorityTypes)
        {
            return EnumHelper.GetLocalizedOptions(priorityTypes, ProcessPriorityTypeMapping);
        }
    }
}
```

Create `src/TaskManager/UI/Localization/RefreshFrequencyTypeHelper.cs` (localized members only):

```csharp
using TaskManager.Shared.Resources.Languages;
using TaskManager.Domain.Primitives;

namespace TaskManager.UI.Localization
{
    public static class RefreshFrequencyTypeHelper
    {
        private static readonly Dictionary<string, RefreshFrequencyType> LocalizedMapping = new Dictionary<string, RefreshFrequencyType>
        {
            { Strings.High, RefreshFrequencyType.High },
            { Strings.Medium, RefreshFrequencyType.Medium },
            { Strings.Low, RefreshFrequencyType.Low },
            { Strings.Paused, RefreshFrequencyType.Paused },
        };

        public static RefreshFrequencyType MapLocalStringToEnum(string input)
        {
            return EnumHelper.MapLocalStringToEnum(input, LocalizedMapping);
        }

        public static string MapEnumToLocalString(RefreshFrequencyType frequency)
        {
            return EnumHelper.MapEnumToLocalString(frequency, LocalizedMapping);
        }

        public static IEnumerable<string> GetAllLocalized()
        {
            return EnumHelper.GetAllLocalizedOptions(LocalizedMapping);
        }

        public static IEnumerable<string> GetLocalizedShapeTypes(IEnumerable<RefreshFrequencyType> frequencyTypes)
        {
            return EnumHelper.GetLocalizedOptions(frequencyTypes, LocalizedMapping);
        }
    }
}
```

- [ ] **Step 5: VisualTreeUtilityHelper and Preconditions move into the app**

```bash
git mv src/TaskManager.Utility/Utility/VisualTreeUtilityHelper.cs src/TaskManager/UI/Controls/VisualTreeUtilityHelper.cs
```

In that file, replace namespace `TaskManager.Utility.Utility` with `TaskManager.UI.Controls`.

Create `src/TaskManager/ViewModels/Preconditions.cs`:

```csharp
namespace TaskManager.ViewModels
{
    [Flags]
    public enum Preconditions
    {
        None = 0,
        SelectedAnyProcess = 1 << 1,
        GotConfirmation = 1 << 2,
    }
}
```

- [ ] **Step 6: Bulk-replace the old using everywhere**

```pwsh
Get-ChildItem -Recurse -Filter *.cs -Include *.cs src, tests |
  ForEach-Object { (Get-Content $_.FullName) -replace 'using TaskManager\.Utility\.Utility;','using TaskManager.Domain.Primitives;' | Set-Content $_.FullName }
```

This correctly retargets every consumer of the domain-primitive types (Domain services/models, app ViewModels/Infrastructure, unit tests).

- [ ] **Step 7: Targeted call-site fixes for split helpers**

`src/TaskManager.Domain/Services/TimerManager.cs` (~line 38):

```csharp
// before
Interval = MilisecondMultiplier * RefreshFrequencyTypeHelper.RefreshFrequencyTypeSecondsMapping[...]
// after
Interval = MilisecondMultiplier * RefreshFrequencies.SecondsMapping[...]
```

(preserve whatever indexer argument was already there — only the type/member prefix changes)

`src/TaskManager.Domain/Services/ProcessManager.cs` (~line 69):

```csharp
// before
var seconds = RefreshFrequencyTypeHelper.RefreshFrequencyTypeSecondsMapping[settings.ProcessesRefreshFrequency];
// after
var seconds = RefreshFrequencies.SecondsMapping[settings.ProcessesRefreshFrequency];
```

`src/TaskManager.Domain/Services/ProcessManager.cs` (~line 291):

```csharp
// before
item.Process.Priority = PriorityTypeHelper.GetBasePriority(priority);
// after
item.Process.Priority = ProcessBasePriority.Get(priority);
```

Add `using TaskManager.UI.Localization;` to these app files (they consume the localized helpers):

- `src/TaskManager/ViewModels/SetPriorityWindowViewModel.cs`
- `src/TaskManager/ViewModels/SettingsWindowViewModel.cs`
- `src/TaskManager/UI/Converters`-bound converters: `src/TaskManager/Utility/Converters/ProcessPriorityConverter.cs` and `ProcessRefreshFrequenceConverter.cs` **if** they reference the helper classes (check on build)

- [ ] **Step 8: Delete the Utility project and its references**

```bash
git rm -r src/TaskManager.Utility
```

Remove from `src/TaskManager/TaskManager.csproj`:

```xml
    <ProjectReference Include="..\TaskManager.Utility\TaskManager.Utility.csproj" />
```

(from `src/TaskManager.Domain/TaskManager.Domain.csproj`):

```xml
    <ProjectReference Include="..\TaskManager.Utility\TaskManager.Utility.csproj" />
```

(from `tests/TaskManager.UnitTests/TaskManager.UnitTests.csproj`):

```xml
    <ProjectReference Include="..\..\src\TaskManager.Utility\TaskManager.Utility.csproj" />
```

(Note: paths gained `..\..` depth in Task 1 — match whatever the current file contains.)

From `TaskManager.slnx`, delete the line `<Project Path="src/TaskManager.Utility/TaskManager.Utility.csproj" />`.

- [ ] **Step 9: Validate**

```bash
dotnet build TaskManager.slnx
dotnet test tests/TaskManager.UnitTests
```

If any residual `CS0246`/`CS1061` errors appear referencing helper members, they are consumers missed by Step 7 — add `using TaskManager.UI.Localization;` (localized helpers) or confirm the type now lives in `TaskManager.Domain.Primitives`, rebuild. Grep must show zero survivors:

```bash
rg -n "TaskManager\.Utility" --glob '!**/bin/**' --glob '!**/obj/**'
# expect: no matches
```

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "refactor: dissolve TaskManager.Utility by ownership (Domain primitives, app localization/UI)"
```

---

### Task 3: Dissolve TaskManager.Shared (localization into the app)

**Files:**
- Move: `src/TaskManager.Shared/Resources/Languages/{Strings.resx,Strings.pl.resx,Strings.Designer.cs}` → `src/TaskManager/Resources/Languages/`
- Delete: `src/TaskManager.Shared/` (whole project)
- Modify: `Strings.Designer.cs` (namespace line)
- Modify: `src/TaskManager/TaskManager.csproj` (resx wiring + drop Shared reference; remove STALE `Resources\Strings.*` entries)
- Modify: `tests/TaskManager.UnitTests/TaskManager.UnitTests.csproj` (drop Shared reference)
- Modify: 5 `.cs` files + 4 `.xaml` files (namespace updates below)
- Modify: `TaskManager.slnx` (remove Shared entry)

**Interfaces:**
- Produces: `TaskManager.Resources.Languages.Strings` (public generated resource class) living inside the exe assembly. All consumers use `using TaskManager.Resources.Languages;` or XAML `xmlns:resx="clr-namespace:TaskManager.Resources.Languages"`.

- [ ] **Step 1: Move the resources**

```bash
mkdir src/TaskManager/Resources/Languages
git mv src/TaskManager.Shared/Resources/Languages/Strings.resx src/TaskManager/Resources/Languages/Strings.resx
git mv src/TaskManager.Shared/Resources/Languages/Strings.pl.resx src/TaskManager/Resources/Languages/Strings.pl.resx
git mv src/TaskManager.Shared/Resources/Languages/Strings.Designer.cs src/TaskManager/Resources/Languages/Strings.Designer.cs
git rm -r src/TaskManager.Shared
```

- [ ] **Step 2: Retarget Designer namespace**

In `src/TaskManager/Resources/Languages/Strings.Designer.cs`, replace (one occurrence):

```csharp
namespace TaskManager.Shared.Resources.Languages {
```

with:

```csharp
namespace TaskManager.Resources.Languages {
```

(match the file's exact brace style when editing)

- [ ] **Step 3: Update C# consumers**

```pwsh
Get-ChildItem -Recurse -Filter *.cs src, tests |
  ForEach-Object { (Get-Content $_.FullName) -replace 'using TaskManager\.Shared\.Resources\.Languages;','using TaskManager.Resources.Languages;' | Set-Content $_.FullName }
```

Affected files (expected): `UiErrorHandler.cs`, `MainWindowViewModel.cs`, `SetPriorityWindowViewModel.cs`, `DataExportWindowViewModel.cs`, `UI/Localization/PriorityTypeHelper.cs`, `UI/Localization/RefreshFrequencyTypeHelper.cs`.

- [ ] **Step 4: Update XAML consumers**

In `MainWindow.xaml`, `DataExportWindow.xaml`, `SetPriorityWindow.xaml`, `SettingsWindow.xaml`, replace:

```xml
xmlns:resx="clr-namespace:TaskManager.Shared.Resources.Languages;assembly=TaskManager.Shared"
```

with (same assembly now — drop the assembly qualifier):

```xml
xmlns:resx="clr-namespace:TaskManager.Resources.Languages"
```

```pwsh
Get-ChildItem -Recurse -Filter *.xaml src/TaskManager |
  ForEach-Object { (Get-Content $_.FullName) -replace 'clr-namespace:TaskManager\.Shared\.Resources\.Languages;assembly=TaskManager\.Shared','clr-namespace:TaskManager.Resources.Languages' | Set-Content $_.FullName }
```

- [ ] **Step 5: Rewire app csproj resx entries and drop dead references**

In `src/TaskManager/TaskManager.csproj`: the existing `Compile Update="Resources\Strings.Designer.cs"` and `EmbeddedResource Update="Resources\Strings.resx"` entries are STALE (those files don't exist in the app). Replace both ItemGroups with:

```xml
  <ItemGroup>
    <Compile Update="Resources\Languages\Strings.Designer.cs">
      <DesignTime>True</DesignTime>
      <AutoGen>True</AutoGen>
      <DependentUpon>Strings.resx</DependentUpon>
    </Compile>
  </ItemGroup>

  <ItemGroup>
    <EmbeddedResource Update="Resources\Languages\Strings.resx">
      <Generator>PublicResXFileCodeGenerator</Generator>
      <LastGenOutput>Strings.Designer.cs</LastGenOutput>
    </EmbeddedResource>
  </ItemGroup>
```

Remove:

```xml
    <ProjectReference Include="..\TaskManager.Shared\TaskManager.Shared.csproj" />
```

From `tests/TaskManager.UnitTests/TaskManager.UnitTests.csproj` remove its `TaskManager.Shared` ProjectReference (adjust relative depth as found).

From `TaskManager.slnx` delete the `src/TaskManager.Shared/...` line.

- [ ] **Step 6: Validate**

```bash
dotnet build TaskManager.slnx
dotnet test tests/TaskManager.UnitTests
rg -n "TaskManager\.Shared" --glob '!**/bin/**' --glob '!**/obj/**'
# expect: no matches
```

Sanity-check the Polish satellite: `src/TaskManager/bin/<config>/pl/TaskManager.resources.dll` exists after build (name changed from `TaskManager.Shared.resources.dll`).

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor: localize resources owned by the app; dissolve TaskManager.Shared"
```

---

### Task 4: Naming fixes — DataExport folder, converters relocation, XAML cleanup

**Files:**
- Rename: `src/TaskManager.Domain/Services/Data Export/` → `DataExport/` (7 exporter files incl. `BaseDataExporter.cs`)
- Move: `src/TaskManager/Utility/Converters/{ProcessPriorityConverter.cs,ProcessRefreshFrequenceConverter.cs}` → `src/TaskManager/UI/Converters/`
- Delete: empty `src/TaskManager/Utility/` folder
- Modify: affected `.cs` namespaces; `SettingsWindow.xaml`, `SetPriorityWindow.xaml` (converters xmlns; drop unused `util:` xmlns)

**Interfaces:**
- Produces: namespaces `TaskManager.Domain.Services.DataExport` and `TaskManager.UI.Converters`. No type bodies change.

- [ ] **Step 1: Rename Domain export folder and namespace**

```bash
git mv "src/TaskManager.Domain/Services/Data Export" src/TaskManager.Domain/Services/DataExport
```

```pwsh
Get-ChildItem -Recurse -Filter *.cs src, tests |
  ForEach-Object { (Get-Content $_.FullName) -replace 'TaskManager\.Domain\.Services\.Data_Export','TaskManager.Domain.Services.DataExport' | Set-Content $_.FullName }
```

- [ ] **Step 2: Relocate app converters**

```bash
mkdir src/TaskManager/UI/Converters
git mv src/TaskManager/Utility/Converters/ProcessPriorityConverter.cs src/TaskManager/UI/Converters/ProcessPriorityConverter.cs
git mv src/TaskManager/Utility/Converters/ProcessRefreshFrequenceConverter.cs src/TaskManager/UI/Converters/ProcessRefreshFrequenceConverter.cs
```

If other converter files exist in the old folder, move them too. Then remove the now-empty folder:

```bash
git rm -r src/TaskManager/Utility 2>$null; if (Test-Path src/TaskManager/Utility) { Remove-Item -Recurse -Force src/TaskManager/Utility }
```

In the moved converter files, replace namespace `TaskManager.Utility.Converters` with `TaskManager.UI.Converters`.

- [ ] **Step 3: Update XAML**

`SettingsWindow.xaml`:
- Remove the now-dead line `xmlns:util="clr-namespace:TaskManager.Utility.Utility;assembly=TaskManager.Utility"` (declared but never used — verified by grep).
- Replace `xmlns:converters="clr-namespace:TaskManager.Utility.Converters"` with `xmlns:converters="clr-namespace:TaskManager.UI.Converters"`.

`SetPriorityWindow.xaml`: same converters xmlns replacement.

```pwsh
Get-ChildItem -Recurse -Filter *.xaml src/TaskManager |
  ForEach-Object { (Get-Content $_.FullName) -replace 'clr-namespace:TaskManager\.Utility\.Converters','clr-namespace:TaskManager.UI.Converters' | Set-Content $_.FullName }
```

(Remove the `util:` line manually — single occurrence.)

- [ ] **Step 4: Validate**

```bash
dotnet build TaskManager.slnx
dotnet test tests/TaskManager.UnitTests
rg -n "TaskManager\.Utility|Data_Export|Data Export" --glob '!**/bin/**' --glob '!**/obj/**' --glob '!docs/**'
# expect: no matches
```

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: rename Data Export to DataExport, relocate converters to UI, clean XAML namespaces"
```

---

### Task 5: Split integration tests into own project; standardize unit-test namespaces

**Files:**
- Create: `tests/TaskManager.IntegrationTests/TaskManager.IntegrationTests.csproj`
- Move: `tests/TaskManager.UnitTests/"Integration Tests"/ProcessManagementTests/*` (4 files) → `tests/TaskManager.IntegrationTests/ProcessManagement/`
- Modify: `src/TaskManager.Domain/TaskManager.Domain.csproj` + `src/TaskManager/TaskManager.csproj` (`InternalsVisibleTo`)
- Modify: all `tests/TaskManager.UnitTests/**/*.cs` namespaces; folders `UI_Behaviors`, `UI_Controls`, `UI_Formatters`, `Viewmodels` renamed
- Modify: `tests/TaskManager.UnitTests/TaskManager.UnitTests.csproj` (merge duplicate Using groups; drop app-irrelevant refs stay unchanged)
- Modify: `TaskManager.slnx` (swap test project entries)

**Interfaces:**
- Consumes: existing test files unchanged behaviorally.
- Produces: `dotnet test tests/TaskManager.UnitTests` runs hermetic suite; `dotnet test tests/TaskManager.IntegrationTests` runs live-system suite. Namespaces: `TaskManager.UnitTests.<Area>`, `TaskManager.IntegrationTests.ProcessManagement`.

- [ ] **Step 1: Create the integration test project**

`tests/TaskManager.IntegrationTests/TaskManager.IntegrationTests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <RootNamespace>TaskManager.IntegrationTests</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="Shouldly" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="xunit.v3" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
    <Using Include="Shouldly" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\TaskManager.Domain\TaskManager.Domain.csproj" />
  </ItemGroup>

</Project>
```

(All package versions resolve from CPM. If compile reveals NSubstitute usage in moved files, add `<PackageReference Include="NSubstitute" />` — still versionless.)

- [ ] **Step 2: Move the integration tests**

```bash
mkdir tests/TaskManager.IntegrationTests/ProcessManagement
git mv "tests/TaskManager.UnitTests/Integration Tests/ProcessManagementTests/NtSystemProcessEnumeratorTests.cs" tests/TaskManager.IntegrationTests/ProcessManagement/NtSystemProcessEnumeratorTests.cs
git mv "tests/TaskManager.UnitTests/Integration Tests/ProcessManagementTests/ProcessEnricherTests.cs" tests/TaskManager.IntegrationTests/ProcessManagement/ProcessEnricherTests.cs
git mv "tests/TaskManager.UnitTests/Integration Tests/ProcessManagementTests/ProcessManagerTests.cs" tests/TaskManager.IntegrationTests/ProcessManagement/ProcessManagerTests.cs
git mv "tests/TaskManager.UnitTests/Integration Tests/ProcessManagementTests/ProcessesData.cs" tests/TaskManager.IntegrationTests/ProcessManagement/ProcessesData.cs
```

Then remove the emptied `"Integration Tests"` tree from UnitTests. In each moved file set the namespace to `TaskManager.IntegrationTests.ProcessManagement` (whatever the old namespace was, e.g. `TaskManager.Tests.Integration_Tests.ProcessManagementTests`). If a moved file references `GridTestHost` or other `TestSupport` types, STOP and record it — those tests belong in the unit project instead; move them back rather than linking shared files.

- [ ] **Step 3: Update InternalsVisibleTo**

`src/TaskManager.Domain/TaskManager.Domain.csproj`:

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="TaskManager.UnitTests"/>
    <InternalsVisibleTo Include="TaskManager.IntegrationTests"/>
  </ItemGroup>
```

`src/TaskManager/TaskManager.csproj`: replace `TaskManager.Tests` with `TaskManager.UnitTests` (keep `DynamicProxyGenAssembly2`):

```xml
  <ItemGroup>
	<InternalsVisibleTo Include="TaskManager.UnitTests"/>
	<InternalsVisibleTo Include="DynamicProxyGenAssembly2"/>
  </ItemGroup>
```

- [ ] **Step 4: Standardize unit-test namespaces and folders**

```bash
$dirs = @{ 'UI_Behaviors'='UI/Behaviors'; 'UI_Controls'='UI/Controls'; 'UI_Formatters'='UI/Formatters'; 'Viewmodels'='ViewModels' }
foreach ($k in $dirs.Keys) {
  git mv "tests/TaskManager.UnitTests/$k" "tests/TaskManager.UnitTests/$($dirs[$k])"
}
```

```pwsh
Get-ChildItem -Recurse -Filter *.cs tests/TaskManager.UnitTests |
  ForEach-Object {
    $t = (Get-Content $_.FullName)
    $t = $t -replace 'namespace TaskManager\.Tests\.','namespace TaskManager.UnitTests.'
    $t = $t -replace '\.UI_Behaviors','.UI.Behaviors'
    $t = $t -replace '\.UI_Controls','.UI.Controls'
    $t = $t -replace '\.UI_Formatters','.UI.Formatters'
    $t = $t -replace '\.(Viewmodels|VVM_Tests)','.ViewModels'
    Set-Content $_.FullName $t
  }
```

(This also fixes `TaskManager.Tests.TestSupport` → `TaskManager.UnitTests.TestSupport` and folds `VVM_Tests` into `ViewModels` while leaving the physical file in `ViewModels/`.)

- [ ] **Step 5: Clean up unit-test csproj and solution**

`tests/TaskManager.UnitTests/TaskManager.UnitTests.csproj` — merge the duplicated Using groups into one and declare RootNamespace:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <RootNamespace>TaskManager.UnitTests</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="NSubstitute" />
    <PackageReference Include="Shouldly" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Xunit.StaFact" />
    <PackageReference Include="xunit.v3" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
    <Using Include="Shouldly" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\TaskManager\TaskManager.csproj" />
    <ProjectReference Include="..\..\src\TaskManager.Domain\TaskManager.Domain.csproj" />
  </ItemGroup>

</Project>
```

`TaskManager.slnx` — remove the old UnitTests line and add BOTH:

```xml
  <Project Path="tests/TaskManager.IntegrationTests/TaskManager.IntegrationTests.csproj" />
  <Project Path="tests/TaskManager.UnitTests/TaskManager.UnitTests.csproj" />
```

- [ ] **Step 6: Validate**

```bash
dotnet build TaskManager.slnx
dotnet test tests/TaskManager.UnitTests          # hermetic suite — green required
dotnet test tests/TaskManager.IntegrationTests   # live-system suite — must run; investigate failures only if caused by the refactor (namespaces/references), not by environment
rg -n "TaskManager\.Tests\b|Integration_Tests|VVM_Tests|UI_Behaviors|UI_Controls|UI_Formatters" --glob '!**/bin/**' --glob '!**/obj/**' --glob '!docs/**'
# expect: no matches
```

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "build: separate integration test project; standardize test namespaces"
```

---

### Task 6: README + final verification sweep

**Files:**
- Modify: `README.md`
- No code changes except fixes surfaced by the sweep.

**Interfaces:**
- Consumes: final layout from Tasks 1–5.
- Produces: documentation matching reality; zero spec violations.

- [ ] **Step 1: Write README.md**

Replace the file contents with:

```markdown
# Task Manager

A Windows process explorer built with WPF on .NET 10: live process list with low-overhead diffing,
per-process detail enrichment (path/bitness via native APIs), priority management, settings, and
data export (Excel/CSV/JSON/XML/TXT). English and Polish UI.

## Solution layout

```
src/
  TaskManager/           WPF application (views, view models, app services, localization)
  TaskManager.Domain/    UI-free core: process models, diff engine, enricher, exporters
tests/
  TaskManager.UnitTests/         fast, hermetic test suite (default `dotnet test` target)
  TaskManager.IntegrationTests/  tests against live system state; run explicitly
```

Dependency rule: only the WPF exe references WPF. Domain depends on the BCL plus
NtApiDotNet and ClosedXML.

## Build & run

Requires the .NET SDK pinned in `global.json`.

```bash
dotnet build TaskManager.slnx
dotnet run --project src/TaskManager
```

## Test

```bash
dotnet test tests/TaskManager.UnitTests          # hermetic suite
dotnet test tests/TaskManager.IntegrationTests   # touches real system state
```

## Design docs

Architecture decisions and plans live under `docs/superpowers/` (`specs/`, `plans/`).
```

(The nested triple-backtick block renders as-is in markdown; adjust fence width if your renderer complains.)

- [ ] **Step 2: Verification sweep**

```bash
rg -n "TaskManager\.Utility|TaskManager\.Shared|TaskManager\.Tests\b|Data_Export|Preconditions\.cs" --glob '!**/bin/**' --glob '!**/obj/**' --glob '!docs/**' -g '!*.md'
# expect: only legitimate hits (ViewModels/Preconditions.cs itself); zero Shared/Utility/old-test-ns hits

git ls-files | Select-String '\.(dll|exe|pdb)$'
# expect: empty

dotnet build TaskManager.slnx
dotnet test tests/TaskManager.UnitTests
dotnet test tests/TaskManager.IntegrationTests
```

Also confirm `Directory.Packages.props` needs no changes (no new packages were introduced) and `src/TaskManager.Domain` csproj contains NO WPF property and NO app/shared/utility references.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "docs: describe architecture, layout, and build/test workflow in README"
```

---

## Self-Review Notes

- Spec coverage: ownership table (spec §Code Ownership Moves) → Tasks 2–3; naming/hygiene items 1–3 → Task 4–5; item 4 (OutputPath) → Task 1; item 5 (duplicate Usings) → Task 5; item 6 (SDK pin) → Task 1; item 7 (ViewModelBase slim-down) → intentionally deferred: inspection shows it is app-internal and harmless; touching it adds risk with no structural gain — recorded here as a conscious deviation pending user approval; item 8 (README) → Task 6. Integration separation (spec §Integration Test Separation) → Task 5.
- Type consistency: `RefreshFrequencies.SecondsMapping`, `ProcessBasePriority.Get`, `TaskManager.UI.Localization.*`, `TaskManager.Resources.Languages.Strings` used consistently across tasks.
- Migration order respects dependencies: Utility dissolution (T2) precedes Shared dissolution (T3) because the new localization helpers temporarily reference `TaskManager.Shared.Resources.Languages`.
