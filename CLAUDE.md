# CLAUDE.md

Shared context for every Claude working in this repo. The goal: no one in the
room knows something the others don't. Read this first; update it when you learn
something the next Claude would want to know.

---

## What this is

An interactive Sudoku web app — play, create, solve, and analyze puzzles in the
browser. **ASP.NET Core 9.0 MVC** on the back end, vanilla JavaScript on the
front end (no SPA framework). See [README.md](README.md) for the user-facing
overview; this file is the developer/process context.

## How to run

Requires the .NET 9 SDK.

```bash
dotnet run        # serves on the URL printed to console
dotnet build      # build only (this command is pre-approved in settings.local.json)
dotnet test Sudoku.Tests   # runs the xUnit suite (MUST name the project — see below)
```

> Because the app project is at the repo root, a bare `dotnet test` targets the
> web app (no tests). Always pass `Sudoku.Tests`.

The app entry point is [Program.cs](Program.cs); `SudokuService` is registered as
a singleton.

## Layout

The project lives at the **repository root** — it is not nested in a `Sudoku/`
subfolder (flattened on the `flatten-project-root` branch).

```text
.
├── Sudoku.csproj                # The web app project (repo root)
├── Program.cs                   # Bootstrap + DI
├── Controllers/HomeController.cs# Pages + JSON API
├── Services/SudokuService.cs    # The whole puzzle engine
├── Services/LogicalSolver.cs    # Human-style technique solver (in progress)
├── Services/SolveStep.cs        # One deduced step (technique + cells)
├── Models/                      # ErrorViewModel, NavLink
├── Views/Home/Sudoku.cshtml     # Game UI + ALL client-side game logic (one file)
├── wwwroot/                     # css, js, bootstrap & jquery libs
└── Sudoku.Tests/                # xUnit tests for SudokuService
```

## Core engine — `SudokuService`

Board = flat `int[81]`, row-major, `0` = empty. Methods:

- `GeneratePuzzle(clues)` — fills a solved board, then removes cells **only while
  the solution stays unique** (checks via `CountSolutions(..., cap: 2)`).
- `Solve(board)` — backtracking solver, returns solved board or `null`.
- `CountSolutions(board, cap)` — counts up to a cap; capped at 2 for speed.
- `GetConflicts(board)` — indices violating row/col/box rules.
- `FindBestEmpty` — MRV heuristic (fewest legal values first) used by solver/counter.

## API (HomeController)

| Method | Route | Returns |
| --- | --- | --- |
| GET  | `/api/sudoku/new?clues=30` (17–80) | `{ board, givens }` |
| POST | `/api/sudoku/solve` `{ board }` | `{ solvable, board }` |
| POST | `/api/sudoku/analyze` `{ board }` | `{ valid, conflicts, solutionCount, message }` |
| POST | `/api/sudoku/conflicts` `{ board }` | `{ conflicts }` |

## Front end (Sudoku.cshtml)

All game logic lives in one IIFE `<script>` block at the bottom of the view.
State: `board`, `givens`, `marks` (one array of 81 `Set`s — the player's candidate
notes; was previously split into `cornerMarks`/`centerMarks`, now unified),
`cellColors` (81 colors or null), `selected`, `conflicts`, `won`, `mode` (input
mode), `boardMode` (setter/solve), `showAllCands` (explorer candidate overlay),
`undoStack`/`redoStack`, timer. Plus grid
build, render, input, API calls, analyze panel, import/export. There is also a
*client-side* conflict checker (`localConflicts`) used for win detection so a
solve doesn't require a round-trip.

**Visual design** lives in `wwwroot/css/site.css` as **CSS custom properties**
(design tokens) on `:root` (light) and `[data-theme="dark"]` (dark). Theme is
applied to `<html data-theme>` by a tiny pre-paint script in `_Layout.cshtml`
(reads `localStorage['sudoku-theme']`, falls back to `prefers-color-scheme`) so
there's no flash; the in-app sun/moon button toggles + persists it. Layout:
header + board (left) + two stacked cards (right) — an **Input card** (mode
selector / numpad / color palette / undo-redo) and a **Puzzle-tools card**
(generate / solve / analyze / I-O). The Puzzle-tools card is the intended future
home of the technique-solver **reverse-tree inspector**.

### Input modes & interactions

- **Input modes** (`mode`): Normal (place digit) and Notes (candidate marks, only
  on empty cells). (`color` mode still exists in the code + a palette div, but is
  currently *unreachable* — no button wires it; dormant from the redesign.) The
  numpad and digit keys act according to the active mode. **Keyboard accelerator**
  bypasses the mode: **Shift+digit OR Ctrl/Cmd+digit = note** (see `enterValue(idx,val,forceMode)`).
- **Candidates render ONE way**: a positional 3×3 grid where digit `d` sits in a
  fixed row-major slot (1 = top-left … 9 = bottom-right), via `candGridHtml()`.
  Stable slots mean a digit never moves as others are added/removed, and a single
  candidate can be struck in place. This *one* renderer draws the player's `marks`,
  the explorer's **"show all candidates"** overlay, and the red struck-out digit an
  elimination removes. (Replaced the old packed corner-marks + joined center-marks.)
- **Undo/redo** snapshot the whole mutable state (`cloneState`/`applyState`);
  `pushHistory()` is called *before* every mutation. Ctrl+Z / Ctrl+Shift+Z (or Ctrl+Y).

### Conventions & gotchas

- **Cell borders are set once as inline styles** in `buildGrid()` and are never
  touched by class/`innerHTML` changes. This is deliberate — `renderBoard()`
  rewrites each cell's `className` AND `innerHTML` every frame (digits + pencil
  marks), so border styling must NOT live in classes or the 3px box separators
  would flicker/disappear. Setting `innerHTML`/`className` does not clear an
  element's inline styles, so borders survive. (Commit `eb49fe1` fixed a padding
  regression around this.) Don't move border logic into CSS classes.
- **Cell color** is applied as an inline `background` in `renderBoard()` so it
  shows over the (non-`!important`) peer/same-num tints; selection/conflict/won
  use `!important` and intentionally show on top of a cell color.
- `givens` is **visual-only in Setter mode** (you author clues — typing sets a
  given) but **protected in Solve mode** (given cells can't be overwritten with a
  digit). This is a deliberate change from the old "givens never enforced"
  behavior, made safe by the explicit `boardMode` split.
- `.sudk` is the app's export format: JSON `{ board, givens, marks, cellColors }`.
  The mark/color fields are **optional** — older `{ board, givens }`-only files
  still import cleanly (`importBoard` tolerates missing fields). **Legacy
  compatibility:** files written before the marks unification have `cornerMarks` +
  `centerMarks` instead of `marks`; `importBoard` merges them (`mergeMarks`) so they
  still load. It's user save data — **gitignored**, never committed.

## Build artifacts & .gitignore

- `.gitignore` ignores `bin/`, `obj/`, caches, NuGet, `.sudk`, IDE/OS junk,
  secrets, logs.
- **History note:** the first two commits accidentally committed all of
  `Sudoku/bin` and `Sudoku/obj` (108 files). They've since been `git rm --cached`'d
  so they're untracked going forward, but they still exist in earlier history.
  Don't re-add build output.

## Roadmap (see README for full list)

Headline idea: a **technique-aware solver** that replaces brute-force with a
human-style logical solver (singles, pairs, pointing pairs, X-Wing, …) and
**colour-codes each deduced cell and the cells it reasoned from by technique** —
turning "Solve" into a step-by-step teaching tool. Plus: hints, technique-based
difficulty rating, pencil marks, undo/redo, persistence, shareable puzzles.

---

*Keep this current. If you change architecture, conventions, or learn a gotcha,
edit this file in the same change.*
