using System.Xml.Linq;

namespace FlowIME.App.Tests;

public sealed class UiShellContractTests
{
    [Fact]
    public void Unpackaged_publish_copies_compiled_xaml_resources()
    {
        var root = FindRepositoryRoot();
        var project = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "FlowIME.App.csproj"));

        Assert.Contains("CopyUnpackagedWinUiResourcesToPublish", project, StringComparison.Ordinal);
        Assert.Contains("$(OutputPath)**\\*.xbf", project, StringComparison.Ordinal);
        Assert.Contains("$(OutputPath)$(AssemblyName).pri", project, StringComparison.Ordinal);
        Assert.Contains("AfterTargets=\"Publish\"", project, StringComparison.Ordinal);
    }

    [Fact]
    public void Unpackaged_app_embeds_a_per_monitor_v2_manifest()
    {
        var root = FindRepositoryRoot();
        var projectPath = Path.Combine(root, "src", "FlowIME.App", "FlowIME.App.csproj");
        var project = XDocument.Load(projectPath);
        var manifestProperty = project.Descendants("ApplicationManifest").Single().Value;

        Assert.Equal("app.manifest", manifestProperty);

        var manifestText = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", manifestProperty));
        Assert.Contains("PerMonitorV2", manifestText, StringComparison.Ordinal);
    }

    [Fact]
    public void Home_page_uses_a_stretched_non_horizontal_scrolling_viewport()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "HomePage.xaml"));

        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollMode=\"Disabled\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalContentAlignment=\"Stretch\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxWidth=\"1040\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Shell_frame_stretches_page_content()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "MainWindow.xaml"));

        Assert.Contains("HorizontalContentAlignment=\"Stretch\"", xaml, StringComparison.Ordinal);
        Assert.Contains("VerticalContentAlignment=\"Stretch\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_exposes_real_startup_and_tray_lifecycle_controls()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "SettingsPage.xaml"));

        Assert.Contains("StartupToggle", xaml, StringComparison.Ordinal);
        Assert.Contains("StartupToggle_Toggled", xaml, StringComparison.Ordinal);
        Assert.Contains("关闭窗口后继续运行", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("StartupTask 后启用", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_exposes_real_input_status_overlay_control()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "SettingsPage.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "SettingsPage.xaml.cs"));

        Assert.Contains("输入状态浮层", xaml, StringComparison.Ordinal);
        Assert.Contains("InputStatusOverlayToggle", xaml, StringComparison.Ordinal);
        Assert.Contains("InputStatusOverlayToggle_Toggled", xaml, StringComparison.Ordinal);
        Assert.Contains("InputStatusOverlayPositionComboBox", xaml, StringComparison.Ordinal);
        Assert.Contains("跟随输入光标", xaml, StringComparison.Ordinal);
        Assert.Contains("InputStatusOverlaySizeComboBox", xaml, StringComparison.Ordinal);
        Assert.Contains("InputStatusOverlayOpacitySlider", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"InputStatusOverlayOpacityText\" Text=\"100%\"", xaml, StringComparison.Ordinal);
        Assert.Contains("InputStatusOverlayAnimationsToggle", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("输入状态提示", xaml, StringComparison.Ordinal);
        Assert.Contains("private bool _synchronizingStartupToggle = true;", source, StringComparison.Ordinal);
        Assert.Contains("private bool _synchronizingInputStatusOverlayToggle = true;", source, StringComparison.Ordinal);
        Assert.Contains("private bool _synchronizingGameplayKeyboardBaselineToggle = true;", source, StringComparison.Ordinal);
        Assert.Contains("private bool _synchronizingGameplayHotkeyToggles = true;", source, StringComparison.Ordinal);
        Assert.Contains("_pendingInputStatusOverlaySettings", source, StringComparison.Ordinal);
        Assert.Contains("SetInputStatusOverlaySettingsAsync", source, StringComparison.Ordinal);

        var services = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Services", "AppServices.cs"));
        Assert.Contains("persistent: false", services, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_exposes_copyable_diagnostics_snapshot()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "SettingsPage.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "SettingsPage.xaml.cs"));
        var writer = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Services", "DiagnosticsClipboardWriter.cs"));

        Assert.Contains("CopyDiagnosticsButton", xaml, StringComparison.Ordinal);
        Assert.Contains("复制诊断信息", xaml, StringComparison.Ordinal);
        Assert.Contains("CreateDiagnosticsReportAsync", source, StringComparison.Ordinal);
        Assert.Contains("DiagnosticsClipboardWriter", source, StringComparison.Ordinal);
        Assert.Contains("Clipboard.SetContent", writer, StringComparison.Ordinal);
        Assert.DoesNotContain("Clipboard.Flush", writer, StringComparison.Ordinal);
        Assert.Contains("MaxAttempts = 3", writer, StringComparison.Ordinal);
    }

    [Fact]
    public void Diagnostics_include_recent_privacy_bounded_context_decisions()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Services", "AppServices.cs"));

        Assert.Contains("recentDecisionCount=", source, StringComparison.Ordinal);
        Assert.Contains("recentDecision[", source, StringComparison.Ordinal);
        Assert.Contains("FormatContextSignalKinds(record.Signals)", source, StringComparison.Ordinal);
        Assert.Contains("provider={SanitizeDiagnosticToken(record.ProviderId", source, StringComparison.Ordinal);
        Assert.Contains("action={record.Action}", source, StringComparison.Ordinal);
        Assert.Contains("thread={record.TargetThreadId}", source, StringComparison.Ordinal);
        Assert.Contains("backend={SanitizeDiagnosticToken(record.Backend", source, StringComparison.Ordinal);
        Assert.Contains("beforeHkl={FormatKeyboardLayout(record.BeforeState?.KeyboardLayout", source, StringComparison.Ordinal);
        Assert.Contains("afterHkl={FormatKeyboardLayout(record.AfterState?.KeyboardLayout", source, StringComparison.Ordinal);
        Assert.Contains("error={SanitizeDiagnosticToken(record.ErrorCode", source, StringComparison.Ordinal);
        Assert.Contains("automationLogSinkEnabled=", source, StringComparison.Ordinal);
        Assert.Contains("automationLogPendingBytes=", source, StringComparison.Ordinal);
        Assert.Contains("automationLogFlushCount=", source, StringComparison.Ordinal);
        Assert.Contains("automationLogRotationCount=", source, StringComparison.Ordinal);
        Assert.Contains("automationLogLastErrorType=", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Automation_exception_log_keeps_hresult_and_sanitized_message()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FlowIME.Core",
            "Automation",
            "AutomationCoordinator.cs"));

        Assert.Contains("hresult=0x{unchecked((uint)ex.HResult):X8}", source, StringComparison.Ordinal);
        Assert.Contains("message={SanitizeTraceValue(ex.Message)}", source, StringComparison.Ordinal);
    }

    [Fact]
    public void P8B2_composes_gameplay_eligibility_without_adding_an_input_policy()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Services", "AppServices.cs"));

        Assert.Contains("new FullscreenWindowDetector()", source, StringComparison.Ordinal);
        Assert.Contains("new GameplayEligibilityDetector()", source, StringComparison.Ordinal);
        Assert.Contains("currentContextSignalDetails=", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GameplayInputPolicy", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Shared_context_engine_reuses_one_detection_result_per_native_event()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Services", "AppServices.cs"));

        Assert.Contains("new RecentInputContextCache", source, StringComparison.Ordinal);
        Assert.Contains("contextEngine: contextEngine", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Tray_host_uses_native_notification_area_and_survives_explorer_restart()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Services", "TrayIconService.cs"));

        Assert.Contains("Shell_NotifyIconW", source, StringComparison.Ordinal);
        Assert.Contains("TaskbarCreated", source, StringComparison.Ordinal);
        Assert.Contains("WTSRegisterSessionNotification", source, StringComparison.Ordinal);
        Assert.Contains("SystemRecoverySignal", source, StringComparison.Ordinal);
        Assert.Contains("打开 FlowIME", source, StringComparison.Ordinal);
        Assert.Contains("暂停自动切换", source, StringComparison.Ordinal);
        Assert.Contains("恢复自动切换", source, StringComparison.Ordinal);
        Assert.Contains("退出", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_input_backend_is_composed_through_the_provider_registry()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Services", "AppServices.cs"));

        Assert.Contains("new MicrosoftPinyinProvider()", source, StringComparison.Ordinal);
        Assert.Contains("new WeChatInputMethodProvider()", source, StringComparison.Ordinal);
        Assert.Contains("new InputMethodProviderRegistry", source, StringComparison.Ordinal);
        Assert.Contains("new ProviderInputMethodBackend", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new MicrosoftPinyinBackend()", source, StringComparison.Ordinal);
    }


    [Fact]
    public void Rule_dialogs_expose_provider_and_mode_selection()
    {
        var root = FindRepositoryRoot();
        var add = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "AddApplicationDialog.xaml"));
        var edit = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "EditRuleDialog.xaml"));
        var input = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "InputPage.xaml"));

        Assert.Contains("ProviderOptions", add, StringComparison.Ordinal);
        Assert.Contains("SelectedProviderId", add, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedActionOption, Mode=TwoWay}\"", add, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedValue=\"{Binding SelectedAction, Mode=TwoWay}\"", add, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"InputStateOptions\" Grid.Row=\"1\"", add, StringComparison.Ordinal);
        Assert.DoesNotContain("RuleOptionsSecondColumn", add, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{x:Bind IconVisibility, Mode=OneWay}\"", add, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{x:Bind FallbackIconVisibility, Mode=OneWay}\"", add, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding SearchQuery, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", add, StringComparison.Ordinal);
        Assert.Contains("Click=\"BrowseExecutable_Click\"", add, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding CustomDisplayName, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", add, StringComparison.Ordinal);
        Assert.Contains("Text=\"{x:Bind SourceLabel, Mode=OneWay}\"", add, StringComparison.Ordinal);
        Assert.Contains("ProviderOptions", edit, StringComparison.Ordinal);
        Assert.Contains("SelectedProviderId", edit, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedActionOption, Mode=TwoWay}\"", edit, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedValue=\"{Binding SelectedAction, Mode=TwoWay}\"", edit, StringComparison.Ordinal);
        Assert.Contains("微信输入法", input, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenStatus", input, StringComparison.Ordinal);
        Assert.DoesNotContain("ConversionMode", input, StringComparison.Ordinal);
    }

    [Fact]
    public void Rules_page_exposes_global_default_configuration()
    {
        var root = FindRepositoryRoot();
        var rules = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "RulesPage.xaml"));
        var dialog = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "GlobalDefaultDialog.xaml"));

        Assert.Contains("全局默认场景", rules, StringComparison.Ordinal);
        Assert.Contains("EditGlobalDefault_Click", rules, StringComparison.Ordinal);
        Assert.Contains("ProviderOptions", dialog, StringComparison.Ordinal);
        Assert.Contains("SelectedProviderId", dialog, StringComparison.Ordinal);
        Assert.Contains("ActionOptions", dialog, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedActionOption, Mode=TwoWay}\"", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedValue=\"{Binding SelectedAction, Mode=TwoWay}\"", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("保持（不切换）", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public void Resident_automation_log_uses_runtime_bounded_rotation()
    {
        var root = FindRepositoryRoot();
        var services = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Services", "AppServices.cs"));
        var listener = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Services", "RollingFileTraceListener.cs"));

        Assert.Contains("RollingFileTraceListener", services, StringComparison.Ordinal);
        Assert.Contains("archiveCount: 3", services, StringComparison.Ordinal);
        Assert.Contains("Rotate()", listener, StringComparison.Ordinal);
        Assert.Contains("Trace.AutoFlush = false", services, StringComparison.Ordinal);
        Assert.DoesNotContain("Trace.AutoFlush = true", services, StringComparison.Ordinal);
        Assert.DoesNotContain("File.WriteAllText(logPath, string.Empty)", services, StringComparison.Ordinal);
    }

    [Fact]
    public void Rules_page_exposes_edit_enable_and_delete_controls()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "RulesPage.xaml"));

        Assert.Contains("RuleEnabled_Toggled", xaml, StringComparison.Ordinal);
        Assert.Contains("EditRule_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("DeleteRule_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("编辑", xaml, StringComparison.Ordinal);
        Assert.Contains("删除", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Home_page_exposes_rule_source_and_target_observability()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "HomePage.xaml"));

        Assert.Contains("当前决策", xaml, StringComparison.Ordinal);
        Assert.Contains("RuleSourceLabel", xaml, StringComparison.Ordinal);
        Assert.Contains("RuleTargetLabel", xaml, StringComparison.Ordinal);
        Assert.Contains("CurrentRuleActionLabel", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Home_rule_editor_loads_the_complete_existing_rule()
    {
        var root = FindRepositoryRoot();
        var code = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "HomePage.xaml.cs"));

        Assert.Contains("existingRules: rules.SourceRules", code, StringComparison.Ordinal);
        Assert.Contains("picker.LoadExistingRule(existing)", code, StringComparison.Ordinal);
        Assert.Contains("picker.PreviewPriority", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Rules_page_exposes_non_mutating_rule_health_diagnostics()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "RulesPage.xaml"));

        Assert.Contains("InfoBar", xaml, StringComparison.Ordinal);
        Assert.Contains("规则健康检查", xaml, StringComparison.Ordinal);
        Assert.Contains("RuleDiagnosticsSummary", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Rule_dialogs_expose_advanced_match_preview_and_conflict_feedback()
    {
        var root = FindRepositoryRoot();
        var add = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "AddApplicationDialog.xaml"));
        var edit = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "EditRuleDialog.xaml"));
        var home = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "HomePage.xaml"));

        foreach (var xaml in new[] { add, edit })
        {
            Assert.Contains("高级匹配", xaml, StringComparison.Ordinal);
            Assert.Contains("WindowTitleContains", xaml, StringComparison.Ordinal);
            Assert.Contains("WindowClass", xaml, StringComparison.Ordinal);
            Assert.Contains("MatchPreview", xaml, StringComparison.Ordinal);
            Assert.Contains("ConflictPreview", xaml, StringComparison.Ordinal);
        }

        Assert.Contains("RuleMatchReason", home, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_text_uses_theme_brushes_instead_of_fractional_opacity()
    {
        var root = FindRepositoryRoot();
        var views = Directory.GetFiles(Path.Combine(root, "src", "FlowIME.App", "Views"), "*.xaml");

        foreach (var view in views)
        {
            var xaml = File.ReadAllText(view);
            Assert.DoesNotContain("Opacity=\"", xaml, StringComparison.Ordinal);
        }

        var home = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "HomePage.xaml"));
        var appXaml = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "App.xaml"));

        // R2 centralizes secondary page text in shared styles instead of repeating
        // the theme brush on every TextBlock. Keep the contract semantic: the
        // page must use the shared subtitle style, and that style must resolve
        // through the Windows theme brush.
        Assert.Contains("FlowPageSubtitleStyle", home, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"FlowPageSubtitleStyle\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("TextFillColorSecondaryBrush", appXaml, StringComparison.Ordinal);
        Assert.Contains("FlowTertiaryTextStyle", home, StringComparison.Ordinal);
    }

    [Fact]
    public void P7_shell_uses_adaptive_navigation_and_dpi_aware_initial_bounds()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "MainWindow.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "MainWindow.xaml.cs"));

        Assert.Contains("PaneDisplayMode=\"Auto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CompactModeThresholdWidth", xaml, StringComparison.Ordinal);
        Assert.Contains("ExpandedModeThresholdWidth", xaml, StringComparison.Ordinal);
        Assert.Contains("ApplyInitialWindowBounds", source, StringComparison.Ordinal);
        Assert.Contains("GetDpiForWindow", source, StringComparison.Ordinal);
        Assert.Contains("MoveAndResize", source, StringComparison.Ordinal);
    }

    [Fact]
    public void P7_rules_ui_exposes_priority_feedback_and_search_empty_state()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "RulesPage.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "RulesPage.xaml.cs"));

        Assert.Contains("应用规则始终拥有更高优先级", xaml, StringComparison.Ordinal);
        Assert.Contains("RuleFeedbackBar", xaml, StringComparison.Ordinal);
        Assert.Contains("NoSearchResultsState", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowFeedback", source, StringComparison.Ordinal);
        Assert.Contains("ShowFailure", source, StringComparison.Ordinal);
    }

    [Fact]
    public void P7_uses_shared_fluent_page_and_card_styles()
    {
        var root = FindRepositoryRoot();
        var appXaml = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "App.xaml"));

        Assert.Contains("FlowPageTitleStyle", appXaml, StringComparison.Ordinal);
        Assert.Contains("FlowPageSubtitleStyle", appXaml, StringComparison.Ordinal);
        Assert.Contains("FlowSectionTitleStyle", appXaml, StringComparison.Ordinal);
        Assert.Contains("FlowCardStyle", appXaml, StringComparison.Ordinal);
    }


    [Fact]
    public void P71_light_theme_uses_warm_beige_surfaces_without_exposing_provider_internals()
    {
        var root = FindRepositoryRoot();
        var appXaml = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "App.xaml"));
        var main = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "MainWindow.xaml"));
        var input = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "InputPage.xaml"));

        Assert.Contains("Themes/Brand.xaml", appXaml, StringComparison.Ordinal);
        var palette = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Themes", "Brand.xaml"));
        Assert.Contains("#F5F0E6", palette, StringComparison.Ordinal);
        Assert.Contains("#FCFAF5", palette, StringComparison.Ordinal);
        Assert.Contains("FlowAppBackgroundBrush", main, StringComparison.Ordinal);
        Assert.Contains("可用输入法", input, StringComparison.Ordinal);
        Assert.DoesNotContain("状态机制", input, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenStatus", input, StringComparison.Ordinal);
        Assert.DoesNotContain("ConversionMode", input, StringComparison.Ordinal);
    }


    [Fact]
    public void P72_pages_use_adaptive_layouts_and_wrapping_instead_of_desktop_only_trimming()
    {
        var root = FindRepositoryRoot();
        var viewRoot = Path.Combine(root, "src", "FlowIME.App", "Views");
        var responsiveViews = new[]
        {
            "HomePage.xaml",
            "RulesPage.xaml",
            "InputPage.xaml",
            "SettingsPage.xaml",
            "AboutPage.xaml"
        };

        foreach (var file in responsiveViews)
        {
            var xaml = File.ReadAllText(Path.Combine(viewRoot, file));
            Assert.Contains("AdaptiveTrigger", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("TextTrimming=\"CharacterEllipsis\"", xaml, StringComparison.Ordinal);
        }

        var home = File.ReadAllText(Path.Combine(viewRoot, "HomePage.xaml"));
        Assert.Contains("StructureStates", home, StringComparison.Ordinal);
        Assert.Contains("SpacingStates", home, StringComparison.Ordinal);
        Assert.Contains("DesktopStructure", home, StringComparison.Ordinal);
        Assert.Contains("WideSpacing", home, StringComparison.Ordinal);
        Assert.Contains("Grid.ColumnSpan", home, StringComparison.Ordinal);

        var rules = File.ReadAllText(Path.Combine(viewRoot, "RulesPage.xaml"));
        Assert.Contains("CompactRuleCard", rules, StringComparison.Ordinal);
        Assert.Contains("WideRuleCard", rules, StringComparison.Ordinal);
        Assert.Contains("RuleActions", rules, StringComparison.Ordinal);
    }

    [Fact]
    public void P72_dialogs_keep_rule_controls_visible_and_protect_low_height_layouts()
    {
        var root = FindRepositoryRoot();
        var viewRoot = Path.Combine(root, "src", "FlowIME.App", "Views");
        var add = File.ReadAllText(Path.Combine(viewRoot, "AddApplicationDialog.xaml"));
        var edit = File.ReadAllText(Path.Combine(viewRoot, "EditRuleDialog.xaml"));
        var global = File.ReadAllText(Path.Combine(viewRoot, "GlobalDefaultDialog.xaml"));

        // The add-rule flow uses a wide master-detail layout: applications stay in
        // the left column while settings for the selection stay in the right column.
        Assert.Contains("x:Name=\"ApplicationPickerColumn\"", add, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RuleSettingsColumn\"", add, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"1\"", add, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ContentDialogMinWidth\">900", add, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ContentDialogMaxWidth\">960", add, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"InputStateOptions\" Grid.Row=\"1\"", add, StringComparison.Ordinal);
        // Let the ContentDialog scroll expanded advanced settings on short screens.
        Assert.DoesNotContain("Target=\"DialogRoot.MaxHeight\"", add, StringComparison.Ordinal);
        Assert.Contains("Height=\"360\"", add, StringComparison.Ordinal);

        Assert.Contains("CompactWidth", edit, StringComparison.Ordinal);
        Assert.Contains("WideWidth", edit, StringComparison.Ordinal);
        Assert.Contains("CompactWidth", global, StringComparison.Ordinal);
        Assert.Contains("WideWidth", global, StringComparison.Ordinal);
    }


    [Fact]
    public void P721_page_triggers_live_on_the_root_child_and_compact_layout_reserves_navigation_space()
    {
        var root = FindRepositoryRoot();
        var viewRoot = Path.Combine(root, "src", "FlowIME.App", "Views");

        foreach (var file in new[] { "HomePage.xaml", "InputPage.xaml", "SettingsPage.xaml", "AboutPage.xaml" })
        {
            var xaml = File.ReadAllText(Path.Combine(viewRoot, file));
            var scrollViewerIndex = xaml.IndexOf("<ScrollViewer", StringComparison.Ordinal);
            var stateIndex = xaml.IndexOf("<VisualStateManager.VisualStateGroups>", StringComparison.Ordinal);
            var contentIndex = xaml.IndexOf("x:Name=\"PageContent\"", StringComparison.Ordinal);

            Assert.True(scrollViewerIndex >= 0);
            Assert.True(stateIndex > scrollViewerIndex);
            Assert.True(contentIndex > stateIndex);
            Assert.Contains("Value=\"16,20,16,28\"", xaml, StringComparison.Ordinal);
            Assert.Contains("MinWindowWidth=\"760\"", xaml, StringComparison.Ordinal);
        }

        var rules = File.ReadAllText(Path.Combine(viewRoot, "RulesPage.xaml"));
        Assert.Contains("Padding=\"16,20,16,28\"", rules, StringComparison.Ordinal);
        Assert.Contains("MinWindowWidth=\"760\"", rules, StringComparison.Ordinal);
    }

    [Fact]
    public void P722_rules_actions_share_aligned_action_groups()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "RulesPage.xaml"));

        Assert.Contains("x:Name=\"GlobalDefaultActions\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RuleActionPanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"GlobalDefaultActions.(Grid.Row)\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"RuleActionPanel.(Grid.Row)\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RuleToggle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Center\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void P72_shell_hides_nonessential_titlebar_subtitle_on_narrow_windows()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "MainWindow.xaml"));

        Assert.Contains("ShellRoot_SizeChanged", xaml, StringComparison.Ordinal);
        var source = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "MainWindow.xaml.cs"));
        Assert.Contains("AppTitleBar.Subtitle = args.NewSize.Width >= 680", source, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"48\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void P8B3_settings_expose_context_gated_gameplay_hotkey_protection()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "SettingsPage.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Services", "AppServices.cs"));

        Assert.Contains("游戏内阻止输入法快捷键", xaml, StringComparison.Ordinal);
        Assert.Contains("GameplayHotkeyGuardToggle", xaml, StringComparison.Ordinal);
        Assert.Contains("WinSpaceToggle", xaml, StringComparison.Ordinal);
        Assert.Contains("CtrlSpaceToggle", xaml, StringComparison.Ordinal);
        Assert.Contains("LegacyHotkeyToggle", xaml, StringComparison.Ordinal);
        Assert.Contains("new GameplayHotkeyGuard", source, StringComparison.Ordinal);
        Assert.Contains("InputContextSignalKind.GameTextEntry", source, StringComparison.Ordinal);
        Assert.Contains("gameplayHotkeyGuardArmed=", source, StringComparison.Ordinal);
        Assert.Contains("gameplayHotkeySuppressedCount=", source, StringComparison.Ordinal);
    }

    [Fact]
    public void P8B4_settings_expose_gameplay_us_keyboard_baseline()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "SettingsPage.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Services", "AppServices.cs"));

        Assert.Contains("游戏中使用美式键盘（US）", xaml, StringComparison.Ordinal);
        Assert.Contains("GameplayKeyboardBaselineToggle", xaml, StringComparison.Ordinal);
        Assert.Contains("new GameplayKeyboardBaseline", source, StringComparison.Ordinal);
        Assert.Contains("new GameplayKeyboardBaselinePolicy", source, StringComparison.Ordinal);
        Assert.Contains("gameplayKeyboardBaselineUsAvailable=", source, StringComparison.Ordinal);
        Assert.Contains("gameplayKeyboardBaselineOutcome=", source, StringComparison.Ordinal);
    }

    [Fact]
    public void P8B43_restores_desktop_rules_after_gameplay_and_hosts_non_activating_overlay()
    {
        var root = FindRepositoryRoot();
        var services = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Services", "AppServices.cs"));
        var overlay = File.ReadAllText(Path.Combine(root, "src", "FlowIME.Windows", "Input", "InputStatusOverlay.cs"));

        Assert.Contains("InputContextTrigger.GameplayExit", services, StringComparison.Ordinal);
        Assert.Contains("ScheduleGameplayExitRestore", services, StringComparison.Ordinal);
        Assert.Contains("gameplayExitRestoreCount=", services, StringComparison.Ordinal);
        Assert.Contains("inputStatusOverlayVisible=", services, StringComparison.Ordinal);
        Assert.Contains("WsExNoActivate", overlay, StringComparison.Ordinal);
        Assert.Contains("WsExTransparent", overlay, StringComparison.Ordinal);
        Assert.Contains("WsExToolWindow", overlay, StringComparison.Ordinal);
        Assert.Contains("MaNoActivate", overlay, StringComparison.Ordinal);
    }

    [Fact]
    public void P8B3_does_not_turn_gameplay_hotkey_guard_into_an_input_policy()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Services", "AppServices.cs"));

        Assert.DoesNotContain("GameplayInputPolicy", source, StringComparison.Ordinal);
        Assert.Contains("GameplayHotkeyGuard", source, StringComparison.Ordinal);
    }

    [Fact]
    public void P8C1_wires_game_text_entry_as_a_child_context_without_auto_activation()
    {
        var root = FindRepositoryRoot();
        var services = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Services", "AppServices.cs"));
        var engine = File.ReadAllText(Path.Combine(root, "src", "FlowIME.Core", "Context", "InputContextEngine.cs"));

        Assert.Contains("new GameTextEntryRuntimeDetector", services, StringComparison.Ordinal);
        Assert.Contains("new GameTextEntryPolicy", services, StringComparison.Ordinal);
        Assert.Contains("InputContextTrigger.GameTextEntryChanged", services, StringComparison.Ordinal);
        Assert.Contains("gameTextEntryActive=", services, StringComparison.Ordinal);
        Assert.Contains("!hasGame", engine, StringComparison.Ordinal);
        Assert.Contains("core.game-text-entry-derived", engine, StringComparison.Ordinal);
        Assert.Contains("TryEnterGameTextEntry(", services, StringComparison.Ordinal);
    }

    [Fact]
    public void P8C2_wires_standard_text_detection_persisted_profiles_and_non_blocking_hotkey_monitor()
    {
        var root = FindRepositoryRoot();
        var services = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Services", "AppServices.cs"));
        var settings = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "SettingsPage.xaml"));
        var monitor = File.ReadAllText(Path.Combine(root, "src", "FlowIME.Windows", "Input", "GameTextEntryHotkeyMonitor.cs"));

        Assert.Contains("new StandardGameTextEntryDetector", services, StringComparison.Ordinal);
        Assert.Contains("new GameTextEntryHotkeyMonitor", services, StringComparison.Ordinal);
        Assert.Contains("JsonGameTextEntryProfileRepository", services, StringComparison.Ordinal);
        Assert.Contains("游戏文字输入", settings, StringComparison.Ordinal);
        Assert.Contains("GameLibraryList", settings, StringComparison.Ordinal);
        Assert.Contains("SelectionMode=\"None\"", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxHeight=\"420\"", settings, StringComparison.Ordinal);
        Assert.Contains("GameLibrarySearchBox", settings, StringComparison.Ordinal);
        Assert.Contains("ScanGameLibraryButton", settings, StringComparison.Ordinal);
        Assert.Contains("AddGameTextEntryButton", settings, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource FlowPrimaryButtonStyle}\"", settings, StringComparison.Ordinal);
        Assert.Contains("ConfigureGameLibraryItemButton_Click", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfigureGameTextEntryButton", settings, StringComparison.Ordinal);
        Assert.Contains("Content=\"扫描\"", settings, StringComparison.Ordinal);
        Assert.Contains("添加游戏…", settings, StringComparison.Ordinal);
        Assert.Contains("Content=\"配置\"", settings, StringComparison.Ordinal);
        Assert.Contains("{Binding Icon}", settings, StringComparison.Ordinal);
        var settingsSource = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "SettingsPage.xaml.cs"));
        Assert.Contains("GameTextEntryTargetItemViewModel.Build", settingsSource, StringComparison.Ordinal);
        Assert.Contains("LocalGameLibraryScanner", settingsSource, StringComparison.Ordinal);
        Assert.Contains("LoadGameArtworkAsync", settingsSource, StringComparison.Ordinal);
        Assert.Contains("FileOpenPicker", settingsSource, StringComparison.Ordinal);
        Assert.Contains("ApplicationIdentity.FromRunningApplication", settingsSource, StringComparison.Ordinal);
        Assert.Contains("CallNextHookEx", monitor, StringComparison.Ordinal);
        Assert.DoesNotContain("return 1;", monitor, StringComparison.Ordinal);
    }

    [Fact]
    public void Game_text_entry_hook_is_started_only_when_a_hotkey_profile_exists()
    {
        var root = FindRepositoryRoot();
        var services = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Services", "AppServices.cs"));
        var startBody = services[
            services.IndexOf("public void Start()", StringComparison.Ordinal)..
            services.IndexOf("public async ValueTask SetAutomationEnabledAsync", StringComparison.Ordinal)];

        Assert.DoesNotContain("TryStartGameTextEntryHotkeyMonitor", startBody, StringComparison.Ordinal);
        Assert.Contains("EnsureGameTextEntryHotkeyMonitorStarted", services, StringComparison.Ordinal);
        Assert.Contains("GameTextEntryDetectionMode.HotkeyProfile", services, StringComparison.Ordinal);
    }

    [Fact]
    public void UiR2_promotes_gameplay_to_navigation_and_removes_input_support_from_primary_nav()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "MainWindow.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "MainWindow.xaml.cs"));

        Assert.Contains("Content=\"游戏\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"game\"", xaml, StringComparison.Ordinal);
        Assert.Contains("NavigationView.FooterMenuItems", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"输入法支持\"", xaml, StringComparison.Ordinal);
        Assert.Contains("NavigateToSection", source, StringComparison.Ordinal);
    }

    [Fact]
    public void UiR2_settings_and_game_share_one_page_without_duplicating_runtime_controls()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "SettingsPage.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "SettingsPage.xaml.cs"));

        Assert.Contains("x:Name=\"GeneralSection\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GameProtectionSection\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GameTextEntrySection\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ScrollViewer MaxWidth=", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PageContent\" MaxWidth=\"{StaticResource FlowPageContentMaxWidth}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("快捷键保护范围", xaml, StringComparison.Ordinal);
        Assert.Contains("OnNavigatedTo", source, StringComparison.Ordinal);
        Assert.Contains("ApplyViewMode", source, StringComparison.Ordinal);
    }

    [Fact]
    public void UiR2_game_text_entry_configuration_uses_a_dedicated_responsive_dialog()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "GameTextEntryDialog.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "GameTextEntryDialog.xaml.cs"));

        Assert.Contains("CompactWidth", xaml, StringComparison.Ordinal);
        Assert.Contains("WideWidth", xaml, StringComparison.Ordinal);
        Assert.Contains("StandardControlToggle", xaml, StringComparison.Ordinal);
        Assert.Contains("HotkeyProfileToggle", xaml, StringComparison.Ordinal);
        Assert.Contains("PreviewKeyDown=\"GestureBox_PreviewKeyDown\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"OpenEnterPreset\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"OpenCommaPreset\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"OpenPeriodPreset\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ExitEscapePreset\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PresetGestureButton_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("IsReadOnly=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ClearEnterGesturesButton_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("ClearExitGesturesButton_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("常用按键可直接多选", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("多个快捷键用英文逗号分隔", xaml, StringComparison.Ordinal);
        Assert.Contains("ValidationBar", xaml, StringComparison.Ordinal);
        Assert.Contains("internal GameTextEntryProfile? ResultProfile", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public GameTextEntryProfile? ResultProfile", source, StringComparison.Ordinal);
        Assert.Contains("GameTextEntryKeyGestureParser.FormatList", source, StringComparison.Ordinal);
        Assert.Contains("GestureBox_PreviewKeyDown", source, StringComparison.Ordinal);
    }

    [Fact]
    public void UiR2_home_surfaces_friendly_context_state_without_diagnostics_jargon()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "HomePage.xaml"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "ViewModels", "HomeViewModel.cs"));

        Assert.Contains("CurrentContextLabel", xaml, StringComparison.Ordinal);
        Assert.Contains("CurrentContextDescription", xaml, StringComparison.Ordinal);
        Assert.Contains("查看全部规则", xaml, StringComparison.Ordinal);
        Assert.Contains("游戏 · 文字输入", viewModel, StringComparison.Ordinal);
        Assert.Contains("全屏应用", viewModel, StringComparison.Ordinal);
    }


    [Fact]
    public void UiR3_separates_responsive_structure_from_spacing_so_wide_windows_do_not_reset_layout()
    {
        var root = FindRepositoryRoot();
        var viewRoot = Path.Combine(root, "src", "FlowIME.App", "Views");

        var home = File.ReadAllText(Path.Combine(viewRoot, "HomePage.xaml"));
        Assert.Contains("x:Name=\"StructureStates\"", home, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SpacingStates\"", home, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DesktopStructure\"", home, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"WideSpacing\"", home, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"MediumLayout\"", home, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"WideLayout\"", home, StringComparison.Ordinal);

        var rules = File.ReadAllText(Path.Combine(viewRoot, "RulesPage.xaml"));
        Assert.Contains("PageStructureStates", rules, StringComparison.Ordinal);
        Assert.Contains("PageSpacingStates", rules, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"WidePage\"", rules, StringComparison.Ordinal);

        var settings = File.ReadAllText(Path.Combine(viewRoot, "SettingsPage.xaml"));
        Assert.Contains("SettingsStructureStates", settings, StringComparison.Ordinal);
        Assert.Contains("SettingsSpacingStates", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"WideLayout\"", settings, StringComparison.Ordinal);

        var about = File.ReadAllText(Path.Combine(viewRoot, "AboutPage.xaml"));
        Assert.Contains("AboutStructureStates", about, StringComparison.Ordinal);
        Assert.Contains("AboutSpacingStates", about, StringComparison.Ordinal);
    }

    [Fact]
    public void UiR3_caps_desktop_content_width_instead_of_stretching_information_across_ultrawide_windows()
    {
        var root = FindRepositoryRoot();
        var appXaml = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "App.xaml"));
        var viewRoot = Path.Combine(root, "src", "FlowIME.App", "Views");

        Assert.Contains("FlowPageContentMaxWidth", appXaml, StringComparison.Ordinal);
        Assert.Contains("FlowSettingsContentMaxWidth", appXaml, StringComparison.Ordinal);

        foreach (var file in new[] { "HomePage.xaml", "RulesPage.xaml" })
        {
            var xaml = File.ReadAllText(Path.Combine(viewRoot, file));
            Assert.Contains("FlowPageContentMaxWidth", xaml, StringComparison.Ordinal);
        }

        foreach (var file in new[] { "AboutPage.xaml", "InputPage.xaml" })
        {
            var xaml = File.ReadAllText(Path.Combine(viewRoot, file));
            Assert.Contains("FlowSettingsContentMaxWidth", xaml, StringComparison.Ordinal);
        }

        var settings = File.ReadAllText(Path.Combine(viewRoot, "SettingsPage.xaml"));
        Assert.Contains("FlowPageContentMaxWidth", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void UiR33_search_fields_center_text_vertically()
    {
        var root = FindRepositoryRoot();
        var views = Path.Combine(root, "src", "FlowIME.App", "Views");
        var rules = File.ReadAllText(Path.Combine(views, "RulesPage.xaml"));
        var settings = File.ReadAllText(Path.Combine(views, "SettingsPage.xaml"));

        Assert.Contains("x:Name=\"SearchBox\"", rules, StringComparison.Ordinal);
        Assert.Contains("VerticalContentAlignment=\"Center\"", rules, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GameLibrarySearchBox\"", settings, StringComparison.Ordinal);
        Assert.Contains("VerticalContentAlignment=\"Center\"", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void UiR32_home_prioritizes_current_state_compact_decision_summary_and_inline_feedback()
    {
        var root = FindRepositoryRoot();
        var appXaml = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "App.xaml"));
        var home = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "HomePage.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "HomePage.xaml.cs"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "ViewModels", "HomeViewModel.cs"));

        Assert.Contains("HomeFeedbackBar", home, StringComparison.Ordinal);
        Assert.Contains("当前决策", home, StringComparison.Ordinal);
        Assert.Contains("FlowDecisionRowStyle", home, StringComparison.Ordinal);
        Assert.Contains("FlowDecisionValueStyle", appXaml, StringComparison.Ordinal);
        Assert.Contains("CurrentRuleActionLabel", home, StringComparison.Ordinal);
        Assert.Contains("CurrentRuleActionLabel", viewModel, StringComparison.Ordinal);
        Assert.Contains("查看全部规则", home, StringComparison.Ordinal);
        Assert.Contains("ShowFeedback", source, StringComparison.Ordinal);
        Assert.DoesNotContain("规则解析", home, StringComparison.Ordinal);
    }

    [Fact]
    public void UiR3_foundation_defines_command_hierarchy_and_ergonomic_target_sizes()
    {
        var root = FindRepositoryRoot();
        var appXaml = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "App.xaml"));
        var home = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "HomePage.xaml"));
        var rules = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "RulesPage.xaml"));

        Assert.Contains("x:Key=\"FlowControlMinHeight\">40", appXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"FlowIconButtonSize\">40", appXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"FlowPrimaryButtonStyle\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"FlowSecondaryButtonStyle\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"FlowSubtleButtonStyle\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("FlowPrimaryButtonStyle", home, StringComparison.Ordinal);
        Assert.Contains("FlowSubtleButtonStyle", home, StringComparison.Ordinal);
        Assert.Contains("FlowPrimaryButtonStyle", rules, StringComparison.Ordinal);
    }

    [Fact]
    public void R321_home_uses_the_confirmed_brand_asset_and_presentation_hierarchy()
    {
        var root = FindRepositoryRoot();
        var mainWindow = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "MainWindow.xaml"));
        var home = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "HomePage.xaml"));
        var project = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "FlowIME.App.csproj"));

        Assert.Contains("FlowIME.Logo.png", mainWindow, StringComparison.Ordinal);
        Assert.Contains("ImageIconSource", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Assets\\FlowIME.Logo.png", project, StringComparison.Ordinal);
        var logo = File.ReadAllBytes(Path.Combine(root, "src", "FlowIME.App", "Assets", "FlowIME.Logo.png"));
        Assert.Equal(new byte[] { 0, 0, 0, 44 }, logo[16..20]);
        Assert.Equal(new byte[] { 0, 0, 0, 44 }, logo[20..24]);
        Assert.Contains("x:Name=\"CurrentAppImage\"", home, StringComparison.Ordinal);
        Assert.Contains("CurrentInputHeadline", home, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"HeroDivider\"", home, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DecisionWhyButton\"", home, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DecisionExplanationPanel\"", home, StringComparison.Ordinal);
    }

    [Fact]
    public void Brand_icon_is_applied_to_executable_window_and_tray_surfaces()
    {
        var root = FindRepositoryRoot();
        var appRoot = Path.Combine(root, "src", "FlowIME.App");
        var project = File.ReadAllText(Path.Combine(appRoot, "FlowIME.App.csproj"));
        var mainWindow = File.ReadAllText(Path.Combine(appRoot, "MainWindow.xaml.cs"));
        var tray = File.ReadAllText(Path.Combine(appRoot, "Services", "TrayIconService.cs"));
        var iconPath = Path.Combine(appRoot, "Assets", "FlowIME.ico");

        Assert.Contains("<ApplicationIcon>Assets\\FlowIME.ico</ApplicationIcon>", project, StringComparison.Ordinal);
        Assert.Contains("Assets\\FlowIME.ico", project, StringComparison.Ordinal);
        Assert.Contains("_appWindow.SetIcon", mainWindow, StringComparison.Ordinal);
        Assert.Contains("FlowIME.ico", mainWindow, StringComparison.Ordinal);
        Assert.Contains("LoadImageW", tray, StringComparison.Ordinal);
        Assert.Contains("FlowIME.ico", tray, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadIconW(0, IdiApplication)", tray, StringComparison.Ordinal);

        var icon = File.ReadAllBytes(iconPath);
        Assert.True(icon.Length > 1024);
        Assert.Equal((byte)0, icon[0]);
        Assert.Equal((byte)0, icon[1]);
        Assert.Equal((byte)1, icon[2]);
        Assert.Equal((byte)0, icon[3]);
        Assert.True(BitConverter.ToUInt16(icon, 4) >= 6);
    }

    [Fact]
    public void R33_rules_use_whole_row_edit_overflow_actions_and_real_application_icons()
    {
        var root = FindRepositoryRoot();
        var appXaml = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "App.xaml"));
        var rules = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "RulesPage.xaml"));
        var source = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "Views", "RulesPage.xaml.cs"));
        var item = File.ReadAllText(Path.Combine(root, "src", "FlowIME.App", "ViewModels", "RuleListItemViewModel.cs"));

        Assert.Contains("FlowCardButtonStyle", appXaml, StringComparison.Ordinal);
        Assert.Contains("FlowRuleListItemStyle", appXaml, StringComparison.Ordinal);
        Assert.Contains("IsItemClickEnabled=\"True\"", rules, StringComparison.Ordinal);
        Assert.Contains("RulesList_ItemClick", rules, StringComparison.Ordinal);
        Assert.Contains("MenuFlyoutItem", rules, StringComparison.Ordinal);
        Assert.Contains("更多规则操作", rules, StringComparison.Ordinal);
        Assert.Contains("Source=\"{x:Bind Icon, Mode=OneWay}\"", rules, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{x:Bind IconVisibility, Mode=OneWay}\"", rules, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{x:Bind FallbackIconVisibility, Mode=OneWay}\"", rules, StringComparison.Ordinal);
        Assert.Contains("ExecutableName", rules, StringComparison.Ordinal);
        Assert.Contains("LoadRuleIconsAsync", source, StringComparison.Ordinal);
        Assert.Contains("ApplicationIconLoader", source, StringComparison.Ordinal);
        Assert.Contains("ImageSource? Icon", item, StringComparison.Ordinal);
        Assert.DoesNotContain("FontFamily=\"Cascadia Mono\"", rules, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "FlowIME.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the FlowIME repository root.");
    }
}
