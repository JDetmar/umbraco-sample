import { test } from 'node:test';
import assert from 'node:assert/strict';
import { checkFloors, mergeReports, parseCobertura, summarise, toMarkdown } from '../coverage-policy.mjs';

const report = (pkg, lines) => `<?xml version="1.0"?>
<coverage line-rate="0" branch-rate="0"><packages><package name="${pkg}"><classes>
<class name="${pkg}.A" filename="${pkg}/Area/A.cs"><methods><method name="M"><lines>
${lines.map(([n, hits]) => `<line number="${n}" hits="${hits}" branch="False" />`).join('')}
</lines></method></methods><lines>
${lines.map(([n, hits, cond]) => `<line number="${n}" hits="${hits}" branch="${cond ? 'True' : 'False'}"${cond ? ` condition-coverage="x (${cond})"` : ''} />`).join('')}
</lines></class></classes></package></packages></coverage>`;

test('lines are counted once, not again from their methods', () => {
  const [row] = summarise(parseCobertura(report('P', [[1, 1], [2, 0]])));
  assert.equal(row.lines, 2);
  assert.equal(row.covered, 1);
  assert.equal(row.line, 50);
});

test('branches come from condition coverage', () => {
  const [row] = summarise(parseCobertura(report('P', [[1, 1, '1/2'], [2, 1, '2/2']])));
  assert.equal(row.branches, 4);
  assert.equal(row.branchesHit, 3);
  assert.equal(row.branch, 75);
});

test('a line covered by any report counts as covered', () => {
  const merged = mergeReports([parseCobertura(report('P', [[1, 0]])), parseCobertura(report('P', [[1, 3]]))]);
  assert.equal(summarise(merged)[0].covered, 1);
});

test('rows are split by top-level folder inside the assembly', () => {
  const [row] = summarise(parseCobertura(report('P', [[1, 1]])));
  assert.deepEqual(row.areas.map((area) => area.area), ['Area']);
});

test('a module below its floor fails; one at or above passes', () => {
  const summary = summarise(parseCobertura(report('P', [[1, 1], [2, 1], [3, 1], [4, 0]])));
  assert.equal(checkFloors(summary, { P: { line: 75 } }).length, 0);
  assert.match(checkFloors(summary, { P: { line: 80 } })[0], /P: line coverage 75.0% is below its floor of 80%/);
});

test('a module without a floor never fails, however low', () => {
  const summary = summarise(parseCobertura(report('P', [[1, 0]])));
  assert.deepEqual(checkFloors(summary, {}), []);
});

test('a floor on a module with no report fails closed', () => {
  assert.equal(checkFloors([], { Missing: { line: 50 } }).length, 1);
});

test('something that is not a Cobertura report is refused', () => {
  assert.throws(() => parseCobertura('<html></html>'));
  assert.throws(() => parseCobertura(undefined));
});

test('the markdown names the floor for each assembly', () => {
  const markdown = toMarkdown(summarise(parseCobertura(report('P', [[1, 1]]))), { P: { line: 90 } });
  assert.match(markdown, /\| \*\*P\*\* \| 100\.0% \(1\/1\) \| n\/a \| line 90% \|/);
});

test('file names relative to different sources are the same file', () => {
  const withSource = (source, filename, hits) => `<coverage><sources><source>${source}</source></sources><packages><package name="P"><classes>
<class name="P.A" filename="${filename}"><lines><line number="1" hits="${hits}" /></lines></class></classes></package></packages></coverage>`;
  const merged = mergeReports([
    parseCobertura(withSource('/repo/src/P/Area/', 'A.cs', 0)),
    parseCobertura(withSource('/repo/src/', 'P/Area/A.cs', 2)),
  ]);
  const [row] = summarise(merged);
  assert.equal(row.lines, 1);
  assert.equal(row.covered, 1);
  assert.deepEqual(row.areas.map((area) => area.area), ['Area']);
});
