using Blazemoji.Services.Projects;
using Blazemoji.Services.Settings;
using Microsoft.Extensions.Options;

namespace Blazemoji.Test.Settings
{
    public sealed class FileSettingsStoreTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "blazemoji-tests", "settings-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }

        private FileSettingsStore Create() =>
            new(Options.Create(new FileProjectStoreOptions { Root = _root }), TimeProvider.System);

        private string SettingsFile => Path.Combine(_root, ".blazemoji", "settings.json");

        [Fact]
        public async Task With_nothing_kept_there_is_nothing_and_no_folder_is_made_by_looking()
        {
            (await Create().LoadAsync()).ShouldBeNull();
            Directory.Exists(_root).ShouldBeFalse();
        }

        [Fact]
        public async Task What_was_kept_is_read_back_by_another_store_on_the_same_folder()
        {
            await Create().SaveAsync("{ \"editor\": { \"fontSize\": 16 } }\n");

            (await Create().LoadAsync()).ShouldBe("{ \"editor\": { \"fontSize\": 16 } }\n");
        }

        [Fact]
        public async Task It_is_one_file_in_the_apps_own_folder_beside_where_the_layout_goes()
        {
            await Create().SaveAsync("{}\n");

            File.Exists(SettingsFile).ShouldBeTrue();
            Directory.GetFiles(Path.GetDirectoryName(SettingsFile)!).ShouldBe([SettingsFile]);
        }

        [Fact]
        public async Task A_later_save_takes_the_place_of_the_earlier_one()
        {
            var store = Create();
            await store.SaveAsync("first");

            await store.SaveAsync("second");

            (await Create().LoadAsync()).ShouldBe("second");
        }

        [Fact]
        public async Task A_folder_that_cannot_be_written_to_is_reported_as_the_store_failing()
        {
            // A file where the projects folder should be: nothing can be made under it.
            Directory.CreateDirectory(Path.GetDirectoryName(_root)!);
            await File.WriteAllTextAsync(_root, "in the way");
            try
            {
                await Should.ThrowAsync<ProjectStoreException>(() => Create().SaveAsync("{}\n"));
            }
            finally
            {
                File.Delete(_root);
            }
        }
    }
}
