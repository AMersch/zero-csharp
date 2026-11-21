// Runs the samples' WebAssembly builds (./build.sh --wasm) in the page.
//
// The modules are WASI 0.1 programs. Of WASI they use only what a console
// program needs: arguments, the clock, reading standard input, writing
// standard output and error, and exiting. This file is that much of WASI,
// written against the page instead of an operating system, so no library is
// loaded from anywhere.
//
// A page opts in with an element carrying data-wasm-runner; the build
// (scripts/build-docs.py) adds this script to such pages and copies the
// modules to wasm/ next to them.

const EBADF = 8, ESPIPE = 70;

class Exit {
  constructor(code) { this.code = code; }
}

// Runs one module to the end. Returns { output, exitCode, trapped }: a
// program that aborts (zerocore and lib/ abort after printing why) ends in a
// WebAssembly trap, reported here as trapped with exit code 134, the code a
// shell shows for an abort.
export async function runWasm(url, { args = [], stdin = '' } = {}) {
  const response = await fetch(url);
  if (!response.ok) throw new Error(`${url}: ${response.status} ${response.statusText}`);
  const bytes = await response.arrayBuffer();

  const encoder = new TextEncoder();
  const argv = ['program', ...args].map(a => encoder.encode(a + '\0'));
  const input = encoder.encode(stdin);
  let inputPosition = 0;
  const written = []; // byte chunks from stdout and stderr, in order
  let memory;
  const view = () => new DataView(memory.buffer);
  const bytesAt = () => new Uint8Array(memory.buffer);

  // Reads an array of { pointer, length } buffers (WASI's iovec).
  function* buffers(iovs, count) {
    for (let i = 0; i < count; i++) {
      yield [view().getUint32(iovs + 8 * i, true), view().getUint32(iovs + 8 * i + 4, true)];
    }
  }

  const wasi = {
    args_sizes_get(countOut, sizeOut) {
      view().setUint32(countOut, argv.length, true);
      view().setUint32(sizeOut, argv.reduce((n, a) => n + a.length, 0), true);
      return 0;
    },
    args_get(pointers, buffer) {
      for (const [i, a] of argv.entries()) {
        view().setUint32(pointers + 4 * i, buffer, true);
        bytesAt().set(a, buffer);
        buffer += a.length;
      }
      return 0;
    },
    clock_time_get(id, precision, timeOut) {
      view().setBigUint64(timeOut, BigInt(Math.round(performance.now() * 1e6)), true);
      return 0;
    },
    fd_close() { return 0; },
    fd_fdstat_get(fd, out) {
      if (fd > 2) return EBADF;
      view().setUint8(out, 2); // a character device, like a terminal
      view().setUint16(out + 2, 0, true);
      view().setBigUint64(out + 8, 0xFFFFFFFFFFFFFFFFn, true);
      view().setBigUint64(out + 16, 0xFFFFFFFFFFFFFFFFn, true);
      return 0;
    },
    fd_seek() { return ESPIPE; },
    fd_read(fd, iovs, count, readOut) {
      if (fd !== 0) return EBADF;
      let total = 0;
      for (const [pointer, length] of buffers(iovs, count)) {
        const chunk = input.subarray(inputPosition, inputPosition + length);
        bytesAt().set(chunk, pointer);
        inputPosition += chunk.length;
        total += chunk.length;
        if (chunk.length < length) break;
      }
      view().setUint32(readOut, total, true);
      return 0;
    },
    fd_write(fd, iovs, count, writtenOut) {
      if (fd !== 1 && fd !== 2) return EBADF;
      let total = 0;
      for (const [pointer, length] of buffers(iovs, count)) {
        written.push(bytesAt().slice(pointer, pointer + length));
        total += length;
      }
      view().setUint32(writtenOut, total, true);
      return 0;
    },
    proc_exit(code) { throw new Exit(code); },
  };

  const { instance } = await WebAssembly.instantiate(bytes, { wasi_snapshot_preview1: wasi });
  memory = instance.exports.memory;
  let exitCode = 0, trapped = null;
  try {
    instance.exports._start();
  } catch (e) {
    if (e instanceof Exit) exitCode = e.code;
    else if (e instanceof WebAssembly.RuntimeError) { exitCode = 134; trapped = e.message; }
    else throw e;
  }
  const all = new Uint8Array(written.reduce((n, c) => n + c.length, 0));
  let offset = 0;
  for (const chunk of written) {
    all.set(chunk, offset);
    offset += chunk.length;
  }
  return { output: new TextDecoder().decode(all), exitCode, trapped };
}

// Wires up every [data-wasm-runner] element: a sample picker, arguments,
// standard input, a Run button and the output.
for (const runner of document.querySelectorAll('[data-wasm-runner]')) {
  const base = runner.dataset.wasmRunner;
  const pick = runner.querySelector('select');
  const args = runner.querySelector('input');
  const stdin = runner.querySelector('textarea');
  const button = runner.querySelector('button');
  const output = runner.querySelector('output');
  const note = runner.querySelector('[data-sample-note]');

  const showDefaults = () => {
    const option = pick.selectedOptions[0];
    args.value = option.dataset.args || '';
    stdin.value = option.dataset.stdin || '';
    note.textContent = option.dataset.note || '';
  };
  pick.addEventListener('change', showDefaults);
  showDefaults();

  button.addEventListener('click', async () => {
    button.disabled = true;
    output.textContent = 'Running…';
    try {
      const url = `${base}${pick.selectedOptions[0].dataset.module || pick.value}.wasm`;
      const started = performance.now();
      const { output: text, exitCode, trapped } = await runWasm(url, {
        // Split like a shell, roughly: "quoted text" is one argument.
        args: (args.value.match(/"[^"]*"|\S+/g) || []).map(a => a.replace(/^"(.*)"$/, '$1')),
        stdin: stdin.value,
      });
      const ms = Math.round(performance.now() - started);
      const ending = trapped ? `stopped (${trapped}), exit code ${exitCode}` : `exit code ${exitCode}`;
      output.textContent = `${text}\n[${ending}, ${ms} ms]`;
    } catch (e) {
      output.textContent = `Couldn't run it: ${e.message}`;
    } finally {
      button.disabled = false;
    }
  });
}
