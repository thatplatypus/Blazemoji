using Microsoft.Extensions.Options;

namespace Blazemoji.Toolchain.Service
{
    /// <summary>
    /// Bounds how many compiles run at once. A compile is the most expensive thing the service
    /// does, and unlike a run it has no slot to wait for, so one beyond the limit is refused.
    /// </summary>
    public sealed class CompileGate(IOptions<ToolchainServiceOptions> options)
    {
        private int _active;

        public bool TryEnter()
        {
            while (true)
            {
                var active = Volatile.Read(ref _active);
                if (active >= options.Value.MaxConcurrentCompiles)
                    return false;

                if (Interlocked.CompareExchange(ref _active, active + 1, active) == active)
                    return true;
            }
        }

        public void Leave() => Interlocked.Decrement(ref _active);
    }
}
