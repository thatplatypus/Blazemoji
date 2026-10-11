using Blazemoji.Toolchain.Service;
using Microsoft.AspNetCore.Http;

namespace Blazemoji.Test.Service
{
    public sealed class RequestBodyReadingTests
    {
        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        [Fact]
        public async Task A_body_within_the_limit_is_read_whole()
        {
            var bytes = Enumerable.Range(0, 100_000).Select(index => (byte)index).ToArray();

            var read = await ToolchainEndpoints.ReadBodyAsync(new MemoryStream(bytes), null, 100_000, Cancellation);

            read.ShouldBe(bytes);
        }

        [Fact]
        public async Task A_body_whose_length_says_it_is_over_the_limit_is_too_long_without_being_read()
        {
            var read = await ToolchainEndpoints.ReadBodyAsync(new FailingStream(0, new InvalidOperationException("read")), 11, 10, Cancellation);

            read.ShouldBeNull();
        }

        [Fact]
        public async Task A_body_of_unknown_length_that_runs_over_the_limit_is_too_long()
        {
            var read = await ToolchainEndpoints.ReadBodyAsync(new MemoryStream(new byte[100_000]), null, 99_999, Cancellation);

            read.ShouldBeNull();
        }

        [Fact]
        public async Task A_body_the_server_cuts_off_at_its_own_limit_is_too_long()
        {
            var stream = new FailingStream(10, new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge));

            var read = await ToolchainEndpoints.ReadBodyAsync(stream, null, 1000, Cancellation);

            read.ShouldBeNull();
        }

        [Fact]
        public async Task Any_other_bad_request_from_the_server_is_not_swallowed()
        {
            var stream = new FailingStream(10, new BadHttpRequestException("Unexpected end of request content.", StatusCodes.Status400BadRequest));

            await Should.ThrowAsync<BadHttpRequestException>(() => ToolchainEndpoints.ReadBodyAsync(stream, null, 1000, Cancellation));
        }

        private sealed class FailingStream(int bytesFirst, Exception failure) : Stream
        {
            private bool _gaveFirst;

            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                if (bytesFirst == 0 || _gaveFirst)
                    throw failure;

                _gaveFirst = true;
                return ValueTask.FromResult(bytesFirst);
            }

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
