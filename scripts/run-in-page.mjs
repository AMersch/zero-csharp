// Runs a sample's WebAssembly build the way the docs site's browser page does,
// through docs/content/run-wasm.js, but under Node: ./test.sh --wasm uses it to
// check that the page can run every sample (a module importing a WASI function
// the page lacks doesn't load at all, so this catches it before the site does).
//
// Usage: node scripts/run-in-page.mjs <Sample> [arguments...] < input
// Prints what the program wrote and exits with its exit code.

import { readFileSync } from 'node:fs';
import { runWasm } from '../docs/content/run-wasm.js';

const [name, ...args] = process.argv.slice(2);
const module = readFileSync(new URL(`../bin/wasm/${name}.wasm`, import.meta.url));
const stdin = new Uint8Array(readFileSync(0));
const result = await runWasm(module, { args, stdin });
process.stdout.write(result.output);
if (result.trapped) process.stderr.write(`(stopped: ${result.trapped})\n`);
process.exitCode = result.exitCode;
