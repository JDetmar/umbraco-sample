import { test } from 'node:test';
import assert from 'node:assert/strict';
import { evaluateAudit } from '../scripts/audit-policy.mjs';

const today = '2026-11-24';
const advisory = (severity, id = 'GHSA-aaaa-bbbb-cccc') => ({
  vulnerabilities: { pkg: { via: [{ severity, url: `https://github.com/advisories/${id}` }] } },
});

test('a clean report passes', () => {
  assert.deepEqual(evaluateAudit({ vulnerabilities: {} }, {}, today).failures, []);
});

test('a registry error fails closed', () => {
  const report = { message: 'request to registry failed', error: { code: 'ENOTFOUND', summary: 'getaddrinfo ENOTFOUND' } };
  assert.equal(evaluateAudit(report, {}, today).failures.length, 1);
});

test('a report without a vulnerabilities section fails closed', () => {
  assert.equal(evaluateAudit({ auditReportVersion: 2 }, {}, today).failures.length, 1);
  assert.equal(evaluateAudit(null, {}, today).failures.length, 1);
});

test('moderate and above fail unless assessed; low passes', () => {
  assert.equal(evaluateAudit(advisory('moderate'), {}, today).failures.length, 1);
  assert.equal(evaluateAudit(advisory('critical'), {}, today).failures.length, 1);
  assert.equal(evaluateAudit(advisory('low'), {}, today).failures.length, 0);
});

test('an assessed advisory passes until its exception expires', () => {
  const assessed = { 'GHSA-aaaa-bbbb-cccc': { ledger: 'DEV-999', expires: '2026-12-01' } };
  assert.equal(evaluateAudit(advisory('high'), assessed, today).failures.length, 0);
  assert.equal(evaluateAudit(advisory('high'), assessed, '2026-12-02').failures.length, 1);
});
