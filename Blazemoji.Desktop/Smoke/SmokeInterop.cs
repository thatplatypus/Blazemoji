using Microsoft.JSInterop;

namespace Blazemoji.Desktop.Smoke;

/// <summary>The checks the page runs on itself for a smoke run, in the host's own script.</summary>
public sealed class SmokeInterop(IJSRuntime js)
{
    private const string ModulePath = "./js/smoke.js";
    private const string RunFunction = "run";

    public async Task<IReadOnlyList<SmokeCheckOutcome>> RunAsync(bool alsoCompile)
    {
        await using var module = await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
        return await module.InvokeAsync<SmokeCheckOutcome[]>(RunFunction, alsoCompile);
    }
}
