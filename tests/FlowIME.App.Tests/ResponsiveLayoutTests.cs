using System.Xml.Linq;

namespace FlowIME.App.Tests;

public sealed class ResponsiveLayoutTests
{
    [Fact]
    public void Rule_row_content_is_vertically_centered_inside_the_full_item_height()
    {
        var doc = Xaml("Views/RulesPage.xaml");
        var content = Named(doc, "RuleContentGrid");

        Assert.Equal("Center", (string?)content.Attribute("VerticalAlignment"));
    }

    private static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FlowIME.sln"))) directory = directory.Parent;
            return directory!.FullName;
        }
    }

    [Theory]
    [InlineData("AddApplicationDialog.xaml")]
    [InlineData("EditRuleDialog.xaml")]
    [InlineData("GlobalDefaultDialog.xaml")]
    [InlineData("GameTextEntryDialog.xaml")]
    public void Dialog_content_never_forces_a_width_larger_than_its_viewport(string file)
    {
        var doc = Xaml("Views/" + file);
        Assert.Equal("{StaticResource FlowPrimaryButtonStyle}", (string?)doc.Root!.Attribute("PrimaryButtonStyle"));
        Assert.Equal("{StaticResource FlowSecondaryButtonStyle}", (string?)doc.Root!.Attribute("CloseButtonStyle"));
        Assert.Null(Named(doc, "DialogRoot").Attribute("Width"));
        Assert.DoesNotContain(doc.Descendants(), e => (string?)e.Attribute("Target") == "DialogRoot.Width");
    }

    [Fact]
    public void Settings_has_persistent_save_feedback_independent_of_status_refresh()
    {
        var doc = Xaml("Views/SettingsPage.xaml");
        Assert.Equal("Polite", (string?)Named(doc, "SettingsFeedbackBar").Attribute("AutomationProperties.LiveSetting"));
        var source = File.ReadAllText(Path.Combine(Root, "src", "FlowIME.App", "Views", "SettingsPage.xaml.cs"));
        Assert.Contains("ShowSettingsFeedback", source);
        Assert.Contains("InputStatusOverlayToggle.IsEnabled = false", source);
        Assert.Contains("finally", source);
    }

    [Fact]
    public void Rule_hover_uses_local_rounded_native_presenter()
    {
        var doc = Xaml("Views/RulesPage.xaml");
        var list = Named(doc, "RulesList");
        var app = XDocument.Load(Path.Combine(Root, "src", "FlowIME.App", "App.xaml"));
        var style = app.Descendants().Single(e => (string?)e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")) == "FlowRuleListItemStyle");
        var presenter = style.Descendants().Single(e => e.Name.LocalName == "ListViewItemPresenter");
        Assert.Equal("{TemplateBinding CornerRadius}", (string?)presenter.Attribute("CornerRadius"));
        Assert.Contains(style.Elements(), e => (string?)e.Attribute("Property") == "CornerRadius" && (string?)e.Attribute("Value") == "12");
        Assert.Equal("{StaticResource FlowRuleListItemStyle}", (string?)list.Attribute("ItemContainerStyle"));
    }

    [Fact]
    public void Application_picker_uses_two_columns_and_discloses_low_frequency_fields()
    {
        var doc = Xaml("Views/AddApplicationDialog.xaml");
        var source = File.ReadAllText(Path.Combine(Root, "src", "FlowIME.App", "Views", "AddApplicationDialog.xaml.cs"));
        Assert.Contains("scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto", source);
        Assert.Contains("scroll.VerticalScrollMode = ScrollMode.Enabled", source);
        Assert.Null(Named(doc, "DialogRoot").Attribute("MaxHeight"));
        var list = Named(doc, "ApplicationsList");
        Assert.Equal("360", (string?)list.Attribute("Height"));
        Assert.Equal("0", (string?)Named(doc, "ApplicationPickerColumn").Attribute("Grid.Column"));
        Assert.Equal("1", (string?)Named(doc, "RuleSettingsColumn").Attribute("Grid.Column"));
        var resources = doc.Descendants()
            .Where(e => e.Name.LocalName == "Double")
            .ToDictionary(
                e => (string)e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))!,
                e => e.Value);
        Assert.Equal("900", resources["ContentDialogMinWidth"]);
        Assert.Equal("960", resources["ContentDialogMaxWidth"]);
        var nameField = doc.Descendants().Single(e => (string?)e.Attribute("Header") == "规则名称");
        Assert.Contains(nameField.Ancestors(), e => e.Name.LocalName == "Expander");
        Assert.Equal("{StaticResource FlowSecondaryButtonStyle}", (string?)Named(doc, "BrowseExecutableButton").Attribute("Style"));
    }

    [Fact]
    public void Edit_rule_dialog_uses_two_columns_and_keeps_expanded_content_scrollable()
    {
        var doc = Xaml("Views/EditRuleDialog.xaml");
        var source = File.ReadAllText(Path.Combine(Root, "src", "FlowIME.App", "Views", "EditRuleDialog.xaml.cs"));

        Assert.Contains("scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto", source);
        Assert.Contains("scroll.VerticalScrollMode = ScrollMode.Enabled", source);
        Assert.Equal("0", (string?)Named(doc, "BasicSettingsColumn").Attribute("Grid.Column"));
        Assert.Equal("1", (string?)Named(doc, "AdvancedSettingsColumn").Attribute("Grid.Column"));
        Assert.Contains(Named(doc, "AdvancedSettingsColumn").Descendants(), e => e.Name.LocalName == "Expander");

        var resources = doc.Descendants()
            .Where(e => e.Name.LocalName == "Double")
            .ToDictionary(
                e => (string)e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))!,
                e => e.Value);
        Assert.Equal("900", resources["ContentDialogMinWidth"]);
        Assert.Equal("960", resources["ContentDialogMaxWidth"]);
    }

    [Fact]
    public void Home_automation_save_recovers_committed_state_and_reenables_control()
    {
        var source = File.ReadAllText(Path.Combine(Root, "src", "FlowIME.App", "Views", "HomePage.xaml.cs"));
        var start = source.IndexOf("private async void AutomationToggle_Toggled", StringComparison.Ordinal);
        var end = source.IndexOf("private void ManageRules_Click", start, StringComparison.Ordinal);
        var handler = source[start..end];
        Assert.Contains("catch (Exception", handler);
        Assert.Contains("_viewModel.IsAutomationEnabled = _services.IsAutomationEnabled", handler);
        Assert.Contains("AutomationToggle.IsEnabled = false", handler);
        Assert.Contains("finally", handler);
        Assert.Contains("AutomationToggle.IsEnabled = true", handler);
        Assert.Contains("InfoBarSeverity.Error", handler);
    }

    [Fact]
    public void Compact_navigation_reserves_space_outside_scrolling_pages()
    {
        var shell = Xaml("MainWindow.xaml");
        Assert.Equal("NavView_DisplayModeChanged", (string?)Named(shell, "NavView").Attribute("DisplayModeChanged"));
        var source = File.ReadAllText(Path.Combine(Root, "src", "FlowIME.App", "MainWindow.xaml.cs"));
        Assert.Contains("ContentFrame.Margin", source);
        Assert.Contains("NavigationViewDisplayMode.Minimal", source);
    }

    private static XDocument Xaml(string file) => XDocument.Load(Path.Combine(Root, "src", "FlowIME.App", file));
    private static XElement Named(XDocument doc, string name) => doc.Descendants().Single(e => (string?)e.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == name);

    [Theory]
    [InlineData("HomePage.xaml")]
    [InlineData("RulesPage.xaml")]
    [InlineData("SettingsPage.xaml")]
    public void Large_window_width_cap_is_owned_by_the_centered_viewport(string file)
    {
        var viewport = Xaml("Views/" + file).Root!.Elements().First(e => e.Name.LocalName == "ScrollViewer");
        Assert.Equal("Stretch", (string?)viewport.Attribute("HorizontalAlignment"));
        Assert.Equal("{StaticResource FlowPageContentMaxWidth}", (string?)viewport.Attribute("MaxWidth"));
    }

    [Fact]
    public void Whole_row_configuration_target_stretches_to_the_page_grid()
    {
        var styles = Xaml("App.xaml");
        var style = styles.Descendants().Single(e => (string?)e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")) == "FlowCardButtonStyle");
        Assert.Contains(style.Elements(), e => (string?)e.Attribute("Property") == "HorizontalAlignment" && (string?)e.Attribute("Value") == "Stretch");
    }

    [Theory]
    [InlineData("HomePage.xaml", "DesktopStructure")]
    [InlineData("RulesPage.xaml", "WideRuleCard")]
    [InlineData("SettingsPage.xaml", "DesktopSettingsStructure")]
    public void Structural_breakpoints_use_available_content_not_window_width(string file, string state)
    {
        var visualState = Named(Xaml("Views/" + file), state);
        Assert.Contains(visualState.Descendants(), e => e.Name.LocalName == "ContentWidthTrigger");
        Assert.DoesNotContain(visualState.Descendants(), e => e.Name.LocalName == "AdaptiveTrigger");
    }

    [Fact]
    public void Rules_header_and_list_are_both_reachable_in_short_windows()
    {
        var page = Xaml("Views/RulesPage.xaml");
        var root = Named(page, "PageRoot");
        Assert.Equal("ScrollViewer", root.Parent!.Name.LocalName);
        Assert.Equal("Disabled", (string?)root.Parent.Attribute("HorizontalScrollMode"));
    }

    [Fact]
    public void Home_closed_feedback_does_not_reserve_a_stack_gap()
    {
        Assert.Equal("Collapsed", (string?)Named(Xaml("Views/HomePage.xaml"), "HomeFeedbackBar").Attribute("Visibility"));
    }

    [Theory]
    [InlineData("HomePage.xaml")]
    [InlineData("RulesPage.xaml")]
    [InlineData("SettingsPage.xaml")]
    public void Page_switches_have_accessible_names_and_shared_compact_geometry(string file)
    {
        foreach (var toggle in Xaml("Views/" + file).Descendants().Where(e => e.Name.LocalName == "ToggleSwitch"))
        {
            Assert.NotNull(toggle.Attribute("AutomationProperties.Name"));
            Assert.Equal("{StaticResource FlowToggleSwitchStyle}", (string?)toggle.Attribute("Style"));
        }
    }
}
