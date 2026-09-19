using FlowIME.Core.Context;
using FlowIME.Core.Models;

namespace FlowIME.Windows.Context;

/// <summary>
/// Collects one optional, privacy-safe gameplay fact from Windows. Probes must be
/// read-only and failure-tolerant; GameplayEligibilityDetector isolates failures so
/// one unavailable Windows subsystem cannot break foreground automation.
/// </summary>
internal interface IGameplayEvidenceProbe
{
    string Id { get; }

    int Order { get; }

    GameplayEvidence? Capture(WindowContext window);
}
