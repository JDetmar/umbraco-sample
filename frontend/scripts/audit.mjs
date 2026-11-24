// Vulnerability gate (ES-016, ES-051): fails on any npm advisory of moderate severity or above,
// except advisories assessed in the conformance ledger. Every exception names its ledger entry and expires.
import { execFileSync } from 'node:child_process';

const assessed = {
  'GHSA-3g3m-2fjr-6pw4': { ledger: 'DEV-006', expires: '2027-02-24' },
  'GHSA-g3hv-75wg-m249': { ledger: 'DEV-006', expires: '2027-02-24' },
};
const gated = new Set(['moderate', 'high', 'critical']);

let raw;
try {
  raw = execFileSync('npm', ['audit', '--json'], { encoding: 'utf8' });
} catch (error) {
  // npm audit exits non-zero when it finds anything; the JSON report is still on stdout.
  raw = error.stdout;
  if (!raw) throw error;
}

const report = JSON.parse(raw);
const today = new Date().toISOString().slice(0, 10);
const failures = [];

for (const [name, vulnerability] of Object.entries(report.vulnerabilities ?? {})) {
  for (const via of vulnerability.via) {
    if (typeof via !== 'object' || !gated.has(via.severity)) continue;
    const id = via.url?.split('/').pop() ?? String(via.source);
    const exception = assessed[id];
    if (!exception) {
      failures.push(`${name}: ${id} (${via.severity}) is not assessed in the ledger`);
    } else if (exception.expires < today) {
      failures.push(`${name}: ${id} exception (${exception.ledger}) expired on ${exception.expires}; review it`);
    } else {
      console.log(`allowed ${name}: ${id} (${via.severity}), assessed in ${exception.ledger} until ${exception.expires}`);
    }
  }
}

if (failures.length > 0) {
  console.error(failures.join('\n'));
  process.exit(1);
}
console.log('npm audit: no unassessed advisories of moderate severity or above.');
