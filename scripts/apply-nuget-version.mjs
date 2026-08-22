// Projects the changeset-computed version of the root placeholder package
// (bielu-microservices-orchestrator) onto the shared NuGet version.
//
// Run automatically by `npm run version` (i.e. right after `changeset version`). Idempotent:
// re-running with no version change is a no-op.
import { readFileSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const versionPropsPath = join(repoRoot, "version.props");

const newVersion = JSON.parse(
  readFileSync(join(repoRoot, "package.json"), "utf8"),
).version;

// A prerelease version (e.g. 1.0.1-beta.0) is split into
// <VersionPrefix>1.0.1</VersionPrefix> + <VersionSuffix>beta.0</VersionSuffix>; the .NET SDK
// recombines them at build/pack time. A stable version clears the suffix.
const dash = newVersion.indexOf("-");
const versionPrefix = dash === -1 ? newVersion : newVersion.slice(0, dash);
const versionSuffix = dash === -1 ? "" : newVersion.slice(dash + 1);

const versionProps = readFileSync(versionPropsPath, "utf8");
if (!/<VersionPrefix>[^<]*<\/VersionPrefix>/.test(versionProps)) {
  throw new Error("Could not find <VersionPrefix> in version.props to update.");
}
if (!/<VersionSuffix>[^<]*<\/VersionSuffix>/.test(versionProps)) {
  throw new Error("Could not find <VersionSuffix> in version.props to update.");
}
const updatedProps = versionProps
  .replace(/(<VersionPrefix>)[^<]*(<\/VersionPrefix>)/, `$1${versionPrefix}$2`)
  .replace(/(<VersionSuffix>)[^<]*(<\/VersionSuffix>)/, `$1${versionSuffix}$2`);
writeFileSync(versionPropsPath, updatedProps);
console.log(
  `version.props <VersionPrefix> -> ${versionPrefix}` +
    (versionSuffix ? `, <VersionSuffix> -> ${versionSuffix}` : ", <VersionSuffix> cleared"),
);
