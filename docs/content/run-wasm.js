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

// Runs one module to the end. Returns { output, segments, exitCode, trapped,
// size }: segments is the output with the input echoed, as { text, input }. A
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
  const written = []; // { bytes, input } chunks: output, and the input echoed, in order
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
    // Like a terminal: each read returns at most one line, and the line is
    // echoed into the output where the program read it.
    fd_read(fd, iovs, count, readOut) {
      if (fd !== 0) return EBADF;
      const newline = input.indexOf(10, inputPosition);
      const lineEnd = newline < 0 ? input.length : newline + 1;
      let total = 0;
      for (const [pointer, length] of buffers(iovs, count)) {
        const chunk = input.subarray(inputPosition, Math.min(inputPosition + length, lineEnd));
        bytesAt().set(chunk, pointer);
        written.push({ bytes: chunk.slice(), input: true });
        inputPosition += chunk.length;
        total += chunk.length;
        if (inputPosition >= lineEnd) break;
      }
      view().setUint32(readOut, total, true);
      return 0;
    },
    fd_write(fd, iovs, count, writtenOut) {
      if (fd !== 1 && fd !== 2) return EBADF;
      let total = 0;
      for (const [pointer, length] of buffers(iovs, count)) {
        written.push({ bytes: bytesAt().slice(pointer, pointer + length), input: false });
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
  // Joins neighbouring chunks of the same kind, so UTF-8 split across writes decodes.
  const parts = [];
  for (const chunk of written) {
    const last = parts[parts.length - 1];
    if (last && last.input === chunk.input) last.chunks.push(chunk.bytes);
    else parts.push({ input: chunk.input, chunks: [chunk.bytes] });
  }
  const decoder = new TextDecoder();
  const segments = parts.map(({ input, chunks }) => {
    const all = new Uint8Array(chunks.reduce((n, c) => n + c.length, 0));
    let offset = 0;
    for (const c of chunks) { all.set(c, offset); offset += c.length; }
    return { text: decoder.decode(all), input };
  });
  const output = segments.filter(s => !s.input).map(s => s.text).join('');
  return { output, segments, exitCode, trapped, size: bytes.byteLength };
}

// Wires up every [data-wasm-runner] element: a row of samples, the picked
// sample's source, its command line (the arguments are editable), its
// standard input, and the output. Picking a sample runs it.
const sources = new Map(); // sample name -> its source text, as a promise

function sourceOf(base, name) {
  if (!sources.has(name)) {
    sources.set(name, fetch(`${base}src/${name}.cs`).then(r => (r.ok ? r.text() : null)).catch(() => null));
  }
  return sources.get(name);
}

const kilobytes = n => (n >= 1e6 ? `${(n / 1e6).toFixed(1)} MB` : `${Math.round(n / 1000)} KB`);

for (const runner of document.querySelectorAll('[data-wasm-runner]')) {
  const base = runner.dataset.wasmRunner;
  const picks = [...runner.querySelectorAll('[data-sample]')];
  const args = runner.querySelector('.term-command input');
  const stdin = runner.querySelector('textarea');
  const inputBox = runner.querySelector('.term-input');
  const run = runner.querySelector('[data-run]');
  const output = runner.querySelector('output');
  const status = runner.querySelector('[data-status]');
  const note = runner.querySelector('[data-sample-note]');
  const moduleName = runner.querySelector('[data-module-name]');
  const sourceName = runner.querySelector('[data-source-name]');
  const source = runner.querySelector('[data-source]');
  let picked = picks[0];
  let shownSource = null;

  async function showSource(name) {
    if (shownSource === name) return;
    shownSource = name;
    sourceName.textContent = `samples/${name}.cs`;
    source.removeAttribute('data-highlighted');
    source.className = 'language-csharp';
    source.textContent = 'Loading…';
    const text = await sourceOf(base, name);
    if (shownSource !== name) return;
    // The // docs:name markers only tell the site which lines to quote.
    source.textContent = text === null
      ? 'The source isn’t on this site.'
      : text.split('\n').filter(line => !/^\s*\/\/ docs:/.test(line)).join('\n').trimEnd();
    if (text !== null) window.highlightCsharp?.(source);
    source.closest('pre').scrollTop = 0;
  }

  async function runPicked() {
    run.disabled = true;
    output.textContent = '';
    status.textContent = 'Running…';
    const name = picked.dataset.sample;
    try {
      const url = `${base}${name}.wasm`;
      const started = performance.now();
      const result = await runWasm(url, {
        // Split like a shell, roughly: "quoted text" is one argument.
        args: (args.value.match(/"[^"]*"|\S+/g) || []).map(a => a.replace(/^"(.*)"$/, '$1')),
        stdin: stdin.value,
      });
      const ms = Math.max(1, Math.round(performance.now() - started));
      output.replaceChildren(...result.segments.map(s => {
        if (!s.input) return s.text;
        const typed = document.createElement('span');
        typed.className = 'typed';
        typed.textContent = s.text;
        return typed;
      }));
      if (!result.segments.length) output.textContent = '(no output)';
      const ending = result.trapped ? `Stopped with exit code ${result.exitCode}` : `Exit code ${result.exitCode}`;
      status.textContent = `${ending}, in ${ms} ms, from a ${kilobytes(result.size)} module.`;
      status.classList.toggle('failed', result.exitCode !== 0);
    } catch (e) {
      status.textContent = `Couldn't run it: ${e.message}`;
      status.classList.add('failed');
    } finally {
      run.disabled = false;
    }
  }

  function pick(button) {
    picked = button;
    for (const b of picks) b.setAttribute('aria-pressed', String(b === button));
    args.value = button.dataset.args || '';
    stdin.value = button.dataset.stdin || '';
    inputBox.open = stdin.value !== '';
    note.textContent = button.dataset.note || '';
    moduleName.textContent = `${button.dataset.sample}.wasm`;
    showSource(button.dataset.sample);
    runPicked();
  }

  for (const button of picks) button.addEventListener('click', () => pick(button));
  run.addEventListener('click', runPicked);
  args.addEventListener('keydown', e => { if (e.key === 'Enter') runPicked(); });
  // The highlighter loads after this script; start once the page has.
  if (document.readyState === 'complete') pick(picked);
  else window.addEventListener('load', () => pick(picked), { once: true });
}
