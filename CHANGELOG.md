# Changelog

## 1.0.1

### Patch Changes

- [#18](https://github.com/bielu/bielu.microservices.orchestrator/pull/18) [`343c47a`](https://github.com/bielu/bielu.microservices.orchestrator/commit/343c47acdbdc814225f5aae674fba6363d141209) Thanks [@github-actions](https://github.com/apps/github-actions)! - Update vendored containerd gRPC proto files from `v2.3.4` to `v2.4.2`.

- [#35](https://github.com/bielu/bielu.microservices.orchestrator/pull/35) [`65cb685`](https://github.com/bielu/bielu.microservices.orchestrator/commit/65cb68570880835687b7570b0a40f4bbec168bf4) Thanks [@bielu](https://github.com/bielu)! - containerd: `StopAsync` with a timeout now escalates to SIGKILL when the grace period expires. With default `Grpc.Net.Client` settings a cancelled call surfaces as `RpcException(StatusCode.Cancelled)`, not `OperationCanceledException`, so previously the stop failed with that exception instead of killing the task. `ConnectAsync`/`DisconnectAsync` on the containerd network manager now send the `containerd-namespace` header when resolving the task's network namespace, so they target the configured namespace like every other call.

- [#26](https://github.com/bielu/bielu.microservices.orchestrator/pull/26) [`be64864`](https://github.com/bielu/bielu.microservices.orchestrator/commit/be6486424e2452325bbb85ae6f334d09830bf0a6) Thanks [@bielu](https://github.com/bielu)! - Publish documentation at https://orchestration.bielu.pl/ and use it as the NuGet project URL.

- [#24](https://github.com/bielu/bielu.microservices.orchestrator/pull/24) [`8976322`](https://github.com/bielu/bielu.microservices.orchestrator/commit/8976322567a14c9e89f6e259a2c32617e4dc8519) Thanks [@bielu](https://github.com/bielu)! - Ship XML documentation, Source Link and `.snupkg` symbol packages with every NuGet package, build deterministically on CI, and include the license file and SPDX metadata in each package.

- [#38](https://github.com/bielu/bielu.microservices.orchestrator/pull/38) [`2a5f878`](https://github.com/bielu/bielu.microservices.orchestrator/commit/2a5f8780852ccdd9e932a087a50cd21e006fd0ac) Thanks [@bielu](https://github.com/bielu)! - Every package now has an icon on nuget.org: a control plane wired to three containers, drawn in the same style as the other bielu package icons.

## 1.0.0

### Major Changes

- [#12](https://github.com/bielu/bielu.microservices.orchestrator/pull/12) [`9d1bbf1`](https://github.com/bielu/bielu.microservices.orchestrator/commit/9d1bbf1dfb9e11111a1a20e16199f49e02b772f8) Thanks [@bielu](https://github.com/bielu)! - Promote the suite to the first stable `1.0.0` release. Every package (`Bielu.Microservices.Orchestrator`, `.Containerd`, `.Docker`, `.Kubernetes`, `.Podman`, `.OpenTelemetry`, `.HealthChecks`, `.Storage.EfCore`, `.Storage.File`) has so far only been published to the `beta` pre-release channel on NuGet.org; this marks the public API as stable.

### Patch Changes

- [#12](https://github.com/bielu/bielu.microservices.orchestrator/pull/12) [`9d1bbf1`](https://github.com/bielu/bielu.microservices.orchestrator/commit/9d1bbf1dfb9e11111a1a20e16199f49e02b772f8) Thanks [@bielu](https://github.com/bielu)! - Update vendored containerd gRPC proto files from `v2.2.3` to `v2.3.4`.

All notable changes to this project will be documented in this file. Versions and this file are
managed by [changesets](https://github.com/changesets/changesets) — see
[`.changeset/README.md`](.changeset/README.md) to add one.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
