namespace Blazemoji.Desktop.Smoke;

/// <summary>
/// How a smoke run was asked for. The names are Hermes's own, so that whatever starts a
/// Hermes app for a smoke test starts this one the same way.
/// </summary>
/// <param name="ResultPath">Where to write the run's result as JSON, if anywhere.</param>
/// <param name="ExitWhenDone">Close the app once the result is out. Off only for looking at a run by hand.</param>
/// <param name="AlsoCompile">Compile and run a program as well. Needs a toolchain service, which a build machine may not have.</param>
public sealed record SmokeSettings(bool IsEnabled, TimeSpan Timeout, string? ResultPath, bool ExitWhenDone, bool AlsoCompile)
{
    public const string Enabled = "HERMES_SMOKE_TEST";
    public const string TimeoutSeconds = "HERMES_SMOKE_TEST_TIMEOUT";
    public const string Result = "HERMES_SMOKE_TEST_RESULT";
    public const string Exit = "HERMES_SMOKE_TEST_EXIT";
    public const string Compile = "BLAZEMOJI_SMOKE_COMPILE";

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    public static SmokeSettings FromEnvironment() => From(Environment.GetEnvironmentVariable);

    public static SmokeSettings From(Func<string, string?> variable)
    {
        var timeout = int.TryParse(variable(TimeoutSeconds), out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(Math.Min(seconds, 86400))
            : DefaultTimeout;
        var resultPath = variable(Result);

        return new SmokeSettings(
            variable(Enabled) == "1",
            timeout,
            string.IsNullOrWhiteSpace(resultPath) ? null : resultPath,
            variable(Exit) != "0",
            variable(Compile) == "1");
    }
}
