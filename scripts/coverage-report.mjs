// Usage: node scripts/coverage-report.mjs <report directory> <floors json>
// Prints the coverage summary as Markdown (and appends it to the GitHub step summary in CI), then fails only if
// an assembly with a floor is below it (ES-002: coverage is diagnostic; floors only on critical modules).
import { appendFileSync, readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { checkFloors, mergeReports, parseCobertura, summarise, toMarkdown } from './coverage-policy.mjs';

const [directory, floorsFile] = process.argv.slice(2);
if (!directory || !floorsFile) {
  console.error('Usage: node scripts/coverage-report.mjs <report directory> <floors json>');
  process.exit(2);
}

const files = readdirSync(directory).filter((name) => name.includes('.cobertura.') && name.endsWith('.xml'));
if (files.length === 0) {
  console.error(`No Cobertura reports in ${directory}.`);
  process.exit(1);
}

const floors = JSON.parse(readFileSync(floorsFile, 'utf8')).floors;
const summary = summarise(mergeReports(files.map((name) => parseCobertura(readFileSync(join(directory, name), 'utf8')))));
const markdown = toMarkdown(summary, floors);
console.log(markdown);
if (process.env.GITHUB_STEP_SUMMARY) {
  appendFileSync(process.env.GITHUB_STEP_SUMMARY, markdown);
}

const failures = checkFloors(summary, floors);
for (const failure of failures) {
  console.error(failure);
}

process.exit(failures.length === 0 ? 0 : 1);
