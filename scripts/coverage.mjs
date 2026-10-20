// Summarises the Cobertura reports written by `dotnet test` (coverlet.runsettings turns collection on
// for every run) and optionally enforces the floors in coverage-thresholds.json.
//
//   node scripts/coverage.mjs [dir-or-file ...] [--check] [--uncovered <path-fragment>]
//
// With no paths it searches TestResults/ folders under the repository. Reports from several test
// projects are merged per source line, so a line counts as covered when any test project hit it.
// The per-assembly table goes to stdout and, on GitHub Actions, to the job summary.
//
//   --check                 exit 1 when total line or branch coverage is below the thresholds.
//   --uncovered <fragment>  list the uncovered line ranges of every file whose path contains <fragment>.
import { readFileSync, readdirSync, statSync, appendFileSync, existsSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join, resolve } from "node:path";

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const REPORT_NAME = "coverage.cobertura.xml";

const args = process.argv.slice(2);
const check = args.includes("--check");
const uncoveredIndex = args.indexOf("--uncovered");
const uncoveredFilter = uncoveredIndex >= 0 ? args[uncoveredIndex + 1] : undefined;
const inputs = args.filter((arg, i) => !arg.startsWith("--") && (uncoveredIndex < 0 || i !== uncoveredIndex + 1));

function findReports(path, found = []) {
  const stats = statSync(path);
  if (stats.isFile()) {
    if (path.endsWith(".xml")) found.push(path);
    return found;
  }
  for (const entry of readdirSync(path)) {
    if (entry === "node_modules" || entry === ".git" || entry === "bin" || entry === "obj") continue;
    const child = join(path, entry);
    if (statSync(child).isDirectory()) findReports(child, found);
    else if (entry === REPORT_NAME) found.push(child);
  }
  return found;
}

function defaultReports() {
  const found = [];
  const walk = (dir, depth) => {
    for (const entry of readdirSync(dir)) {
      if (entry === "node_modules" || entry === ".git" || entry === "bin" || entry === "obj") continue;
      const child = join(dir, entry);
      if (!statSync(child).isDirectory()) continue;
      if (entry === "TestResults") findReports(child, found);
      else if (depth < 3) walk(child, depth + 1);
    }
  };
  walk(repoRoot, 0);
  return found;
}

const reports = (inputs.length ? inputs.map((p) => resolve(p)) : [])
  .flatMap((p) => (existsSync(p) ? findReports(p) : []));
if (!inputs.length) reports.push(...defaultReports());
if (!reports.length) {
  console.error("No Cobertura reports found. Run `dotnet test` first.");
  process.exit(1);
}

// file -> { assembly, lines: Map<line, { hits, branchesCovered, branchesTotal }> }
const files = new Map();

// Coverlet writes paths relative to the report's <source> root (src/ here) or, with several roots,
// absolute ones; key on the part below src/ so reports from different machines or checkouts merge.
const normalise = (filename) => filename.replace(/\\/g, "/").replace(/^.*?\/src\//, "src/");

for (const report of reports) {
  const xml = readFileSync(report, "utf8");
  for (const pkg of xml.matchAll(/<package name="([^"]+)"[\s\S]*?<\/package>/g)) {
    const assembly = pkg[1];
    for (const cls of pkg[0].matchAll(/<class [^>]*filename="([^"]+)"[\s\S]*?<\/class>/g)) {
      const filename = normalise(cls[1]);
      const file = files.get(filename) ?? { assembly, lines: new Map() };
      files.set(filename, file);
      for (const line of cls[0].matchAll(/<line number="(\d+)" hits="(\d+)"([^>]*)>/g)) {
        const number = Number(line[1]);
        const hits = Number(line[2]);
        const condition = /condition-coverage="[^"(]*\((\d+)\/(\d+)\)"/.exec(line[3]);
        const covered = condition ? Number(condition[1]) : 0;
        const total = condition ? Number(condition[2]) : 0;
        const existing = file.lines.get(number);
        if (existing) {
          existing.hits += hits;
          existing.branchesCovered = Math.max(existing.branchesCovered, covered);
          existing.branchesTotal = Math.max(existing.branchesTotal, total);
        } else {
          file.lines.set(number, { hits, branchesCovered: covered, branchesTotal: total });
        }
      }
    }
  }
}

const empty = () => ({ lines: 0, linesCovered: 0, branches: 0, branchesCovered: 0 });
const add = (acc, line) => {
  acc.lines++;
  if (line.hits > 0) acc.linesCovered++;
  acc.branches += line.branchesTotal;
  acc.branchesCovered += line.branchesCovered;
};

const total = empty();
const assemblies = new Map();
for (const file of files.values()) {
  const acc = assemblies.get(file.assembly) ?? empty();
  assemblies.set(file.assembly, acc);
  for (const line of file.lines.values()) {
    add(acc, line);
    add(total, line);
  }
}

const percent = (covered, valid) => (valid === 0 ? 100 : (100 * covered) / valid);
const fmt = (covered, valid) => `${percent(covered, valid).toFixed(1)}%`;

const rows = [...assemblies.entries()].sort(([a], [b]) => a.localeCompare(b));
const table = [
  "| Assembly | Lines | Line coverage | Branch coverage |",
  "|---|---:|---:|---:|",
  ...rows.map(([name, acc]) =>
    `| ${name} | ${acc.linesCovered} / ${acc.lines} | ${fmt(acc.linesCovered, acc.lines)} | ${fmt(acc.branchesCovered, acc.branches)} |`),
  `| **Total** | **${total.linesCovered} / ${total.lines}** | **${fmt(total.linesCovered, total.lines)}** | **${fmt(total.branchesCovered, total.branches)}** |`,
].join("\n");

const thresholdsPath = join(repoRoot, "coverage-thresholds.json");
const thresholds = existsSync(thresholdsPath) ? JSON.parse(readFileSync(thresholdsPath, "utf8")) : {};
const lineRate = percent(total.linesCovered, total.lines);
const branchRate = percent(total.branchesCovered, total.branches);

const failures = [];
if (typeof thresholds.line === "number" && lineRate < thresholds.line) {
  failures.push(`line coverage ${lineRate.toFixed(1)}% is below the ${thresholds.line}% floor`);
}
if (typeof thresholds.branch === "number" && branchRate < thresholds.branch) {
  failures.push(`branch coverage ${branchRate.toFixed(1)}% is below the ${thresholds.branch}% floor`);
}

const summary = [
  "## Code coverage",
  "",
  table,
  "",
  `Floors from \`coverage-thresholds.json\`: line ${thresholds.line ?? "n/a"}%, branch ${thresholds.branch ?? "n/a"}%.`,
  "",
].join("\n");

console.log(summary);
if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, summary + "\n");

if (uncoveredFilter) {
  for (const [filename, file] of [...files.entries()].sort(([a], [b]) => a.localeCompare(b))) {
    if (!filename.includes(uncoveredFilter)) continue;
    const missed = [...file.lines.entries()]
      .filter(([, line]) => line.hits === 0 || line.branchesCovered < line.branchesTotal)
      .map(([number, line]) => ({ number, partial: line.hits > 0 }))
      .sort((a, b) => a.number - b.number);
    if (!missed.length) continue;
    const ranges = [];
    for (const { number, partial } of missed) {
      const last = ranges.at(-1);
      if (last && !partial && !last.partial && number === last.end + 1) last.end = number;
      else ranges.push({ start: number, end: number, partial });
    }
    const text = ranges.map((r) => (r.partial ? `${r.start}(branch)` : r.start === r.end ? `${r.start}` : `${r.start}-${r.end}`));
    console.log(`${filename}: ${text.join(", ")}`);
  }
}

if (check) {
  if (failures.length) {
    for (const failure of failures) console.error(`::error::Coverage gate: ${failure}.`);
    process.exit(1);
  }
  // A floor that trails the measured value lets coverage slip back unnoticed; nudge it up.
  if (lineRate - (thresholds.line ?? 0) >= 2 || branchRate - (thresholds.branch ?? 0) >= 2) {
    console.log(`::notice::Coverage is ${lineRate.toFixed(1)}% line / ${branchRate.toFixed(1)}% branch; consider raising the floors in coverage-thresholds.json.`);
  }
}
