using Blazemoji.Toolchain;

namespace Blazemoji.Shared.State
{
    public sealed record RunSummary(int? ExitCode, RunEndReason Reason, TimeSpan Duration);
}
