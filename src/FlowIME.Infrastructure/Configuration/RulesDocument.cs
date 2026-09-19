using FlowIME.Core.Rules;

namespace FlowIME.Infrastructure.Configuration;

internal sealed record RulesDocument(
    int SchemaVersion,
    List<ApplicationRule> Rules,
    GlobalDefaultTarget? DefaultTarget = null)
{
    public const int LegacySchemaVersion = 1;
    public const int CurrentSchemaVersion = 2;

    public static RulesDocument Create(
        IReadOnlyList<ApplicationRule> rules,
        GlobalDefaultTarget? defaultTarget = null) =>
        new(CurrentSchemaVersion, [.. rules], defaultTarget);
}
