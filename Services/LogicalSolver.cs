using System.Numerics;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Sudoku.Tests")]

namespace Sudoku.Services;

// A deductive, human-style solver. It applies named techniques in increasing
// order of difficulty and records each one as a SolveStep. Unlike the
// backtracking SudokuService (used for generation / uniqueness / the Solve
// button), this solver NEVER guesses — when it can find no forced move it stops,
// which is itself the useful signal that the puzzle needs a harder technique
// than it currently knows.
//
// The solver maintains a *persistent candidate grid* (`cands`, one 9-bit mask
// per cell) alongside the board. Placements update the board and strike the
// digit from peers' masks; elimination-only techniques (locked candidates,
// subsets, fish, wings) strike candidates that no placed digit would have
// removed. Those eliminations MUST persist between steps — that is the whole
// reason advanced techniques chain into later singles — so finders read and the
// Solve loop writes the same grid rather than recomputing candidates from
// scratch each step.
//
// Add a technique by writing a finder and slotting it into the FindNextStep
// chain in difficulty (tier) order; everything downstream (the step list, and
// later the branching graph + grading) reads the same SolveStep shape. A finder
// must only return a step that makes real progress — a placement, or at least
// one elimination of a candidate that is currently present — otherwise the
// Solve loop could spin forever re-finding the same pattern.
public class LogicalSolver
{
    public SolveResult Solve(int[] puzzle)
    {
        var board = (int[])puzzle.Clone();
        var cands = BuildCandidates(board);
        var steps = new List<SolveStep>();

        while (Array.IndexOf(board, 0) != -1)
        {
            var step = FindNextStep(board, cands);
            if (step == null) break;          // stuck: needs a technique we don't have yet

            // Apply the step to the live candidate grid (placements clear the cell
            // and strike the digit from peers; eliminations strike single bits)…
            foreach (var p in step.Placements)
            {
                cands[p.Cell] = 0;
                int m = 1 << (p.Digit - 1);
                foreach (int peer in Peers[p.Cell]) cands[peer] &= ~m;
            }
            foreach (var e in step.Eliminations)
                cands[e.Cell] &= ~(1 << (e.Digit - 1));

            board = step.BoardAfter;          // BoardAfter already has placements applied
            steps.Add(step);
        }

        bool solved = Array.IndexOf(board, 0) == -1;
        return new SolveResult(solved, steps, board);
    }

    // View-agnostic model primitive for the move-tree explorer: EVERY legal
    // single-cell placement (naked + hidden single) on the board right now, deduped
    // by cell+digit. Candidates are computed fresh from the board (placement-only —
    // no carried-over advanced eliminations), so a board always yields the same set
    // regardless of which front-end view renders it.
    public List<SolveStep> AllMoves(int[] board)
    {
        var cands = BuildCandidates(board);
        var moves = new List<SolveStep>();
        var seen = new HashSet<int>();   // cell * 9 + (digit - 1)

        for (int cell = 0; cell < 81; cell++)
        {
            if (board[cell] != 0) continue;
            int cand = cands[cell];
            if (cand == 0 || (cand & (cand - 1)) != 0) continue;   // need exactly one bit
            int digit = BitOperations.TrailingZeroCount(cand) + 1;
            if (!seen.Add(cell * 9 + digit - 1)) continue;
            moves.Add(new SolveStep
            {
                Technique = "Naked Single",
                Tier = (int)TechniqueTier.Single,
                Explanation = $"{CellName(cell)} has only one candidate left: {digit}.",
                Placements = new() { new Placement(cell, digit) },
                EvidenceCells = new() { cell },
                BoardAfter = With(board, cell, digit)
            });
        }

        for (int unit = 0; unit < 27; unit++)
        {
            int[] cells = UnitsList[unit];
            for (int digit = 1; digit <= 9; digit++)
            {
                int mask = 1 << (digit - 1);
                int home = -1, count = 0;
                bool alreadyPlaced = false;
                foreach (int cell in cells)
                {
                    if (board[cell] == digit) { alreadyPlaced = true; break; }
                    if (board[cell] == 0 && (cands[cell] & mask) != 0) { home = cell; count++; }
                }
                if (alreadyPlaced || count != 1) continue;
                if (!seen.Add(home * 9 + digit - 1)) continue;
                moves.Add(new SolveStep
                {
                    Technique = "Hidden Single",
                    Tier = (int)TechniqueTier.Single,
                    Explanation = $"In {UnitName(unit)}, {digit} fits only in {CellName(home)}.",
                    Placements = new() { new Placement(home, digit) },
                    EvidenceCells = cells.ToList(),
                    BoardAfter = With(board, home, digit)
                });
            }
        }

        moves.Sort((a, b) => a.Placements[0].Cell - b.Placements[0].Cell);
        return moves;
    }

    // Cheapest technique first. Each finder returns the first move it spots, or null.
    // Order is tier order: singles → locked candidates → subsets → fish → wings.
    internal static SolveStep? FindNextStep(int[] board, int[] cands) =>
        FindNakedSingle(board, cands)
        ?? FindHiddenSingle(board, cands)
        ?? FindPointing(board, cands)
        ?? FindClaiming(board, cands)
        ?? FindNakedSubset(board, cands, 2, "Naked Pair")
        ?? FindHiddenSubset(board, cands, 2, "Hidden Pair")
        ?? FindNakedSubset(board, cands, 3, "Naked Triple")
        ?? FindHiddenSubset(board, cands, 3, "Hidden Triple")
        ?? FindNakedSubset(board, cands, 4, "Naked Quad")
        ?? FindHiddenSubset(board, cands, 4, "Hidden Quad")
        ?? FindFish(board, cands, 2, "X-Wing")
        ?? FindFish(board, cands, 3, "Swordfish")
        ?? FindFish(board, cands, 4, "Jellyfish")
        ?? FindXyWing(board, cands)
        ?? FindXyzWing(board, cands);

    // ── Tier 1: Singles ──────────────────────────────────────────────────────

    // Naked Single: an empty cell with exactly one remaining candidate.
    internal static SolveStep? FindNakedSingle(int[] board, int[] cands)
    {
        for (int cell = 0; cell < 81; cell++)
        {
            if (board[cell] != 0) continue;
            int cand = cands[cell];
            if (cand == 0 || (cand & (cand - 1)) != 0) continue;   // need exactly one bit set

            int digit = BitOperations.TrailingZeroCount(cand) + 1;
            return new SolveStep
            {
                Technique = "Naked Single",
                Tier = (int)TechniqueTier.Single,
                Explanation = $"{CellName(cell)} has only one candidate left: {digit}.",
                Placements = new() { new Placement(cell, digit) },
                EvidenceCells = new() { cell },
                BoardAfter = With(board, cell, digit)
            };
        }
        return null;
    }

    // Hidden Single: within a unit (row/col/box) a digit has exactly one cell it
    // can legally go, even if that cell has other candidates too.
    internal static SolveStep? FindHiddenSingle(int[] board, int[] cands)
    {
        for (int unit = 0; unit < 27; unit++)
        {
            int[] cells = UnitsList[unit];
            for (int digit = 1; digit <= 9; digit++)
            {
                int mask = 1 << (digit - 1);
                int home = -1, count = 0;
                bool alreadyPlaced = false;

                foreach (int cell in cells)
                {
                    if (board[cell] == digit) { alreadyPlaced = true; break; }
                    if (board[cell] == 0 && (cands[cell] & mask) != 0) { home = cell; count++; }
                }
                if (alreadyPlaced || count != 1) continue;

                return new SolveStep
                {
                    Technique = "Hidden Single",
                    Tier = (int)TechniqueTier.Single,
                    Explanation = $"In {UnitName(unit)}, {digit} fits only in {CellName(home)}.",
                    Placements = new() { new Placement(home, digit) },
                    EvidenceCells = cells.ToList(),
                    BoardAfter = With(board, home, digit)
                };
            }
        }
        return null;
    }

    // ── Tier 2: Locked Candidates ────────────────────────────────────────────

    // Pointing: within a box, if every candidate cell for a digit lies on a
    // single row (or column), the digit must be in the box on that line — so it
    // can be removed from the rest of that line outside the box.
    internal static SolveStep? FindPointing(int[] board, int[] cands)
    {
        for (int box = 0; box < 9; box++)
        {
            int[] boxCells = UnitsList[18 + box];
            for (int digit = 1; digit <= 9; digit++)
            {
                int mask = 1 << (digit - 1);
                var holders = boxCells.Where(c => (cands[c] & mask) != 0).ToList();
                if (holders.Count < 2) continue;

                bool sameRow = holders.All(c => c / 9 == holders[0] / 9);
                bool sameCol = holders.All(c => c % 9 == holders[0] % 9);
                if (!sameRow && !sameCol) continue;

                int[] line = sameRow ? UnitsList[holders[0] / 9] : UnitsList[9 + holders[0] % 9];
                var elims = line.Where(c => c / 9 / 3 * 3 + c % 9 / 3 != box && (cands[c] & mask) != 0)
                                .Select(c => new Elimination(c, digit)).ToList();
                if (elims.Count == 0) continue;

                string lineName = sameRow ? $"row {holders[0] / 9 + 1}" : $"column {holders[0] % 9 + 1}";
                return new SolveStep
                {
                    Technique = holders.Count == 2 ? "Pointing Pair" : "Pointing Triple",
                    Tier = (int)TechniqueTier.LockedCandidate,
                    Explanation = $"In box {box + 1}, {digit} can only go in {lineName}, so it is removed from the rest of {lineName}.",
                    Eliminations = elims,
                    EvidenceCells = holders,
                    BoardAfter = (int[])board.Clone()
                };
            }
        }
        return null;
    }

    // Claiming (box-line reduction): within a row or column, if every candidate
    // cell for a digit lies in a single box, the digit must be in that box on
    // this line — so it can be removed from the rest of the box.
    internal static SolveStep? FindClaiming(int[] board, int[] cands)
    {
        for (int line = 0; line < 18; line++)   // 0-8 rows, 9-17 columns
        {
            int[] lineCells = UnitsList[line];
            for (int digit = 1; digit <= 9; digit++)
            {
                int mask = 1 << (digit - 1);
                var holders = lineCells.Where(c => (cands[c] & mask) != 0).ToList();
                if (holders.Count < 2) continue;

                int box = holders[0] / 9 / 3 * 3 + holders[0] % 9 / 3;
                if (!holders.All(c => c / 9 / 3 * 3 + c % 9 / 3 == box)) continue;

                var elims = UnitsList[18 + box]
                    .Where(c => !lineCells.Contains(c) && (cands[c] & mask) != 0)
                    .Select(c => new Elimination(c, digit)).ToList();
                if (elims.Count == 0) continue;

                return new SolveStep
                {
                    Technique = holders.Count == 2 ? "Claiming Pair" : "Claiming Triple",
                    Tier = (int)TechniqueTier.LockedCandidate,
                    Explanation = $"In {UnitName(line)}, {digit} only appears in box {box + 1}, so it is removed from the rest of box {box + 1}.",
                    Eliminations = elims,
                    EvidenceCells = holders,
                    BoardAfter = (int[])board.Clone()
                };
            }
        }
        return null;
    }

    // ── Tier 3: Subsets ──────────────────────────────────────────────────────

    // Naked subset: n cells in a unit whose candidates collectively use exactly n
    // digits. Those n digits are locked to those n cells, so they can be removed
    // from every other cell in the unit.
    internal static SolveStep? FindNakedSubset(int[] board, int[] cands, int n, string name)
    {
        for (int unit = 0; unit < 27; unit++)
        {
            // empty cells with 2..n candidates are the only ones that can be part
            // of a naked subset of size n.
            var pool = UnitsList[unit]
                .Where(c => board[c] == 0 && PopCount(cands[c]) is var p && p >= 2 && p <= n)
                .ToList();
            if (pool.Count < n) continue;

            foreach (var combo in Combinations(pool, n))
            {
                int union = 0;
                foreach (int c in combo) union |= cands[c];
                if (PopCount(union) != n) continue;

                var comboSet = new HashSet<int>(combo);
                var elims = new List<Elimination>();
                foreach (int c in UnitsList[unit])
                {
                    if (board[c] != 0 || comboSet.Contains(c)) continue;
                    foreach (int d in Digits(cands[c] & union)) elims.Add(new Elimination(c, d));
                }
                if (elims.Count == 0) continue;

                return new SolveStep
                {
                    Technique = name,
                    Tier = (int)TechniqueTier.Subset,
                    Explanation = $"In {UnitName(unit)}, {DigitList(union)} are confined to {CellList(combo)}, so they are removed from the other cells in {UnitName(unit)}.",
                    Eliminations = elims,
                    EvidenceCells = combo.ToList(),
                    BoardAfter = (int[])board.Clone()
                };
            }
        }
        return null;
    }

    // Hidden subset: n digits in a unit that, between them, only fit in the same
    // n cells. Those cells must hold exactly those digits, so every other
    // candidate can be removed from them.
    internal static SolveStep? FindHiddenSubset(int[] board, int[] cands, int n, string name)
    {
        for (int unit = 0; unit < 27; unit++)
        {
            int[] cells = UnitsList[unit];
            // digits still unplaced in this unit, mapped to the cells they fit in.
            var spots = new Dictionary<int, List<int>>();
            for (int d = 1; d <= 9; d++)
            {
                int mask = 1 << (d - 1);
                if (cells.Any(c => board[c] == d)) continue;
                var fit = cells.Where(c => board[c] == 0 && (cands[c] & mask) != 0).ToList();
                if (fit.Count >= 2 && fit.Count <= n) spots[d] = fit;
            }
            if (spots.Count < n) continue;

            foreach (var combo in Combinations(spots.Keys.ToList(), n))
            {
                var cellUnion = new HashSet<int>();
                foreach (int d in combo) cellUnion.UnionWith(spots[d]);
                if (cellUnion.Count != n) continue;

                int digitMask = 0;
                foreach (int d in combo) digitMask |= 1 << (d - 1);

                var elims = new List<Elimination>();
                foreach (int c in cellUnion)
                    foreach (int d in Digits(cands[c] & ~digitMask)) elims.Add(new Elimination(c, d));
                if (elims.Count == 0) continue;

                return new SolveStep
                {
                    Technique = name,
                    Tier = (int)TechniqueTier.Subset,
                    Explanation = $"In {UnitName(unit)}, {DigitList(digitMask)} only fit in {CellList(cellUnion)}, so other candidates are removed from those cells.",
                    Eliminations = elims,
                    EvidenceCells = cellUnion.ToList(),
                    BoardAfter = (int[])board.Clone()
                };
            }
        }
        return null;
    }

    // ── Tier 4: Fish (X-Wing / Swordfish / Jellyfish) ────────────────────────

    // Basic fish of size n for a digit: n "base" lines (all rows, or all columns)
    // on which the digit's candidates fall within the same n "cover" lines (the
    // perpendicular direction). The digit is then locked to the intersection
    // cells, so it can be removed from the cover lines outside the base lines.
    internal static SolveStep? FindFish(int[] board, int[] cands, int n, string name)
    {
        for (int digit = 1; digit <= 9; digit++)
        {
            var step = FishOneOrientation(board, cands, n, name, digit, byRow: true)
                    ?? FishOneOrientation(board, cands, n, name, digit, byRow: false);
            if (step != null) return step;
        }
        return null;
    }

    private static SolveStep? FishOneOrientation(int[] board, int[] cands, int n, string name, int digit, bool byRow)
    {
        int mask = 1 << (digit - 1);

        // For each base line, the set of cover indices (perpendicular positions)
        // where the digit is a candidate, as a 9-bit mask.
        var basePositions = new int[9];
        for (int line = 0; line < 9; line++)
        {
            int posMask = 0;
            for (int k = 0; k < 9; k++)
            {
                int cell = byRow ? line * 9 + k : k * 9 + line;
                if (board[cell] == 0 && (cands[cell] & mask) != 0) posMask |= 1 << k;
            }
            basePositions[line] = posMask;
        }

        // candidate base lines: 2..n positions (≥2 keeps it a genuine fish).
        var baseLines = Enumerable.Range(0, 9)
            .Where(l => PopCount(basePositions[l]) is var p && p >= 2 && p <= n).ToList();
        if (baseLines.Count < n) return null;

        foreach (var combo in Combinations(baseLines, n))
        {
            int cover = 0;
            foreach (int l in combo) cover |= basePositions[l];
            if (PopCount(cover) != n) continue;

            var baseSet = new HashSet<int>(combo);
            var elims = new List<Elimination>();
            var evidence = new List<int>();
            // collect evidence (the base intersection cells holding the digit)
            foreach (int l in combo)
                for (int k = 0; k < 9; k++)
                    if ((basePositions[l] & (1 << k)) != 0)
                        evidence.Add(byRow ? l * 9 + k : k * 9 + l);

            // eliminate the digit from cover lines on every NON-base line.
            foreach (int k in Digits(cover))   // k = cover position index (0-8)
            {
                int coverIdx = k;
                for (int line = 0; line < 9; line++)
                {
                    if (baseSet.Contains(line)) continue;
                    int cell = byRow ? line * 9 + coverIdx : coverIdx * 9 + line;
                    if (board[cell] == 0 && (cands[cell] & mask) != 0)
                        elims.Add(new Elimination(cell, digit));
                }
            }
            if (elims.Count == 0) continue;

            string baseKind = byRow ? "rows" : "columns";
            string coverKind = byRow ? "columns" : "rows";
            return new SolveStep
            {
                Technique = name,
                Tier = (int)TechniqueTier.Fish,
                Explanation = $"{name} on {digit}: in {baseKind} {LineList(combo)} the digit is confined to {coverKind} {string.Join(", ", Digits(cover))}, so it is removed elsewhere in those {coverKind}.",
                Eliminations = elims,
                EvidenceCells = evidence,
                BoardAfter = (int[])board.Clone()
            };
        }
        return null;
    }

    // ── Tier 5: Wings ────────────────────────────────────────────────────────

    // XY-Wing: a pivot cell with candidates {x,y}; two pincers seeing the pivot
    // with candidates {x,z} and {y,z}. Whatever the pivot is, one pincer becomes
    // z — so any cell seeing BOTH pincers cannot be z.
    internal static SolveStep? FindXyWing(int[] board, int[] cands)
    {
        var bivalue = Enumerable.Range(0, 81).Where(c => board[c] == 0 && PopCount(cands[c]) == 2).ToList();

        foreach (int pivot in bivalue)
        {
            var px = Digits(cands[pivot]).ToArray();   // [x, y]
            int x = px[0], y = px[1];

            var seen = PeerSet[pivot];
            var pincers = bivalue.Where(c => c != pivot && seen.Contains(c)).ToList();

            foreach (int p1 in pincers)
            foreach (int p2 in pincers)
            {
                if (p1 >= p2) continue;
                int m1 = cands[p1], m2 = cands[p2];
                // p1 must be {x,z}, p2 must be {y,z}: each shares exactly one of
                // x/y with the pivot, and they share the third digit z together.
                int shared = m1 & m2;
                if (PopCount(shared) != 1) continue;
                int z = BitOperations.TrailingZeroCount(shared) + 1;
                int zMask = shared;
                if ((cands[pivot] & zMask) != 0) continue;            // z must NOT be in the pivot
                int xb = 1 << (x - 1), yb = 1 << (y - 1);
                bool ok = (m1 == (xb | zMask) && m2 == (yb | zMask))
                       || (m1 == (yb | zMask) && m2 == (xb | zMask));
                if (!ok) continue;

                var elims = Enumerable.Range(0, 81)
                    .Where(c => board[c] == 0 && c != p1 && c != p2
                                && PeerSet[p1].Contains(c) && PeerSet[p2].Contains(c)
                                && (cands[c] & zMask) != 0)
                    .Select(c => new Elimination(c, z)).ToList();
                if (elims.Count == 0) continue;

                return new SolveStep
                {
                    Technique = "XY-Wing",
                    Tier = (int)TechniqueTier.Wing,
                    Explanation = $"XY-Wing: pivot {CellName(pivot)} with pincers {CellName(p1)} and {CellName(p2)} forces {z} out of every cell they both see.",
                    Eliminations = elims,
                    EvidenceCells = new() { pivot, p1, p2 },
                    BoardAfter = (int[])board.Clone()
                };
            }
        }
        return null;
    }

    // XYZ-Wing: a pivot with candidates {x,y,z}; two pincers seeing the pivot
    // with {x,z} and {y,z}. z appears in all three, so any cell seeing the pivot
    // AND both pincers cannot be z.
    internal static SolveStep? FindXyzWing(int[] board, int[] cands)
    {
        var trivalue = Enumerable.Range(0, 81).Where(c => board[c] == 0 && PopCount(cands[c]) == 3).ToList();
        var bivalue = Enumerable.Range(0, 81).Where(c => board[c] == 0 && PopCount(cands[c]) == 2).ToList();

        foreach (int pivot in trivalue)
        {
            var pincers = bivalue.Where(c => PeerSet[pivot].Contains(c) && (cands[c] & ~cands[pivot]) == 0).ToList();

            foreach (int p1 in pincers)
            foreach (int p2 in pincers)
            {
                if (p1 >= p2) continue;
                if ((cands[p1] | cands[p2]) != cands[pivot]) continue;   // pincers cover all of {x,y,z}
                int shared = cands[p1] & cands[p2];                       // {z}
                if (PopCount(shared) != 1) continue;
                int z = BitOperations.TrailingZeroCount(shared) + 1;

                var elims = Enumerable.Range(0, 81)
                    .Where(c => board[c] == 0 && c != pivot && c != p1 && c != p2
                                && PeerSet[pivot].Contains(c)
                                && PeerSet[p1].Contains(c) && PeerSet[p2].Contains(c)
                                && (cands[c] & shared) != 0)
                    .Select(c => new Elimination(c, z)).ToList();
                if (elims.Count == 0) continue;

                return new SolveStep
                {
                    Technique = "XYZ-Wing",
                    Tier = (int)TechniqueTier.Wing,
                    Explanation = $"XYZ-Wing: pivot {CellName(pivot)} with pincers {CellName(p1)} and {CellName(p2)} forces {z} out of every cell all three see.",
                    Eliminations = elims,
                    EvidenceCells = new() { pivot, p1, p2 },
                    BoardAfter = (int[])board.Clone()
                };
            }
        }
        return null;
    }

    // ── Candidate grid ───────────────────────────────────────────────────────

    // 9-bit candidate mask per cell (bit d-1 set => digit d still legal); 0 for
    // filled cells. This is the initial grid; the Solve loop mutates it in place.
    internal static int[] BuildCandidates(int[] board)
    {
        var cands = new int[81];
        for (int cell = 0; cell < 81; cell++)
            cands[cell] = board[cell] == 0 ? Candidates(board, cell) : 0;
        return cands;
    }

    private static int Candidates(int[] board, int cell)
    {
        int r = cell / 9, c = cell % 9, used = 0;
        for (int i = 0; i < 9; i++)
        {
            if (board[r * 9 + i] != 0) used |= 1 << (board[r * 9 + i] - 1);
            if (board[i * 9 + c] != 0) used |= 1 << (board[i * 9 + c] - 1);
        }
        int br = r / 3 * 3, bc = c / 3 * 3;
        for (int dr = 0; dr < 3; dr++)
        for (int dc = 0; dc < 3; dc++)
        {
            int v = board[(br + dr) * 9 + (bc + dc)];
            if (v != 0) used |= 1 << (v - 1);
        }
        return ~used & 0x1FF;
    }

    private static int[] With(int[] board, int cell, int digit)
    {
        var next = (int[])board.Clone();
        next[cell] = digit;
        return next;
    }

    // ── Bit / combinatorics helpers ──────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int PopCount(int mask) => BitOperations.PopCount((uint)mask);

    // digits (1-9) present in a 9-bit mask, ascending.
    private static IEnumerable<int> Digits(int mask)
    {
        while (mask != 0)
        {
            int d = BitOperations.TrailingZeroCount(mask);
            yield return d + 1;
            mask &= mask - 1;
        }
    }

    // all n-element combinations of a list (n is small: 2-4).
    private static IEnumerable<int[]> Combinations(List<int> items, int n)
    {
        var idx = new int[n];
        for (int i = 0; i < n; i++) idx[i] = i;
        if (items.Count < n) yield break;

        while (true)
        {
            var combo = new int[n];
            for (int i = 0; i < n; i++) combo[i] = items[idx[i]];
            yield return combo;

            int pos = n - 1;
            while (pos >= 0 && idx[pos] == items.Count - n + pos) pos--;
            if (pos < 0) yield break;
            idx[pos]++;
            for (int i = pos + 1; i < n; i++) idx[i] = idx[i - 1] + 1;
        }
    }

    // ── Units / peers (precomputed) ──────────────────────────────────────────

    // 27 units: 0-8 rows, 9-17 columns, 18-26 boxes.
    private static readonly int[][] UnitsList = BuildUnits();
    private static readonly int[][] Peers = BuildPeers();
    private static readonly HashSet<int>[] PeerSet = Peers.Select(p => new HashSet<int>(p)).ToArray();

    private static int[][] BuildUnits()
    {
        var units = new int[27][];
        for (int r = 0; r < 9; r++)
        {
            units[r] = new int[9];
            for (int c = 0; c < 9; c++) units[r][c] = r * 9 + c;
        }
        for (int c = 0; c < 9; c++)
        {
            units[9 + c] = new int[9];
            for (int r = 0; r < 9; r++) units[9 + c][r] = r * 9 + c;
        }
        for (int b = 0; b < 9; b++)
        {
            units[18 + b] = new int[9];
            int br = b / 3 * 3, bc = b % 3 * 3;
            for (int i = 0; i < 9; i++) units[18 + b][i] = (br + i / 3) * 9 + (bc + i % 3);
        }
        return units;
    }

    private static int[][] BuildPeers()
    {
        var peers = new int[81][];
        for (int cell = 0; cell < 81; cell++)
        {
            int r = cell / 9, c = cell % 9, b = r / 3 * 3 + c / 3;
            var set = new HashSet<int>();
            set.UnionWith(UnitsList[r]);
            set.UnionWith(UnitsList[9 + c]);
            set.UnionWith(UnitsList[18 + b]);
            set.Remove(cell);
            peers[cell] = set.ToArray();
        }
        return peers;
    }

    // ── Naming helpers (for explanations) ────────────────────────────────────

    private static string CellName(int cell) => $"R{cell / 9 + 1}C{cell % 9 + 1}";

    private static string CellList(IEnumerable<int> cells) =>
        string.Join(", ", cells.OrderBy(c => c).Select(CellName));

    private static string DigitList(int mask) => string.Join(", ", Digits(mask));

    private static string LineList(IEnumerable<int> lines) =>
        string.Join(", ", lines.OrderBy(l => l).Select(l => (l + 1).ToString()));

    private static string UnitName(int unit) =>
        unit < 9  ? $"row {unit + 1}" :
        unit < 18 ? $"column {unit - 8}" :
                    $"box {unit - 17}";
}
