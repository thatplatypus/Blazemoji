using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Blazemoji.Desktop.Smoke;

/// <param name="Error">Null for a check that passed.</param>
public sealed record SmokeCheckOutcome(string Name, bool Passed, long DurationMs, string? Error);

/// <summary>
/// One smoke run, reported the way Hermes reports one: single lines on standard output that
/// start with <c>HERMES_SMOKE_</c>, the result as JSON where asked, and exit code 0 only for
/// a pass. Hermes does this itself from 1.3.0. This host is on 1.2.0, the last release for
/// .NET 10, so it says the same things in the same words, and the same tools can judge it.
/// </summary>
public sealed class SmokeSession(SmokeSettings settings) : IDisposable
{
    private const string AppName = "Blazemoji";

    private readonly Stopwatch _running = Stopwatch.StartNew();
    private readonly Lock _finishing = new();
    private Timer? _watchdog;
    private bool _finished;

    public bool IsEnabled => settings.IsEnabled;

    public bool AlsoCompile => settings.AlsoCompile;

    /// <summary>
    /// Says the run has begun, and from then on gives the page only so long to report. A
    /// window that never shows its page would otherwise be a run that never ends.
    /// </summary>
    public void Start()
    {
        if (!settings.IsEnabled)
            return;

        Say($"HERMES_SMOKE_START: {AppName} {Version} {Platform} {RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}");
        _watchdog = new Timer(_ => Finish([], timedOutWaitingFor: "the page to report"), null, settings.Timeout, Timeout.InfiniteTimeSpan);
    }

    public void Report(IReadOnlyList<SmokeCheckOutcome> checks) => Finish(checks, timedOutWaitingFor: null);

    private void Finish(IReadOnlyList<SmokeCheckOutcome> checks, string? timedOutWaitingFor)
    {
        lock (_finishing)
        {
            if (_finished)
                return;

            _finished = true;
        }

        _watchdog?.Dispose();
        foreach (var check in checks)
        {
            Say(check.Passed
                ? $"HERMES_SMOKE_CHECK_PASS: {check.Name} {check.DurationMs}ms"
                : $"HERMES_SMOKE_CHECK_FAIL: {check.Name} {check.DurationMs}ms - {SingleLine(check.Error ?? "Not run")}");
        }

        var failed = checks.Count(check => !check.Passed);
        var passed = failed == 0 && checks.Count > 0 && timedOutWaitingFor is null;
        if (settings.ResultPath is { } path)
            WriteResult(path, checks, passed, timedOutWaitingFor);

        Say(passed
            ? $"HERMES_SMOKE_RESULT: PASSED ({checks.Count} checks)"
            : $"HERMES_SMOKE_RESULT: FAILED ({failed}/{checks.Count} checks failed, 0 errors{(timedOutWaitingFor is null ? string.Empty : $", timed out waiting for {timedOutWaitingFor}")})");

        if (settings.ExitWhenDone)
            Environment.Exit(passed ? 0 : 1);
    }

    private void WriteResult(string path, IReadOnlyList<SmokeCheckOutcome> checks, bool passed, string? timedOutWaitingFor)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema", 1);
            writer.WriteString("app", AppName);
            writer.WriteString("version", Version);
            writer.WriteString("platform", Platform);
            writer.WriteString("architecture", RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant());
            writer.WriteString("result", passed ? "passed" : "failed");
            writer.WriteNumber("durationMs", _running.ElapsedMilliseconds);
            writer.WriteString("timedOutWaitingFor", timedOutWaitingFor);
            writer.WriteStartArray("milestones");
            writer.WriteEndArray();
            writer.WriteStartArray("checks");
            foreach (var check in checks)
            {
                writer.WriteStartObject();
                writer.WriteString("name", check.Name);
                writer.WriteString("status", check.Passed ? "passed" : "failed");
                writer.WriteNumber("durationMs", check.DurationMs);
                writer.WriteString("error", check.Error);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("errors");
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        try
        {
            if (Path.GetDirectoryName(Path.GetFullPath(path)) is { } folder)
                Directory.CreateDirectory(folder);

            File.WriteAllBytes(path, buffer.ToArray());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The lines on standard output still carry the verdict.
            Say($"HERMES_SMOKE_WARNING: {SingleLine("The result could not be written: " + exception.GetType().Name)}");
        }
    }

    private static string Version =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    private static string Platform =>
        OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";

    private static string SingleLine(string text)
    {
        var end = text.IndexOfAny(['\r', '\n']);
        return (end < 0 ? text : text[..end]).Trim();
    }

    private static void Say(string line)
    {
        Console.Out.WriteLine(line);
        Console.Out.Flush();
    }

    public void Dispose() => _watchdog?.Dispose();
}
