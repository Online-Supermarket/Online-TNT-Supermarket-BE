import { spawn } from 'node:child_process';
import { mkdir, writeFile, rm } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
process.chdir(fileURLToPath(new URL('.', import.meta.url)));
await mkdir('reports', { recursive: true });
// Prevent stale reports from being mistaken for a new run after setup failure.
for (const path of ['reports/junit.xml', 'reports/summary.md', 'reports/summary.json']) await rm(path, { force: true });
await rm('reports/screenshots', { recursive: true, force: true });
const publicOnly = process.argv.includes('--public');
const env = { ...process.env, E2E_PUBLIC_ONLY: String(publicOnly), ...(process.argv.includes('--headed') ? { HEADLESS: 'false' } : {}) };
await writeFile('reports/run-info.json', JSON.stringify({ started: new Date().toISOString(), url: env.APP_URL || 'https://gray-rock-0d7c57400.2.azurestaticapps.net', scope: publicOnly ? 'public-only (customer workflow skipped)' : 'full', createsOrder: env.E2E_ALLOW_ORDER === 'true' }, null, 2));
if (!publicOnly && !(env.E2E_EMAIL && env.E2E_PASSWORD)) {
  console.error('Full E2E requires E2E_EMAIL and E2E_PASSWORD. For public checks: npm run test:public');
  process.exit(2);
}
const child = spawn(process.execPath, ['--test', '--test-concurrency=1', '--test-reporter=spec', '--test-reporter-destination=stdout', '--test-reporter=junit', '--test-reporter-destination=reports/junit.xml', 'e2e.test.mjs'], { env, stdio: 'inherit' });
child.on('error', error => { console.error(error.message); process.exitCode = 1; });
child.on('exit', code => { process.exitCode = code ?? 1; });
