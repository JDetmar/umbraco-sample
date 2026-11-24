// Bundle-size budget (TST-03): fails the build when the compressed stylesheet grows past the budget.
import { readFileSync } from 'node:fs';
import { gzipSync } from 'node:zlib';

const budgetBytes = 10 * 1024;
const file = new URL('../../src/UmbracoSample.Web/wwwroot/dist/site.css', import.meta.url);
const size = gzipSync(readFileSync(file)).length;

console.log(`site.css: ${size} bytes gzipped (budget ${budgetBytes})`);
if (size > budgetBytes) {
  console.error(`site.css is over its budget by ${size - budgetBytes} bytes.`);
  process.exit(1);
}
