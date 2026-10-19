import { spawn, spawnSync } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { mkdirSync, createWriteStream } from 'node:fs';
import { createServer } from 'node:net';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const demo = resolve(root, '.demo');
mkdirSync(demo, { recursive: true, mode: 0o700 });
const dotnet = process.env.DOTNET_EXE || 'dotnet';
const children = [];
let stopping = false;
function run(command, args, cwd = root) {
  const result = spawnSync(command, args, { cwd, stdio: 'inherit', shell: process.platform === 'win32' && command === 'npm' });
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error(`${command} failed (${result.status}).`);
}
async function freePort(port) {
  await new Promise((resolvePromise, reject) => {
    const server = createServer(); server.once('error', () => reject(new Error(`Le port ${port} est déjà utilisé. Arrêtez l’autre instance avant de continuer.`)));
    server.listen(port, '127.0.0.1', () => server.close(resolvePromise));
  });
}
const shared = { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development',
  KillerBee__Demo: 'true', KillerBee__TransportKey: randomBytes(48).toString('base64'),
  KillerBee__DemoDatabase: resolve(demo, 'killerbee.db') };
function start(name, project, port, extra = {}) {
  const log = createWriteStream(resolve(demo, `${name}.log`), { flags: 'a', mode: 0o600 });
  const dll = resolve(root, `src/KillerBee.${project}/bin/Release/net10.0/KillerBee.${project}.dll`);
  const child = spawn(dotnet, [dll], { cwd: resolve(root, `src/KillerBee.${project}`), env: { ...shared, ASPNETCORE_URLS: `http://127.0.0.1:${port}`, KillerBee__Instance: name, ...extra }, stdio: ['ignore', 'pipe', 'pipe'] });
  child.stdout.pipe(log); child.stderr.pipe(log); children.push(child);
  child.once('error', err => { console.error(err.message); stop(1); });
  child.once('exit', code => { log.end(); if (!stopping) { console.error(`${name} stopped (${code}). See .demo/${name}.log`); stop(1); } });
}
function stop(code = 0) {
  if (stopping) return; stopping = true;
  for (const child of children) child.kill('SIGTERM');
  setTimeout(() => { for (const child of children) if (child.exitCode === null) child.kill('SIGKILL'); process.exit(code); }, 1500);
}
process.on('SIGINT', () => stop()); process.on('SIGTERM', () => stop());

try {
  for (const port of [5100, 5101, 5201, 5202]) await freePort(port);
  if (!process.argv.includes('--no-build')) {
    run(dotnet, ['build', 'KillerBee.slnx', '--configuration', 'Release']);
    run('npm', ['ci', '--no-audit', '--no-fund'], resolve(root, 'frontend'));
    run('npm', ['run', 'build'], resolve(root, 'frontend'));
  }
  const initialized = spawnSync(dotnet, [resolve(root, 'src/KillerBee.Components/bin/Release/net10.0/KillerBee.Components.dll'), '--init-demo'], {
    cwd: root, env: { ...shared, ASPNETCORE_URLS: 'http://127.0.0.1:5201' }, encoding: 'utf8'
  });
  if (initialized.error || initialized.status !== 0) throw new Error(initialized.error?.message || initialized.stderr || initialized.stdout || 'Database initialization failed.');
  start('components-a', 'Components', 5201); start('components-b', 'Components', 5202);
  start('platform', 'Platform', 5101, { KillerBee__Components__0: 'http://127.0.0.1:5201/components/execute', KillerBee__Components__1: 'http://127.0.0.1:5202/components/execute' });
  start('local', 'Local', 5100, { KillerBee__PlatformUrl: 'http://127.0.0.1:5101/platform/dispatch' });
  const deadline = Date.now() + 45000;
  let ready = false;
  while (Date.now() < deadline && !stopping) {
    ready = (await Promise.all([5100, 5101, 5201, 5202].map(async port => { try { return (await fetch(`http://127.0.0.1:${port}/health`)).ok; } catch { return false; } }))).every(Boolean);
    if (ready) break;
    await new Promise(r => setTimeout(r, 300));
  }
  if (!ready) throw new Error('Services did not become ready. Inspect .demo/*.log.');
  console.log('\nKillerBee ready: http://127.0.0.1:5100\nProfils fictifs : R&D, Tests, Production. Ctrl+C pour arrêter.\n');
} catch (error) { console.error(error.message); stop(1); }
