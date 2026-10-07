using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace Blazemoji.Toolchain.Service
{
    public sealed class RunSession(IToolchainRun run)
    {
        public string RunId => run.RunId;

        public IToolchainRun Run => run;

        public EventLog Log { get; } = new();

        public DateTimeOffset? EndedAt { get; internal set; }
    }

    /// <summary>
    /// The runs the service currently knows about, and the queue that hands new ones to the pump.
    /// </summary>
    public sealed class RunRegistry(IOptions<ToolchainServiceOptions> options)
    {
        private readonly ConcurrentDictionary<string, RunSession> _sessions = new();
        private readonly Channel<RunSession> _started = Channel.CreateUnbounded<RunSession>(new UnboundedChannelOptions { SingleReader = true });
        private int _active;

        public ChannelReader<RunSession> Started => _started.Reader;

        /// <summary>
        /// Claims one of the concurrent run slots. The claim is given back by
        /// <see cref="MarkEnded"/> once the run ends, or by <see cref="ReleaseReservation"/> if it never starts.
        /// </summary>
        public bool TryReserve()
        {
            while (true)
            {
                var active = Volatile.Read(ref _active);
                if (active >= options.Value.MaxConcurrentRuns)
                    return false;

                if (Interlocked.CompareExchange(ref _active, active + 1, active) == active)
                    return true;
            }
        }

        public void ReleaseReservation() => Interlocked.Decrement(ref _active);

        public RunSession Add(IToolchainRun run)
        {
            var session = new RunSession(run);
            _sessions[session.RunId] = session;
            _started.Writer.TryWrite(session);
            return session;
        }

        public RunSession? Find(string runId) => _sessions.GetValueOrDefault(runId);

        public void MarkEnded(RunSession session, DateTimeOffset at)
        {
            session.EndedAt = at;
            ReleaseReservation();
        }

        public IReadOnlyList<RunSession> RemoveExpired(DateTimeOffset now, TimeSpan retention)
        {
            var expired = new List<RunSession>();
            foreach (var session in _sessions.Values)
            {
                if (session.EndedAt is { } endedAt && now - endedAt >= retention && _sessions.TryRemove(session.RunId, out _))
                    expired.Add(session);
            }

            return expired;
        }

        /// <summary>
        /// Finished runs stay readable so that a slow client can still fetch their output, but
        /// only so many and only so much: runs can finish far faster than they expire. The
        /// ones that ended longest ago go first. The one that ended last always stays, since
        /// its client has had the least time to read it. Running programs are not counted;
        /// <see cref="TryReserve"/> bounds those.
        /// </summary>
        public IReadOnlyList<RunSession> RemoveOverBudget()
        {
            var ended = _sessions.Values
                .Where(session => session.EndedAt is not null)
                .OrderBy(session => session.EndedAt)
                .ToList();

            var bytes = ended.Sum(session => session.Log.ApproximateBytes);
            var dropped = new List<RunSession>();

            foreach (var session in ended.Take(ended.Count - 1))
            {
                var overCount = ended.Count - dropped.Count > options.Value.MaxRetainedRuns;
                var overBytes = bytes > options.Value.MaxRetainedBytes;
                if (!overCount && !overBytes)
                    break;

                if (_sessions.TryRemove(session.RunId, out _))
                {
                    dropped.Add(session);
                    bytes -= session.Log.ApproximateBytes;
                }
            }

            return dropped;
        }

        public IReadOnlyList<RunSession> RemoveAll()
        {
            var all = _sessions.Values.ToList();
            _sessions.Clear();
            return all;
        }
    }
}
