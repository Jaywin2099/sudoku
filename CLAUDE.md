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
cd Sudoku
dotnet run        # serves on the URL printed to console
dotnet build      # build only (this command is pre-approved in settings.local.json)
```

The app entry point is [Sudoku/Program.cs](Sudoku/Program.cs); `SudokuService`
is registered as a singleton.

## Layout

```
Sudoku/
├── Program.cs                   # Bootstrap + DI
├── Controllers/HomeController.cs# Pages + JSON API
├── Services/SudokuService.cs    # The whole puzzle engine
├── Models/                      # ErrorViewModel, NavLink
├── Views/Home/Sudoku.cshtml     # Game UI + ALL client-side game logic (one file)
└── wwwroot/                     # css, js, bootstrap & jquery libs
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

All game logic lives in one IIFE `<script>` block at the bottom of the view —
state (`board`, `givens`, `selected`, `conflicts`, `won`, timer), grid build,
render, input, API calls, analyze panel, import/export, timer. There is also a
*client-side* conflict checker (`localConflicts`) used for win detection so a
solve doesn't require a round-trip.

### Conventions & gotchas

- **Cell borders are set once as inline styles** in `buildGrid()` and are never
  touched by class changes. This is deliberate — `renderBoard()` rewrites
  `className` every frame, so border styling must NOT live in classes or the 3px
  box separators would flicker/disappear. (Commit `eb49fe1` fixed a padding
  regression around this.) Don't move border logic into CSS classes.
- `givens` is **visual-only** — it marks original puzzle cells for styling; it is
  not enforced (players can overwrite, which is intentional for the editor use).
- `.sudk` is the app's export format: JSON `{ board, givens }`. It's user save
  data — **gitignored**, never committed.

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
