// Coverage is a diagnostic (ES-002): reported for every assembly, with a floor only on critical modules.
// Pure functions over Cobertura XML from coverlet, so the policy is tested without running coverage.

const attribute = (tag, name) => {
  const match = tag.match(new RegExp(`\\b${name}="([^"]*)"`));
  return match ? match[1] : undefined;
};

// Returns Map<package, Map<"file:line", { hit, branches, branchesHit }>>. Lines are taken from each class's own
// <lines>, not from its <methods>, which repeat them; a line seen twice keeps the better result. File names are
// resolved against the report's <source>, because each report states them relative to a different root.
export function parseCobertura(xml) {
  if (typeof xml !== 'string' || !/<coverage\b/.test(xml)) {
    throw new Error('Not a Cobertura report.');
  }

  const sources = [...xml.matchAll(/<source>([^<]*)<\/source>/g)].map((source) => source[1].replaceAll('\\', '/'));
  const resolve = (file) => {
    const path = file.replaceAll('\\', '/');
    return sources.length === 1 && !path.startsWith('/') && !/^[A-Za-z]:\//.test(path)
      ? `${sources[0].replace(/\/?$/, '/')}${path}`
      : path;
  };

  const packages = new Map();
  for (const pkg of xml.matchAll(/<package\b([^>]*)>([\s\S]*?)<\/package>/g)) {
    const name = attribute(pkg[1], 'name');
    const lines = packages.get(name) ?? new Map();
    packages.set(name, lines);

    for (const cls of pkg[2].matchAll(/<class\b([^>]*)>([\s\S]*?)<\/class>/g)) {
      const file = resolve(attribute(cls[1], 'filename'));
      const own = cls[2].replace(/<methods>[\s\S]*?<\/methods>/g, '');
      for (const line of own.matchAll(/<line\b([^>]*?)\/?>/g)) {
        const key = `${file}:${attribute(line[1], 'number')}`;
        const hit = Number(attribute(line[1], 'hits')) > 0;
        const condition = attribute(line[1], 'condition-coverage')?.match(/\((\d+)\/(\d+)\)/);
        const result = {
          hit,
          branchesHit: condition ? Number(condition[1]) : 0,
          branches: condition ? Number(condition[2]) : 0,
        };
        const previous = lines.get(key);
        lines.set(key, previous
          ? { hit: previous.hit || result.hit, branchesHit: Math.max(previous.branchesHit, result.branchesHit), branches: Math.max(previous.branches, result.branches) }
          : result);
      }
    }
  }

  return packages;
}

export function mergeReports(reports) {
  const merged = new Map();
  for (const report of reports) {
    for (const [name, lines] of report) {
      const target = merged.get(name) ?? new Map();
      merged.set(name, target);
      for (const [key, result] of lines) {
        const previous = target.get(key);
        target.set(key, previous
          ? { hit: previous.hit || result.hit, branchesHit: Math.max(previous.branchesHit, result.branchesHit), branches: Math.max(previous.branches, result.branches) }
          : result);
      }
    }
  }

  return merged;
}

const percent = (covered, total) => (total === 0 ? 100 : (100 * covered) / total);

function totals(lines) {
  let covered = 0;
  let branchesHit = 0;
  let branches = 0;
  for (const line of lines) {
    covered += line.hit ? 1 : 0;
    branchesHit += line.branchesHit;
    branches += line.branches;
  }

  return { lines: lines.length, covered, branches, branchesHit, line: percent(covered, lines.length), branch: percent(branchesHit, branches) };
}

// One row per package, and one per top-level folder inside it, so a reader sees where the uncovered code is.
export function summarise(packages) {
  return [...packages].sort(([a], [b]) => a.localeCompare(b)).map(([name, lines]) => {
    const areas = new Map();
    for (const [key, result] of lines) {
      // The area is the folder under the project folder (named like the assembly): src/<assembly>/<area>/...
      const parts = key.slice(0, key.lastIndexOf(':')).split('/');
      const below = parts.slice(parts.lastIndexOf(name) + 1);
      const area = parts.includes(name) && below.length > 1 ? below[0] : '(root)';
      areas.set(area, [...(areas.get(area) ?? []), result]);
    }

    return {
      name,
      ...totals([...lines.values()]),
      areas: [...areas].sort(([a], [b]) => a.localeCompare(b)).map(([area, results]) => ({ area, ...totals(results) })),
    };
  });
}

// floors: { "<package>": { "line": 95, "branch": 90 } }. A floor on a package with no report fails closed.
export function checkFloors(summary, floors) {
  const failures = [];
  for (const [name, floor] of Object.entries(floors ?? {})) {
    const row = summary.find((entry) => entry.name === name);
    if (!row) {
      failures.push(`${name}: no coverage report, but it has a floor.`);
      continue;
    }

    for (const kind of ['line', 'branch']) {
      if (floor[kind] !== undefined && row[kind] < floor[kind]) {
        failures.push(`${name}: ${kind} coverage ${row[kind].toFixed(1)}% is below its floor of ${floor[kind]}%.`);
      }
    }
  }

  return failures;
}

export function toMarkdown(summary, floors) {
  const rate = (value, covered, total) => (total === 0 ? 'n/a' : `${value.toFixed(1)}% (${covered}/${total})`);
  const cell = (row) => `${rate(row.line, row.covered, row.lines)} | ${rate(row.branch, row.branchesHit, row.branches)}`;
  const out = [
    '### Coverage of the fast tests (diagnostic, ES-002)',
    '',
    'Integration and end-to-end tests run the site out of process and are not counted. Only assemblies with a floor can fail the build.',
    '',
    '| Assembly / area | Lines | Branches | Floor |',
    '|---|---|---|---|',
  ];
  for (const row of summary) {
    const floor = floors?.[row.name];
    const floorText = floor ? Object.entries(floor).map(([kind, value]) => `${kind} ${value}%`).join(', ') : 'none';
    out.push(`| **${row.name}** | ${cell(row)} | ${floorText} |`);
    for (const area of row.areas) {
      out.push(`| &nbsp;&nbsp;${area.area} | ${cell(area)} | |`);
    }
  }

  return `${out.join('\n')}\n`;
}
