using System.Xml.Linq;

namespace FlowIME.App.Tests;

/// <summary>Composition guards complement behavioral detector/repository tests.</summary>
public sealed class GameplayCompositionContractTests
{
    [Fact]
    public void Production_pipeline_shares_user_profiles_with_game_classification()
    {
        var services = Source("Services/AppServices.cs");
        Assert.Contains("new GameplayContextPipeline(", services, StringComparison.Ordinal);
        var pipeline = Source("Services/GameplayContextPipeline.cs");
        Assert.Contains("new GameplayEligibilityDetector(profiles)", pipeline, StringComparison.Ordinal);
        Assert.Contains("new StandardGameTextEntryDetector(profiles, runtimeState)", pipeline, StringComparison.Ordinal);
    }

    [Fact]
    public void Dialog_exposes_classification_independently_of_chat_and_explicit_exe_aliases()
    {
        var dialog = XDocument.Parse(Source("Views/GameTextEntryDialog.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        Assert.Contains(dialog.Descendants(), e => (string?)e.Attribute(x + "Name") == "ClassificationCombo");
        Assert.Contains(dialog.Descendants(), e => (string?)e.Attribute(x + "Name") == "AdditionalExecutablesBox");
        var code = Source("Views/GameTextEntryDialog.xaml.cs");
        Assert.Contains("Classification = classification.Value", code, StringComparison.Ordinal);
        Assert.Contains("AdditionalExecutablePaths =", code, StringComparison.Ordinal);
        Assert.Contains("GameProfileTargetValidator.HasReliableTarget(ResultProfile)", code, StringComparison.Ordinal);
        Assert.Contains("GameProfileTargetValidator.RequireReliableTarget(", Source("Services/AppServices.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Profile_load_refreshes_both_automation_and_foreground_status()
    {
        var services = Source("Services/AppServices.cs");
        var start = services.IndexOf("private async Task InitializeGameTextEntryProfilesAsync()", StringComparison.Ordinal);
        var end = services.IndexOf("private async Task InitializeAppSettingsAsync()", start, StringComparison.Ordinal);
        var load = services[start..end];
        Assert.Contains("ReplaceGameTextEntryProfiles(profiles)", load, StringComparison.Ordinal);
        var replaceStart = services.IndexOf("public void ReplaceGameTextEntryProfiles(", StringComparison.Ordinal);
        var replaceEnd = services.IndexOf("public RecentGameplayTarget? GetRecentGameplayTarget()", replaceStart, StringComparison.Ordinal);
        var replace = services[replaceStart..replaceEnd];
        Assert.Contains("RequestGameTextEntryRefresh();", replace, StringComparison.Ordinal);
        Assert.DoesNotContain("if (_gameTextEntryRuntimeState.GetSnapshot().Active)", replace, StringComparison.Ordinal);
    }

    private static string Source(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FlowIME.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory.FullName, "src", "FlowIME.App", relative));
    }
}
