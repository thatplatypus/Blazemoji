using Blazemoji.Models.Library;

namespace Blazemoji.Shared.State
{
    public class LocalStorageFiles
    {
        private List<EmojicFile> _localStorageFiles = new();

        public List<EmojicFile> Files
        {
            get => _localStorageFiles;
            set
            {
                _localStorageFiles = value;
                LocalStorageStateChanged?.Invoke(nameof(LocalStorageFiles), default);
            }
        }

        public event EventHandler LocalStorageStateChanged;
    }
}
