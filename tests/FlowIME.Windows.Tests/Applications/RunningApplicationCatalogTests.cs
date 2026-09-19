using FlowIME.Core.Abstractions;
using FlowIME.Core.Models;
using FlowIME.Windows.Applications;

namespace FlowIME.Windows.Tests.Applications;

public sealed class RunningApplicationCatalogTests
{
    [Fact]
    public async Task Returns_visible_resolvable_apps_deduplicated_by_executable_path()
    {
        var windows = new FakeTopLevelWindowSource((nint)0x10, (nint)0x20, (nint)0x30);
        var resolver = new FakeWindowResolver(
            Context((nint)0x10, 100, "Code", @"C:\Apps\Code.exe"),
            Context((nint)0x20, 101, "Code", @"c:\apps\CODE.EXE"),
            Context((nint)0x30, 102, "chrome", @"C:\Apps\Chrome\chrome.exe"));
        var names = new FakeDisplayNameResolver(
            (@"C:\Apps\Code.exe", "Visual Studio Code"),
            (@"C:\Apps\Chrome\chrome.exe", "Google Chrome"));
        var catalog = new RunningApplicationCatalog(windows, resolver, names, ignoredProcessId: 999);

        var result = await catalog.GetRunningApplicationsAsync(TestContext.Current.CancellationToken);

        Assert.Collection(
            result,
            app =>
            {
                Assert.Equal("Google Chrome", app.DisplayName);
                Assert.Equal("chrome", app.ProcessName);
                Assert.Equal(@"C:\Apps\Chrome\chrome.exe", app.ExecutablePath);
            },
            app =>
            {
                Assert.Equal("Visual Studio Code", app.DisplayName);
                Assert.Equal("Code", app.ProcessName);
                Assert.Equal(@"C:\Apps\Code.exe", app.ExecutablePath);
            });
    }

    [Fact]
    public async Task Carries_packaged_identity_into_running_application_model()
    {
        var windows = new FakeTopLevelWindowSource((nint)0x10);
        var context = new WindowContext(
            (nint)0x10,
            100,
            101,
            "ChatGPT",
            @"C:\Program Files\WindowsApps\OpenAI.Codex_26.908.0_x64__abc\app\ChatGPT.exe",
            "Codex",
            "WinUIDesktopWin32WindowClass",
            "OpenAI.Codex_abc",
            "OpenAI.Codex_abc!App");
        var resolver = new FakeWindowResolver(context);
        var names = new FakeDisplayNameResolver((context.ExecutablePath!, "Codex"));
        var catalog = new RunningApplicationCatalog(
            windows,
            resolver,
            names,
            ignoredProcessId: 999);

        var result = await catalog.GetRunningApplicationsAsync(
            TestContext.Current.CancellationToken);

        var app = Assert.Single(result);
        Assert.Equal("OpenAI.Codex_abc", app.PackageFamilyName);
        Assert.Equal("OpenAI.Codex_abc!App", app.ApplicationUserModelId);
    }

    [Fact]
    public async Task Excludes_current_process_and_windows_without_an_executable_path()
    {
        var windows = new FakeTopLevelWindowSource((nint)0x10, (nint)0x20, (nint)0x30);
        var resolver = new FakeWindowResolver(
            Context((nint)0x10, 500, "FlowIME", @"C:\FlowIME\FlowIME.App.exe"),
            Context((nint)0x20, 600, "Protected", null),
            Context((nint)0x30, 700, "Notepad", @"C:\Windows\Notepad.exe"));
        var catalog = new RunningApplicationCatalog(
            windows,
            resolver,
            new FakeDisplayNameResolver((@"C:\Windows\Notepad.exe", "Notepad")),
            ignoredProcessId: 500);

        var result = await catalog.GetRunningApplicationsAsync(TestContext.Current.CancellationToken);

        var app = Assert.Single(result);
        Assert.Equal("Notepad", app.DisplayName);
        Assert.Equal((uint)700, app.ProcessId);
    }

    [Fact]
    public async Task Includes_accessible_background_processes_without_visible_windows()
    {
        var catalog = new RunningApplicationCatalog(
            new FakeTopLevelWindowSource(),
            new FakeWindowResolver(),
            new FakeDisplayNameResolver((@"C:\Apps\Agent.exe", "Background Agent")),
            ignoredProcessId: 999,
            new FakeProcessApplicationSource(
                new ProcessApplicationEntry(321, "agent", @"C:\Apps\Agent.exe")));

        var app = Assert.Single(await catalog.GetRunningApplicationsAsync(
            TestContext.Current.CancellationToken));

        Assert.Equal("Background Agent", app.DisplayName);
        Assert.Equal("agent", app.ProcessName);
        Assert.Equal(@"C:\Apps\Agent.exe", app.ExecutablePath);
        Assert.Equal((nint)0, app.MainWindowHandle);
        Assert.Equal((uint)321, app.ProcessId);
    }

    [Fact]
    public async Task Visible_application_identity_wins_over_duplicate_process_candidate()
    {
        var path = @"C:\Apps\Codex.exe";
        var context = new WindowContext(
            (nint)0x10,
            100,
            101,
            "Codex",
            path,
            "Codex",
            "WindowClass",
            "OpenAI.Codex_abc",
            "OpenAI.Codex_abc!App");
        var catalog = new RunningApplicationCatalog(
            new FakeTopLevelWindowSource((nint)0x10),
            new FakeWindowResolver(context),
            new FakeDisplayNameResolver((path, "Codex")),
            ignoredProcessId: 999,
            new FakeProcessApplicationSource(
                new ProcessApplicationEntry(100, "Codex", @"c:\apps\CODEX.EXE")));

        var app = Assert.Single(await catalog.GetRunningApplicationsAsync(
            TestContext.Current.CancellationToken));

        Assert.Equal((nint)0x10, app.MainWindowHandle);
        Assert.Equal("OpenAI.Codex_abc", app.PackageFamilyName);
        Assert.Equal("OpenAI.Codex_abc!App", app.ApplicationUserModelId);
    }

    private static WindowContext Context(
        nint hwnd,
        uint processId,
        string processName,
        string? path) =>
        new(
            hwnd,
            processId,
            processId + 1,
            processName,
            path,
            processName,
            "WindowClass",
            null);

    private sealed class FakeTopLevelWindowSource(params nint[] windows) : ITopLevelWindowSource
    {
        public IReadOnlyList<nint> EnumerateVisibleTopLevelWindows() => windows;
    }

    private sealed class FakeWindowResolver(params WindowContext[] contexts) : IWindowResolver
    {
        private readonly IReadOnlyDictionary<nint, WindowContext> _contexts =
            contexts.ToDictionary(context => context.Hwnd);

        public ValueTask<WindowContext?> ResolveAsync(
            nint hwnd,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _contexts.TryGetValue(hwnd, out var context);
            return ValueTask.FromResult(context);
        }
    }

    private sealed class FakeDisplayNameResolver(
        params (string Path, string DisplayName)[] values) : IApplicationDisplayNameResolver
    {
        private readonly IReadOnlyDictionary<string, string> _values =
            values.ToDictionary(
                value => value.Path,
                value => value.DisplayName,
                StringComparer.OrdinalIgnoreCase);

        public string Resolve(string executablePath, string processName) =>
            _values.TryGetValue(executablePath, out var name)
                ? name
                : processName;
    }

    private sealed class FakeProcessApplicationSource(params ProcessApplicationEntry[] entries)
        : IProcessApplicationSource
    {
        public IReadOnlyList<ProcessApplicationEntry> Enumerate() => entries;
    }
}
