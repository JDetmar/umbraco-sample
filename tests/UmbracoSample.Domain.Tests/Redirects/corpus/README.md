# Redirect parser corpus (ES-007)

Retained regression cases for `RedirectMap.Parse`. `CorpusTests` runs every file on every build:

- `valid-*.csv` must parse with no errors.
- `invalid-*.csv` must be rejected with at least one error.

When an input breaks the parser, or a property test fails, add it here before you fix it:

1. CsCheck prints the failing (shrunk) input and a seed. Re-run that property with `seed: "<seed>"` to reproduce it.
2. Save the input as `valid-<what>.csv` or `invalid-<what>.csv`, named for the behaviour it pins.
3. Confirm `CorpusTests` fails, fix the parser, and confirm it passes.

The files are bytes on disk and are read with `File.ReadAllText`, which removes a UTF-8 byte order mark. `Parse` also ignores one, as tested in `RedirectMapTests`.
