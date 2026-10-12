namespace Blazemoji.Shared.State
{
    /// <summary>
    /// How many archives this host is handing to the toolchain at this moment, for everyone
    /// using it. The toolchain holds an archive whole in memory while it arrives, and a host
    /// is the only thing that sends it one, so the limit is kept here. One for the host.
    /// </summary>
    /// <remarks>
    /// It refuses and does not queue. Whoever is refused waits a moment and asks again, which
    /// needs no lock that a window's own thread could be left holding.
    /// </remarks>
    public sealed class PackageUploads
    {
        public const int MostAtOnce = 2;

        private int _underWay;

        public bool TryBegin()
        {
            if (Interlocked.Increment(ref _underWay) <= MostAtOnce)
                return true;

            Interlocked.Decrement(ref _underWay);
            return false;
        }

        public void End() => Interlocked.Decrement(ref _underWay);
    }
}
