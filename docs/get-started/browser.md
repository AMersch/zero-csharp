---
title: Run it in your browser
index: 5
---

# Run it in your browser

The samples' WebAssembly builds run right here, in this page: the same `.wasm` files that
`./build.sh --wasm` makes and `./test.sh --wasm` checks, built from this site's version of the code.
Each one is a few dozen kilobytes. Pick one and run it.

<div data-wasm-runner="../wasm/" class="not-prose wasm-runner">
  <label>Sample
    <select>
      <option value="Records" data-note="Records, anonymous types and ToString.">Records</option>
      <option value="Queries" data-note="LINQ-style queries that allocate nothing while they run.">Queries</option>
      <option value="Core" data-note="Math, Interlocked, extension members and unions.">Core</option>
      <option value="Strings" data-note="Strings without a garbage collector.">Strings</option>
      <option value="Collections" data-note="Lists and maps in arenas.">Collections</option>
      <option value="Casts" data-note="Casts and type checks.">Casts</option>
      <option value="Threads" data-note="Parallel.For; in the browser it runs on one thread.">Threads</option>
      <option value="Quiz" data-stdin="46&#10;seven&#10;5&#10;17&#10;44&#10;&#10;74&#10;" data-note="The tutorial's quiz, answered from the input box: one line per question, an empty line skips one. Change the answers and run it again.">Quiz</option>
      <option value="Guards" data-args="list-index" data-note="A mistake lib/ stops the program for; try restore-order or throw as the argument.">Guards</option>
      <option value="Llama" data-args="-t 0.8 -s 7 -i &quot;The dragon&quot;" data-note="A language model of 260,000 parameters, compiled into a 1.1 MB module, telling a story. Change the seed (-s) or the start (-i) and run it again.">Llama</option>
      <option value="Tiny" data-note="The smallest program.">Tiny</option>
    </select>
  </label>
  <label>Arguments <input type="text" spellcheck="false"></label>
  <label>Standard input <textarea rows="3" spellcheck="false"></textarea></label>
  <p data-sample-note></p>
  <button type="button">Run</button>
  <output></output>
</div>

The page gives each program what a terminal would: its arguments, standard input from the box, and
standard output and error printed below, with the exit code. That is all of WASI these programs use,
so the page implements it itself, in `docs/content/run-wasm.js`, instead of loading a library.

The programs run as they do under wasmtime, on one thread: WebAssembly here has no threads, so
`Parallel.For` runs its loop on the calling thread. [Run it on WebAssembly](webassembly.html) shows how
to build and run the same modules yourself.
