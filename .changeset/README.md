# Changesets

This folder is managed by [changesets](https://github.com/changesets/changesets). Every pull
request that changes a shipped package should include a **changeset** — a small markdown file that
records what changed and how the version should bump. CI aggregates these into an automated
"Version Packages" PR that bumps the version and updates the changelog.

## Add a changeset

```bash
npx changeset
```

Pick the `bielu-microservices-orchestrator` package and a bump level, then write a short,
user-facing summary. Every NuGet package in this repository (`Bielu.Microservices.Orchestrator`,
`.Containerd`, `.Docker`, `.Kubernetes`, `.Podman`, `.OpenTelemetry`, `.HealthChecks`,
`.Storage.EfCore`, `.Storage.File`, ...) shares one version via `version.props`, so one changeset
entry covers the whole suite regardless of which package(s) the change actually touched.

Bump levels follow semver:

- **patch** — bug fixes, docs, internal changes with no API impact.
- **minor** — new, backward-compatible features.
- **major** — breaking changes.

Chore-only PRs (CI, formatting, tests) can record an empty changeset:

```bash
npx changeset add --empty
```

## How the NuGet version is applied

Changesets is npm-native, so the NuGet suite is represented by the root `package.json`
(`bielu-microservices-orchestrator`, private, never published). After `changeset version` runs,
`scripts/apply-nuget-version.mjs` copies that package's new version into `version.props`
`<VersionPrefix>`/`<VersionSuffix>`. `changeset version` itself writes the release notes straight
into the root `CHANGELOG.md`.
