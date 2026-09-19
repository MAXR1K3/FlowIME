namespace FlowIME.App.Services;

internal static class LaunchOptions
{
    internal const string BackgroundArgument = "--background";

    internal static bool ShouldStartHidden(IEnumerable<string> arguments) =>
        arguments.Any(argument =>
            string.Equals(
                argument,
                BackgroundArgument,
                StringComparison.OrdinalIgnoreCase));
}
