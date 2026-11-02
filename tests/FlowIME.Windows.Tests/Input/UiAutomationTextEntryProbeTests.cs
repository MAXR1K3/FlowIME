using System.Windows.Automation;
using FlowIME.Core.Abstractions;
using FlowIME.Windows.Input;

namespace FlowIME.Windows.Tests.Input;

public sealed class UiAutomationTextEntryProbeTests
{
    [Fact]
    public void Writable_edit_is_a_text_box()
    {
        Assert.Equal(
            TextEntryPresence.Present,
            UiAutomationTextEntryProbe.Classify(ControlType.Edit, isEnabled: true, isReadOnly: false));
    }

    [Fact]
    public void Edit_without_value_pattern_is_a_text_box()
    {
        // Qt chat inputs and contenteditable editors often omit ValuePattern.
        Assert.Equal(
            TextEntryPresence.Present,
            UiAutomationTextEntryProbe.Classify(ControlType.Edit, isEnabled: true, isReadOnly: null));
    }

    [Fact]
    public void Read_only_browser_page_is_not_a_text_box()
    {
        Assert.Equal(
            TextEntryPresence.Absent,
            UiAutomationTextEntryProbe.Classify(ControlType.Document, isEnabled: true, isReadOnly: true));
    }

    [Fact]
    public void Disabled_edit_is_not_a_text_box()
    {
        Assert.Equal(
            TextEntryPresence.Absent,
            UiAutomationTextEntryProbe.Classify(ControlType.Edit, isEnabled: false, isReadOnly: false));
    }

    [Fact]
    public void Qt_group_with_writable_value_pattern_is_not_a_text_box()
    {
        Assert.Equal(
            TextEntryPresence.Absent,
            UiAutomationTextEntryProbe.Classify(ControlType.Group, isEnabled: true, isReadOnly: false));
    }

    [Fact]
    public void Chat_list_item_is_not_a_text_box()
    {
        Assert.Equal(
            TextEntryPresence.Absent,
            UiAutomationTextEntryProbe.Classify(ControlType.ListItem, isEnabled: true, isReadOnly: false));
    }

    [Fact]
    public void Custom_control_is_unknown()
    {
        Assert.Equal(
            TextEntryPresence.Unknown,
            UiAutomationTextEntryProbe.Classify(ControlType.Custom, isEnabled: true, isReadOnly: null));
    }
}
