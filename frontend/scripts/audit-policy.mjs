// Pure policy for the npm vulnerability gate, kept separate from the npm call so it can be tested.
// It fails closed: a report without a vulnerabilities section (for example a registry error) is a failure.

const gated = new Set(['moderate', 'high', 'critical']);

/**
 * @param {unknown} report Parsed `npm audit --json` output.
 * @param {Record<string, {ledger: string, expires: string}>} assessed Exceptions by advisory ID.
 * @param {string} today ISO date (yyyy-mm-dd).
 * @returns {{failures: string[], allowed: string[]}}
 */
export function evaluateAudit(report, assessed, today) {
  if (report === null || typeof report !== 'object') {
    return { failures: ['npm audit did not return a JSON object'], allowed: [] };
  }
  if ('error' in report && report.error) {
    const summary = report.error.summary ?? report.message ?? 'unknown error';
    return { failures: [`npm audit failed: ${summary}`], allowed: [] };
  }
  if (!report.vulnerabilities || typeof report.vulnerabilities !== 'object') {
    return { failures: ['npm audit report has no vulnerabilities section; treating it as a failure'], allowed: [] };
  }

  const failures = [];
  const allowed = [];
  for (const [name, vulnerability] of Object.entries(report.vulnerabilities)) {
    for (const via of vulnerability.via ?? []) {
      if (typeof via !== 'object' || !gated.has(via.severity)) continue;
      const id = via.url?.split('/').pop() ?? String(via.source);
      const exception = assessed[id];
      if (!exception) {
        failures.push(`${name}: ${id} (${via.severity}) is not assessed in the ledger`);
      } else if (exception.expires < today) {
        failures.push(`${name}: ${id} exception (${exception.ledger}) expired on ${exception.expires}; review it`);
      } else {
        allowed.push(`${name}: ${id} (${via.severity}), assessed in ${exception.ledger} until ${exception.expires}`);
      }
    }
  }
  return { failures, allowed };
}
