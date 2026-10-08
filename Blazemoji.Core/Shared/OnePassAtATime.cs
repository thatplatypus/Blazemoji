namespace Blazemoji.Shared
{
    /// <summary>
    /// Runs work that brings one thing into line with another, such as an editor with the
    /// project it shows, so that it is never running twice at once and no caller waits behind
    /// another. A call that arrives during a pass asks for one more pass and returns at once.
    /// However many arrive, one more pass follows, and it sees things as they then are.
    /// </summary>
    /// <remarks>
    /// This is for work that is safe to repeat, which is what lets it do without a lock:
    /// nothing queues, so there is no turn to hand on and none to be left taken. Its callers
    /// must all be on one thread, as a component's are.
    /// </remarks>
    public sealed class OnePassAtATime(Func<Task> pass)
    {
        private bool _running;
        private bool _askedAgain;

        /// <summary>
        /// Makes a pass, or asks the pass that is running to be followed by another. The task
        /// of the caller that started the passes ends when the last of them has.
        /// </summary>
        public async Task RunAsync()
        {
            if (_running)
            {
                _askedAgain = true;
                return;
            }

            _running = true;
            try
            {
                do
                {
                    _askedAgain = false;
                    await pass();
                }
                while (_askedAgain);
            }
            finally
            {
                _running = false;
            }
        }
    }
}
