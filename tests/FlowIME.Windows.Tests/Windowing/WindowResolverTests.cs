using FlowIME.Windows.Windowing;

namespace FlowIME.Windows.Tests.Windowing;

public sealed class WindowResolverTests
{
    [Fact]
    public async Task Zero_hwnd_returns_null_without_native_calls()
    {
        var native = new FakeWindowNativeApi { ThrowOnUse = true };
        var resolver = new WindowResolver(native);

        var result = await resolver.ResolveAsync(0, TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task Invalid_window_identity_returns_null()
    {
        var native = new FakeWindowNativeApi
        {
            ThreadId = 0,
            ProcessId = 0
        };
        var resolver = new WindowResolver(native);

        var result = await resolver.ResolveAsync((nint)0x1234, TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task Missing_executable_path_does_not_fail_resolution()
    {
        var native = new FakeWindowNativeApi
        {
            ThreadId = 77,
            ProcessId = 88,
            ProcessName = "adminapp",
            ExecutablePath = null,
            WindowTitle = "Administrator",
            WindowClass = "AdminWindow"
        };
        var resolver = new WindowResolver(native);

        var result = await resolver.ResolveAsync((nint)0x1234, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal((uint)88, result.ProcessId);
        Assert.Equal((uint)77, result.ThreadId);
        Assert.Equal("adminapp", result.ProcessName);
        Assert.Null(result.ExecutablePath);
    }

    [Fact]
    public async Task Resolves_full_window_context()
    {
        var native = new FakeWindowNativeApi
        {
            ThreadId = 123,
            ProcessId = 456,
            ProcessName = "Code",
            ExecutablePath = @"C:\Apps\Code.exe",
            WindowTitle = "Program.cs - FlowIME",
            WindowClass = "Chrome_WidgetWin_1",
            PackageFamilyName = "Example.Package_123",
            ApplicationUserModelId = "Example.Package_123!App"
        };
        var resolver = new WindowResolver(native);

        var result = await resolver.ResolveAsync((nint)0xCAFE, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal((nint)0xCAFE, result.Hwnd);
        Assert.Equal("Code", result.ProcessName);
        Assert.Equal(@"C:\Apps\Code.exe", result.ExecutablePath);
        Assert.Equal("Program.cs - FlowIME", result.WindowTitle);
        Assert.Equal("Chrome_WidgetWin_1", result.WindowClass);
        Assert.Equal("Example.Package_123", result.PackageFamilyName);
        Assert.Equal("Example.Package_123!App", result.ApplicationUserModelId);
    }

    private sealed class FakeWindowNativeApi : IWindowNativeApi
    {
        public bool ThrowOnUse { get; init; }
        public uint ThreadId { get; init; } = 1;
        public uint ProcessId { get; init; } = 2;
        public string? WindowTitle { get; init; }
        public string? WindowClass { get; init; }
        public string? ExecutablePath { get; init; }
        public string? ProcessName { get; init; } = "process";
        public string? PackageFamilyName { get; init; }
        public string? ApplicationUserModelId { get; init; }

        public uint GetWindowThreadProcessId(nint hwnd, out uint processId)
        {
            Guard();
            processId = ProcessId;
            return ThreadId;
        }

        public string? GetWindowTitle(nint hwnd)
        {
            Guard();
            return WindowTitle;
        }

        public string? GetWindowClass(nint hwnd)
        {
            Guard();
            return WindowClass;
        }

        public string? GetExecutablePath(uint processId)
        {
            Guard();
            return ExecutablePath;
        }

        public string? GetProcessName(uint processId, string? executablePath)
        {
            Guard();
            return ProcessName;
        }

        public string? GetPackageFamilyName(uint processId)
        {
            Guard();
            return PackageFamilyName;
        }

        public string? GetApplicationUserModelId(uint processId)
        {
            Guard();
            return ApplicationUserModelId;
        }

        private void Guard()
        {
            if (ThrowOnUse)
            {
                throw new InvalidOperationException("Native API should not have been called.");
            }
        }
    }
}
