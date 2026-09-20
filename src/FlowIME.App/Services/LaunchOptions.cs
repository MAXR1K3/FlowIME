namespace FlowIME.App.Services;

internal static class LaunchOptions
{
    internal const string BackgroundArgument = "--background";
    internal const string ShutdownArgument = "--shutdown";

    internal static bool ShouldStartHidden(IEnumerable<string> arguments) =>
        arguments.Any(argument =>
            string.Equals(
                argument,
                BackgroundArgument,
                StringComparison.OrdinalIgnoreCase));

    internal static bool ShouldRequestExit(IEnumerable<string> arguments) =>
        arguments.Any(argument =>
            string.Equals(
                argument,
                ShutdownArgument,
                StringComparison.OrdinalIgnoreCase));
}
