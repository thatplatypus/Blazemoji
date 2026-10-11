# Test packages

`hello/` is Packmoji's own sample, `samples/hello-pkg`, copied byte for byte from the commit
of Packmoji that `packmoji.pin` names. Two packages and the two files of an application that
depends on them:

- `words` is Emojicode alone.
- `greeter` depends on `words`, has a C++ file, and names a library to link.
- `app/packmoji.lock` holds the SHA-256 of each package as `pmj pack` makes it.

Do not edit these files. `pmj pack` gives the same bytes for the same files, and the tests
hold a packed package to the digest the lockfile has. When `packmoji.pin` moves to a commit
whose sample or whose archive format differs, copy the sample again, and pack the two
packages again for `Blazemoji.Toolchain.ContractTests/Packages`.
