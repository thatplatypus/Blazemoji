using Blazemoji.Services.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Blazemoji.Test.Projects
{
    public sealed class FileSamplesTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "blazemoji-tests", "samples-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }

        private FileSamples Create(SampleOptions? options = null) =>
            new(Options.Create(options ?? new SampleOptions { Path = _root }), NullLogger<FileSamples>.Instance);

        private void Write(string name, string content)
        {
            Directory.CreateDirectory(_root);
            File.WriteAllText(Path.Combine(_root, name), content);
        }

        [Fact]
        public async Task Every_file_in_the_folder_is_a_sample_under_its_own_name_and_they_come_in_the_order_of_their_names()
        {
            Write("Loop.🍇", "🏁 🍇 🔁 👍 🍇 🍉 🍉");
            Write("Hello.🍇", "🏁 🍇 😀 🔤Hello🔤❗️ 🍉");

            var samples = await Create().AllAsync();

            samples.Select(sample => sample.Name).ShouldBe(["Hello.🍇", "Loop.🍇"]);
            samples[0].Code.ShouldBe("🏁 🍇 😀 🔤Hello🔤❗️ 🍉");
        }

        [Fact]
        public async Task A_mark_at_the_start_of_a_file_that_says_how_it_is_encoded_is_not_part_of_the_sample()
        {
            Write("Hello.🍇", "﻿🏁 🍇 🍉");

            (await Create().AllAsync()).Single().Code.ShouldBe("🏁 🍇 🍉");
        }

        [Fact]
        public async Task A_folder_that_is_not_there_has_no_samples()
        {
            (await Create().AllAsync()).ShouldBeEmpty();
        }

        [Fact]
        public async Task The_samples_that_ship_are_looked_for_beside_the_app_and_not_where_it_was_started_from()
        {
            Path.IsPathRooted(new SampleOptions().Path).ShouldBeTrue();
            new SampleOptions().Path.ShouldStartWith(AppContext.BaseDirectory);

            var samples = await Create(new SampleOptions()).AllAsync();

            samples.Select(sample => sample.Name).ShouldContain("HelloWorld.🍇");
            samples.Count.ShouldBeGreaterThanOrEqualTo(10);
        }
    }
}
