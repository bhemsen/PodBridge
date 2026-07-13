# Release notes fragments

Each `<version>.md` here is the curated **What's changed** section for one
release, written by `/loopkit:ship` from the commits since the previous tag and
committed in that release's `chore(release):` commit. The tag-triggered
`.github/workflows/release.yml` reads the file matching the release version and
inserts it into the GitHub Release notes.

- **Filename = the bare version, no leading `v`** — e.g. tag `v1.1.0` ->
  `docs/release-notes/1.1.0.md` (it must match `release.yml`'s v-stripped
  `needs.version.outputs.version`). A `v`-prefixed or misnamed file is silently
  ignored: CI ships only the fixed notes, with no error.
- **Open the file with an `### What's changed` heading** (h3) so it sits level
  with the fixed `### Scope` / `### First run` sections under the single
  `## PodBridge <tag>` release title.

This is a per-release fragment, **not** a running `CHANGELOG.md`. See
`docs/release.md` for the full release contract.
