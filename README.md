# Sudoku

An interactive Sudoku web app — play, create, solve, and analyze puzzles in the
browser. Built with **ASP.NET Core 9.0 MVC** on the back end and vanilla
JavaScript on the front end (no SPA framework).

---

## Features

- **Puzzle generation** with adjustable difficulty by clue count
  (Easy 36 · Med 30 · Hard 24 · Expert 17). Every generated puzzle is
  guaranteed to have a **unique solution** — clues are only removed while a
  single solution still exists.
- **Solver** — a backtracking engine with an MRV (Minimum Remaining Values)
  heuristic that fills the most-constrained cell first.
- **Analyze** — reports whether the current board has conflicts and how many
  solutions exist (none / exactly one / multiple).
- **Setter & Solve modes** — *Setter* authors a puzzle (typed digits become
  givens); *Solve* plays it and protects the givens from being overwritten.
- **Four input modes** — Normal digits, **Corner** and **Center** pencil marks,
  and **Color** to tint cells — the input model the constructor community expects.
- **Undo / redo** across digits, pencil marks, and colors (`Ctrl+Z` / `Ctrl+Shift+Z`).
- **Light & dark themes** — toggle in the header; remembers your choice and
  respects your system preference, applied before first paint (no flash).
- **Live play aids** — conflict highlighting, peer (row/column/box) highlighting,
  and same-number highlighting for the selected cell.
- **Keyboard support** — arrow keys to move, `1`–`9` to fill, `Shift`+digit for
  corner marks, `Ctrl/Cmd`+digit for center marks, `Backspace`/`Delete` to clear.
- **Timer** that starts on a new puzzle and stops on solve.
- **Import / Export** in a custom `.sudk` format (JSON holding the board, the
  original givens, and any pencil marks / cell colors).

---

## Architecture

```
Sudoku/
├── Program.cs                     # App bootstrap; registers SudokuService (singleton)
├── Controllers/
│   └── HomeController.cs          # Pages + JSON API endpoints
├── Services/
│   └── SudokuService.cs           # Core engine: generate / solve / analyze / conflicts
├── Models/                        # View models (ErrorViewModel, NavLink)
├── Views/
│   └── Home/Sudoku.cshtml         # The game UI + all client-side game logic
└── wwwroot/                       # Static assets (css, js, bootstrap/jquery libs)
```

### Core engine — `SudokuService`

A board is a flat `int[81]` in row-major order; `0` means empty.

| Method | Purpose |
| --- | --- |
| `GeneratePuzzle(clues)` | Fills a full solved board, then removes cells while uniqueness holds. |
| `Solve(board)` | Returns the solved board, or `null` if unsolvable. |
| `CountSolutions(board, cap)` | Counts solutions up to a cap (used to enforce uniqueness; cheap at cap 2). |
| `GetConflicts(board)` | Returns indices of cells that violate row/column/box rules. |
| `GetGivens(puzzle)` | Marks which cells are part of the original puzzle. |

### API

| Method | Route | Body / Query | Returns |
| --- | --- | --- | --- |
| GET  | `/api/sudoku/new?clues=30` | `clues` (17–80) | `{ board, givens }` |
| POST | `/api/sudoku/solve` | `{ board }` | `{ solvable, board }` |
| POST | `/api/sudoku/analyze` | `{ board }` | `{ valid, conflicts, solutionCount, message }` |
| POST | `/api/sudoku/conflicts` | `{ board }` | `{ conflicts }` |

---

## Getting started

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download).

```bash
cd Sudoku
dotnet run
```

Then open the URL printed in the console (e.g. `https://localhost:5001`) and
click **Play Now**.

To build without running:

```bash
dotnet build
```

---

## The `.sudk` format

Exporting a board produces a `.sudk` file — plain JSON:

```json
{
  "board":       [5, 3, 0, ... 81 values ...],
  "givens":      [true, true, false, ... 81 booleans ...],
  "cornerMarks": [[1,2], [], ... 81 arrays of digits ...],
  "centerMarks": [[], [4,5], ... 81 arrays of digits ...],
  "cellColors":  ["var(--c1)", null, ... 81 colors-or-null ...]
}
```

`board` holds the current values (`0` = empty); `givens` marks which cells were
part of the original puzzle so they can be rendered as fixed. `cornerMarks`,
`centerMarks`, and `cellColors` capture pencil marks and cell tints and are
**optional** — older files containing only `board` and `givens` still import
cleanly. `.sudk` files are user save data and are **not** tracked in the repo.

---

## Roadmap / future design propositions

Ideas for where this can go next:

- **Technique-aware solver with colour-coded steps.** Replace the brute-force
  backtracker with a human-style logical solver that recognises named
  techniques — naked/hidden singles, naked/pointing pairs, box-line reduction,
  X-Wing, swordfish, etc. As it deduces each next cell, the UI would
  **colour-code the cell (and the cells it reasoned from) by the technique used**,
  turning "Solve" into a step-by-step teaching tool that shows *why* each value
  is forced, not just the answer.
- **Hint button** that surfaces the single easiest next deduction (and its
  technique) instead of solving the whole board.
- **Difficulty rating by technique.** Grade a puzzle by the hardest technique
  required to solve it logically, rather than by clue count alone.
- **Pencil marks / candidate notes** — let players annotate candidate digits per
  cell, with optional auto-candidate mode.
- **Undo / redo** history and a move log.
- **Persistence** — save and resume in-progress games (local storage or accounts).
- **Statistics** — track solve times and win streaks per difficulty.
- **Shareable puzzles** — encode a board into a short URL alongside `.sudk` export.

---

## Tech stack

- ASP.NET Core 9.0 MVC (C#, nullable + implicit usings enabled)
- Razor views
- Vanilla JavaScript (no front-end framework)
- Bootstrap & jQuery (bundled in `wwwroot/lib`)
