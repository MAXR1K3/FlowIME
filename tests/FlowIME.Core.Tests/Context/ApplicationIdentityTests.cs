using FlowIME.Core.Context;
using FlowIME.Core.Models;

namespace FlowIME.Core.Tests.Context;

public sealed class ApplicationIdentityTests
{
    [Fact]
    public void Package_family_name_wins_over_versioned_WindowsApps_path()
    {
        var first = Window(
            @"C:\Program Files\WindowsApps\OpenAI.Codex_26.908.0_x64__abc\app\ChatGPT.exe",
            packageFamilyName: "OpenAI.Codex_abc");
        var updated = Window(
            @"C:\Program Files\WindowsApps\OpenAI.Codex_26.909.0_x64__abc\app\ChatGPT.exe",
            packageFamilyName: "OpenAI.Codex_abc");

        var firstIdentity = ApplicationIdentity.FromWindow(first);
        var updatedIdentity = ApplicationIdentity.FromWindow(updated);

        Assert.Equal(ApplicationIdentityKind.PackageFamilyName, firstIdentity.Kind);
        Assert.Equal(firstIdentity.Key, updatedIdentity.Key);
        Assert.Equal("pfn:openai.codex_abc", firstIdentity.Key);
    }

    [Fact]
    public void Versioned_WindowsApps_path_falls_back_to_executable_name_when_package_identity_is_unavailable()
    {
        var first = Window(
            @"C:\Program Files\WindowsApps\Vendor.App_1.0.0_x64__abc\App.exe");
        var updated = Window(
            @"C:\Program Files\WindowsApps\Vendor.App_2.0.0_x64__abc\App.exe");

        var firstIdentity = ApplicationIdentity.FromWindow(first);
        var updatedIdentity = ApplicationIdentity.FromWindow(updated);

        Assert.Equal(ApplicationIdentityKind.ExecutableName, firstIdentity.Kind);
        Assert.Equal(firstIdentity.Key, updatedIdentity.Key);
        Assert.Equal("exe:app.exe", firstIdentity.Key);
    }

    [Fact]
    public void Unpackaged_application_uses_full_path_to_avoid_same_name_collisions()
    {
        var first = ApplicationIdentity.FromWindow(Window(@"C:\Tools\App.exe"));
        var second = ApplicationIdentity.FromWindow(Window(@"D:\Other\App.exe"));

        Assert.Equal(ApplicationIdentityKind.ExecutablePath, first.Kind);
        Assert.NotEqual(first.Key, second.Key);
    }

    [Fact]
    public void Aumid_is_the_highest_priority_identity_when_available()
    {
        var identity = ApplicationIdentity.FromWindow(
            Window(
                @"C:\Program Files\WindowsApps\Vendor.App_1.0.0_x64__abc\App.exe",
                packageFamilyName: "Vendor.App_abc",
                aumid: "Vendor.App_abc!Main"));

        Assert.Equal(ApplicationIdentityKind.ApplicationUserModelId, identity.Kind);
        Assert.Equal("aumid:vendor.app_abc!main", identity.Key);
    }

    private static WindowContext Window(
        string? path,
        string? packageFamilyName = null,
        string? aumid = null) =>
        new(
            (nint)0x1234,
            10,
            11,
            "App",
            path,
            "App",
            "Window",
            packageFamilyName,
            aumid);
}
