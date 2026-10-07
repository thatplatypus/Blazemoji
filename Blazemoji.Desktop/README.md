# Desktop spike

A throwaway Hermes window around the editor, to find out what `Blazemoji.Components` needs
from a desktop host. It is not for merging: it borrows source files from the web host by
link, and `Workspace.razor` on this branch carries a workaround marked `SPIKE ONLY`.

It targets .NET 10 with Hermes.Blazor pinned at 1.2.0, the last release built for .NET 10
(1.3.0 and later are .NET 11 only), so it builds with the repository's own SDK.

To run it (macOS, Docker running):

```sh
scripts/dev-toolchain.sh          # the compiler, in its container, on 127.0.0.1:5290
cd Blazemoji.Desktop
dotnet run
```

`BLAZEMOJI_TOOLCHAIN` points it at a toolchain service somewhere else.

With `BLAZEMOJI_SPIKE_REPORT=/some/file.json` set, the page looks at itself from the inside
(Monaco, style sheets, typing, completion, a compile and run, local storage, dark mode, the
clipboard) and writes what it found to that file. `BLAZEMOJI_SPIKE_QUIT=1` closes the window
when it has.
