# Documentation site — design

Date: 2026-11-21. Approved in conversation; items marked *(call)* were decided by Claude, not the user.

## Intent

A documentation website for this repository (zero-mode C# on the bflat fork, with zerocore and `lib/`),
built the way FCQRS (`~/Repos/FCQRS`) builds its site. The user likes how FsLiveDocs documents things
and wants the same reading experience here. Readers are people who want to **use this bflat fork and
learn it**: install it, build programs, and understand what zero mode allows and how to work within it.
C# only: no F# content.

## Goals

- A reader with no prior context can install the fork, build and run a first program natively, on
  WebAssembly and on DOS, by following the site alone.
- Explain the ideas that make zero mode different (no GC, zerocore, arenas and lifetimes, strings,
  updated memory safety rules, the three targets) as concept pages.
- Short task guides for common jobs.
- A reference page for every public type in `lib/`.
- Every C# excerpt on the site is the code of a sample that builds and runs; a check fails when they
  drift apart.
- Search, light/dark themes, and publication to GitHub Pages from CI.

## Non-goals

- F# content or F# tooltips.
- Documenting zerocore's internals type by type (concept pages explain it; reference covers `lib/`).
- Versioned docs/release history (FsLiveDocs supports it; not needed now).
- Replacing the README in this work. The README may be shortened later to point at the site.

## Anti-goals

- Code on the site that doesn't compile or no longer matches the repo.
- A site that looks generic rather than like FCQRS's FsLiveDocs site.
- A docs build that needs bflat, Docker, emulators or anything beyond .NET 10, Python 3 and Node.

## Constraints and findings

- FsLiveDocs 0.7.3 (pinned in FCQRS's `.config/dotnet-tools.json`) **requires at least one F#
  project**; a placeholder `.fsproj` with an empty module works (verified with a spike). Its API pages
  cover F# projects only, so its `api/` output is empty here and must be dropped from navigation.
- FsLiveDocs renders a `csharp` fence as plain `<code class="language-csharp">`. FCQRS colours C# with
  its own `docs/content/highlight.min.js` and `language-tabs.js`; reuse them.
- `dotnet build lib/BflatLib.csproj -p:GenerateDocumentationFile=true` builds against zerocore without
  bflat and emits `BflatLib.xml` (verified). That is the source for reference pages.
- `docs/llama-plan.md` is the user's untracked file: it must not be published or moved.
- The repo is `github.com/OnurGumus/bflat_test`; CI already has `.github/workflows/test.yml`.

## Approaches considered

1. **FsLiveDocs, set up like FCQRS** — *chosen.* It is what the user liked; a spike showed it builds a
   C#-only site with a placeholder F# project. Costs: a placeholder project, and reference pages must
   come from our own script.
2. DocFX — generates a C# API reference natively, but looks and reads nothing like FsLiveDocs.
3. A hand-written static generator — full control, but a tool to maintain.

## Design

### Layout

```
.config/dotnet-tools.json        FsLiveDocs pinned (same version as FCQRS)
.livedocs/config.json            site name, themes, navigation, docs set
.livedocs/examples/              placeholder Examples.fsproj + Empty.fs (FsLiveDocs needs one project)
.livedocs/content/               generated, git-ignored (the build assembles pages here)
docs/index.html                  custom landing page
docs/get-started/                install, first program, WebAssembly, DOS
docs/tutorial/                   the quiz, built step by step
docs/concepts/                   zero mode, zerocore, arenas, strings, safety rules, targets
docs/how-to/                     task guides
docs/content/                    site.css, highlight.min.js, language-tabs.js (adapted from FCQRS)
docs/img/                        logo and images (bflat.png exists at repo root)
scripts/build-docs.py            the build
scripts/check-doc-snippets.py    excerpt check (--update rewrites excerpts)
scripts/gen-reference.py         lib/ XML docs → docs pages
.github/workflows/docs.yml       build on PRs, deploy to Pages from master
```

`docs/superpowers/` and `docs/llama-plan.md` are not part of the site *(call: the build copies only the
listed folders)*.

### Content

- **Get started:** install the fork (from README "Getting bflat"/"Quick start"), build and run
  `Minimal`, then the same on WebAssembly (`--wasm`) and DOS (`--dos`, Bochs).
- **Tutorial:** build the quiz in steps — print with `Out`, format prompts, read answers with
  `In.ReadLine`, validate and re-ask, keep mistakes in an arena with `RefList<string>`. Each step is a
  runnable program under `samples/tutorial/` *(call)*, built by `build.sh` and checked by `test.sh`
  like other samples (with `tests/input/` where needed), so a reader can run every step.
- **Concepts:** what zero mode is and why; zerocore (what the core library provides); arenas and
  lifetimes (Save/Restore vs scratch arenas, Reset, DebugChecks); strings without a GC; the updated
  memory safety rules; how each target works (native, WebAssembly, DOS/386); what's supported and known
  issues. Source: the README sections of the same names, rewritten into pages.
- **Task guides** (initial set) *(call)*: read input; keep a string past its arena; choose a collection
  (List/Dict/RefList/StrDict/RefDict); format numbers; build for WebAssembly; build for DOS; draw on the
  VGA screen; use SIMD; debug arena misuse with DebugChecks.
- **Reference:** one page per public `lib/` type (Arena, Out, In, Str, StrBuilder, List, Dict, RefList,
  StrDict, RefDict, Kernels, Dos, Fmt), generated from `///` comments: summary, members with
  signatures, remarks/safety. Missing doc comments are listed by the generator as warnings.
  *(call, in planning: the generator reads `lib/*.cs` rather than the XML file, so signatures appear
  as written in the source; the XML build stays a fallback.)*
- **Landing page:** what this is, a short code sample, the three targets (with the 386 game), links to
  Get started and the tutorial. Same visual language as FCQRS's homepage, adapted *(call: own colours
  derived from bflat.png)*.

### Code excerpts

Same mechanism as FCQRS: samples mark regions with `// docs:<name>` … `// docs:end`; a page puts
`<!-- sample: <file> <region> -->` before a `csharp` fence. `check-doc-snippets.py` fails when a fence
differs from its region or a line exceeds 80 columns; `--update` rewrites fences. Region markers are
plain comments, so samples still build unchanged. Untagged fences are allowed only for shell commands
and output.

### Build

`python3 scripts/build-docs.py`: run the snippet check, generate reference pages, assemble
`.livedocs/content/` from `docs/`, build the placeholder project, run `dotnet livedocs build`, copy
static assets and the landing page into `output/`, remove the empty API page and its nav link, index
with Pagefind, and check local links. Serve with `python3 -m http.server -d output`.

### CI

`.github/workflows/docs.yml`: on PRs, build and verify; on pushes to `master`, also deploy `output/` to
GitHub Pages. It does not build bflat; sample correctness stays with `test.yml`. Enabling Pages in the
repository settings is a manual step for the user.

## Decisions after review

- **Branch** *(call)*: the docs are built on `csharp15`, which has `In`, Quiz and DOS. `docs.yml` builds
  on every push and PR but deploys only from `master`, so the first deploy happens when `csharp15` is
  merged. Merging is the user's decision.
- **Reference scope** *(call)*: generate pages for `public` types only, nested public types (such as
  `Arena.Mark`) on their parent's page. `Fmt` is internal, so it gets no page; number formatting is
  covered by the `Out`/`StrBuilder` pages and the "format numbers" guide. No library code changes.
- **Arena API** (user, 2026-11-21: "arena done"): document the arena API as it is in the working
  tree: `OpenScope()`/`Scope` as the usual way to free temporary work (Save/Restore underneath), named
  arenas, `Dump()`, `PeakUsed`/`PeakCapacity`, `stopOnOutOfMemory`, and the `Guards` sample as the
  list of mistakes the library stops the program for (the "debug arena misuse" guide quotes it). The
  user commits that work; the docs work builds on top of it and doesn't commit its files.
- **Placeholder project** *(call)*: `.livedocs/examples/` gets its own `Directory.Build.props` so the
  root one (NoStdLib, zerocore) doesn't apply to it.

## Left to the builder

- Exact page titles, order and wording; splitting or merging concept pages as the material dictates.
- How much of FCQRS's `build-docs.py` and `language-tabs.js` to copy versus simplify (no F#/C# tab
  pairing is needed).
- Landing-page design within the FCQRS look.
