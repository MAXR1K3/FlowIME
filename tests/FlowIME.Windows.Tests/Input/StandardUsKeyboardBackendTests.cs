using FlowIME.Core.Automation;
using FlowIME.Core.Models;
using FlowIME.Windows.Input;

namespace FlowIME.Windows.Tests.Input;

public sealed class StandardUsKeyboardBackendTests
{
    private static readonly nint UsLayout = unchecked((nint)0x04090409u);
    private static readonly nint ChineseLayout = unchecked((nint)0x08040804u);

    [Fact]
    public async Task Apply_switches_the_focused_target_and_reports_success_only_after_verification()
    {
        var native = new FakeNative([UsLayout], ChineseLayout)
        {
            ApplyRequestedLayout = true
        };
        var backend = new StandardUsKeyboardBackend(
            new FakeFocusWindowResolver((nint)0x66),
            native,
            new ImmediateDelay());

        var result = await backend.ApplyAsync(
            TestWindow(),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Null(result.ErrorCode);
        Assert.Equal(ChineseLayout, result.Before.KeyboardLayout);
        Assert.Equal(UsLayout, result.After.KeyboardLayout);
        Assert.Equal((nint)0x66, native.LastTargetWindow);
        Assert.Equal(1, native.RequestCount);
    }

    [Fact]
    public async Task Apply_fails_when_the_request_is_accepted_but_the_target_layout_never_changes()
    {
        var native = new FakeNative([UsLayout], ChineseLayout);
        var backend = new StandardUsKeyboardBackend(
            new FakeFocusWindowResolver((nint)0x66),
            native,
            new ImmediateDelay());

        var result = await backend.ApplyAsync(
            TestWindow(),
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("target-layout-not-applied", result.ErrorCode);
        Assert.Equal(ChineseLayout, result.After.KeyboardLayout);
        Assert.Equal(1, native.RequestCount);
    }

    [Fact]
    public async Task Apply_fails_without_requesting_when_standard_us_is_not_installed()
    {
        var native = new FakeNative([ChineseLayout], ChineseLayout);
        var backend = new StandardUsKeyboardBackend(
            new FakeFocusWindowResolver((nint)0x66),
            native,
            new ImmediateDelay());

        var result = await backend.ApplyAsync(
            TestWindow(),
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("us-keyboard-unavailable", result.ErrorCode);
        Assert.Equal(0, native.RequestCount);
    }

    [Fact]
    public async Task Apply_succeeds_without_a_redundant_request_when_target_is_already_standard_us()
    {
        var native = new FakeNative([UsLayout], UsLayout);
        var backend = new StandardUsKeyboardBackend(
            new FakeFocusWindowResolver((nint)0x66),
            native,
            new ImmediateDelay());

        var result = await backend.ApplyAsync(
            TestWindow(),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(UsLayout, result.After.KeyboardLayout);
        Assert.Equal(0, native.RequestCount);
    }

    [Fact]
    public async Task Apply_rejects_a_focus_window_owned_by_a_different_gui_thread()
    {
        var native = new FakeNative([UsLayout], ChineseLayout)
        {
            WindowThreadId = 100,
            ApplyRequestedLayout = true
        };
        var backend = new StandardUsKeyboardBackend(
            new FakeFocusWindowResolver((nint)0x66),
            native,
            new ImmediateDelay());

        var result = await backend.ApplyAsync(
            TestWindow(),
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("target-thread-mismatch", result.ErrorCode);
        Assert.Equal(0, native.RequestCount);
    }

    [Fact]
    public async Task Apply_verifies_the_exact_requested_hkl_not_only_the_language_family()
    {
        var native = new FakeNative([UsLayout], ChineseLayout)
        {
            ApplyRequestedLayout = true,
            AppliedLayout = (nint)0x00000409
        };
        var backend = new StandardUsKeyboardBackend(
            new FakeFocusWindowResolver((nint)0x66),
            native,
            new ImmediateDelay());

        var result = await backend.ApplyAsync(
            TestWindow(),
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("target-layout-not-applied", result.ErrorCode);
    }

    [Fact]
    public async Task Native_failures_are_returned_as_structured_operation_results()
    {
        var native = new FakeNative([UsLayout], ChineseLayout)
        {
            ThrowOnGetKeyboardLayout = true
        };
        var backend = new StandardUsKeyboardBackend(
            new FakeFocusWindowResolver((nint)0x66),
            native,
            new ImmediateDelay());

        var result = await backend.ApplyAsync(
            TestWindow(),
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("native-operation-InvalidOperationException", result.ErrorCode);
    }

    private static WindowContext TestWindow() =>
        new(
            (nint)0x55,
            20,
            99,
            "Notepad",
            @"C:\Windows\notepad.exe",
            "Untitled - Notepad",
            "Notepad",
            null);

    private sealed class FakeFocusWindowResolver(nint effectiveWindow) : IFocusWindowResolver
    {
        public FocusWindowResult Resolve(nint topLevelWindow) =>
            new(
                topLevelWindow,
                99,
                topLevelWindow,
                effectiveWindow,
                0,
                effectiveWindow,
                FocusWindowSource.Focus,
                true,
                0,
                null);
    }

    private sealed class FakeNative(
        IReadOnlyList<nint> layouts,
        nint currentLayout) : IGameplayKeyboardNativeApi
    {
        public bool ApplyRequestedLayout { get; init; }
        public nint AppliedLayout { get; init; }
        public bool ThrowOnGetKeyboardLayout { get; init; }
        public uint WindowThreadId { get; init; } = 99;
        public uint WindowProcessId { get; init; } = 20;
        public int RequestCount { get; private set; }
        public nint LastTargetWindow { get; private set; }
        public nint CurrentLayout { get; private set; } = currentLayout;

        public IReadOnlyList<nint> GetKeyboardLayouts() => layouts;

        public nint GetKeyboardLayout(uint threadId)
        {
            if (ThrowOnGetKeyboardLayout)
            {
                throw new InvalidOperationException("layout read failed");
            }

            return CurrentLayout;
        }

        public uint GetWindowThreadId(
            nint hwnd,
            out uint processId,
            out int errorCode)
        {
            processId = WindowProcessId;
            errorCode = 0;
            return WindowThreadId;
        }

        public bool RequestInputLanguageChange(
            nint hwnd,
            nint keyboardLayout,
            out int errorCode)
        {
            RequestCount++;
            LastTargetWindow = hwnd;
            if (ApplyRequestedLayout)
            {
                CurrentLayout = AppliedLayout != 0 ? AppliedLayout : keyboardLayout;
            }

            errorCode = 0;
            return true;
        }
    }

    private sealed class ImmediateDelay : IAutomationDelay
    {
        public ValueTask DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }
}
