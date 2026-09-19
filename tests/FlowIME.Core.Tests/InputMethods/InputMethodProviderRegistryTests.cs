using FlowIME.Core.Abstractions;
using FlowIME.Core.InputMethods;
using FlowIME.Core.Models;

namespace FlowIME.Core.Tests.InputMethods;

public sealed class InputMethodProviderRegistryTests
{
    [Fact]
    public void Registry_exposes_default_provider_and_case_insensitive_lookup()
    {
        var first = new FakeProvider("first", "First IME");
        var second = new FakeProvider("second", "Second IME");
        var registry = new InputMethodProviderRegistry([first, second], "FIRST");

        Assert.Same(first, registry.DefaultProvider);
        Assert.Equal(2, registry.Descriptors.Count);
        Assert.Same(second, registry.GetRequiredProvider("SECOND"));
        Assert.True(registry.TryGetProvider("first", out var resolved));
        Assert.Same(first, resolved);
    }

    [Fact]
    public void Registry_rejects_duplicate_provider_ids()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            new InputMethodProviderRegistry(
                [new FakeProvider("same", "One"), new FakeProvider("SAME", "Two")],
                "same"));

        Assert.Contains("Duplicate", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Registry_rejects_missing_default_provider()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            new InputMethodProviderRegistry(
                [new FakeProvider("registered", "Registered")],
                "missing"));

        Assert.Contains("not registered", error.Message, StringComparison.Ordinal);
    }

    private sealed class FakeProvider : IInputMethodProvider
    {
        public FakeProvider(string id, string name)
        {
            Descriptor = new InputMethodProviderDescriptor(
                id,
                name,
                new InputMethodProviderCapabilities(
                    CanDetectActiveProfile: true,
                    CanActivateProfile: true,
                    CanReadMode: true,
                    CanSetChinese: true,
                    CanSetEnglish: true,
                    RequiresPostActivationSettling: false));
        }

        public InputMethodProviderDescriptor Descriptor { get; }

        public ValueTask<InputMethodProviderDetectionResult> DetectAsync(
            WindowContext window,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new InputMethodProviderDetectionResult(true, true));

        public ValueTask<InputState> GetStateAsync(
            WindowContext window,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new InputState(Descriptor.DisplayName, InputMode.English, 0));

        public ValueTask<InputOperationResult> ApplyAsync(
            WindowContext window,
            InputAction action,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
