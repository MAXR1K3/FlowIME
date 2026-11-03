using FlowIME.Core.Automation;
using FlowIME.Core.Models;

namespace FlowIME.Core.Tests.Automation;

public sealed class ApplicationModeMemoryTests
{
    [Fact]
    public void Disabled_memory_records_nothing()
    {
        var memory = new ApplicationModeMemory();

        Assert.False(memory.Record("path:code", InputMode.English));
        Assert.Null(memory.TryGet("path:code"));
    }

    [Fact]
    public void Only_definite_modes_are_recorded()
    {
        var memory = Enabled();
        memory.Record("path:code", InputMode.English);

        Assert.False(memory.Record("path:code", InputMode.Unknown));
        Assert.Equal(InputMode.English, memory.TryGet("PATH:CODE"));
    }

    [Fact]
    public void Disabling_forgets_every_application()
    {
        var memory = Enabled();
        memory.Record("path:code", InputMode.English);

        memory.SetEnabled(false);
        memory.SetEnabled(true);

        Assert.Null(memory.TryGet("path:code"));
    }

    [Fact]
    public void Oldest_application_is_evicted_when_full()
    {
        var memory = Enabled();
        for (var index = 0; index < ApplicationModeMemory.MaxApplications; index++)
        {
            memory.Record($"path:{index}", InputMode.Chinese);
        }

        memory.Record("path:0", InputMode.English);
        memory.Record("path:new", InputMode.English);

        Assert.Equal(ApplicationModeMemory.MaxApplications, memory.Count);
        Assert.Null(memory.TryGet("path:1"));
        Assert.Equal(InputMode.English, memory.TryGet("path:0"));
        Assert.Equal(InputMode.English, memory.TryGet("path:new"));
    }

    private static ApplicationModeMemory Enabled()
    {
        var memory = new ApplicationModeMemory();
        memory.SetEnabled(true);
        return memory;
    }
}
