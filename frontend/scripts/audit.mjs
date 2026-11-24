// Vulnerability gate (ES-016, ES-051): fails on any npm advisory of moderate severity or above,
// except advisories assessed in the conformance ledger, and fails if the audit itself cannot run.
// Every exception names its ledger entry and expires. There are none today.
import { execFileSync } from 'node:child_process';
import { evaluateAudit } from './audit-policy.mjs';

/** @type {Record<string, {ledger: string, expires: string}>} */
const assessed = {};

let raw;
try {
  raw = execFileSync('npm', ['audit', '--json'], { encoding: 'utf8' });
} catch (error) {
  // npm audit exits non-zero both when it finds advisories and when it fails; the policy tells them apart.
  raw = error.stdout;
  if (!raw) throw error;
}

let report;
try {
  report = JSON.parse(raw);
} catch {
  console.error('npm audit did not print JSON:\n' + raw);
  process.exit(1);
}

const { failures, allowed } = evaluateAudit(report, assessed, new Date().toISOString().slice(0, 10));
for (const line of allowed) console.log(`allowed ${line}`);
if (failures.length > 0) {
  console.error(failures.join('\n'));
  process.exit(1);
}
console.log('npm audit: no unassessed advisories of moderate severity or above.');
