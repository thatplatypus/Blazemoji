namespace Blazemoji.Test
{
    /// <summary>
    /// For tests that allocate heavily. Under amd64 emulation, a garbage-collection-heavy test
    /// running beside tests that start processes stalls both for around 20 seconds (measured
    /// 2026-10-07: 1.6 s alone, 22 s in parallel), which breaks the tests that measure timing.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class RunsAloneCollection
    {
        public const string Name = "Runs alone";
    }
}
