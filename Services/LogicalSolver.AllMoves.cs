namespace Sudoku.Services;

// One available move for the move-tree explorer. Either a PLACEMENT (a single —
// fills a cell) or an ELIMINATION (every other technique — strikes candidates,
// which later unlocks singles). Carries both what it concluded and the evidence it
// reasoned from: the cells AND the rows/cols/boxes (EvidenceUnits, unit indices:
// 0-8 rows, 9-17 cols, 18-26 boxes) that were used to reach it — so a view can
// light up exactly what a pro looked at. BoardAfter/CandsAfter are the full state
// after the move, so the explorer can advance the path with no recomputation
// (eliminations leave the board unchanged but change the candidates).
public record MoveOption(
    string Technique,
    int Tier,
    bool Places,
    List<Placement> Placements,
    List<Elimination> Eliminations,
    List<int> EvidenceCells,
    List<int> EvidenceUnits,
    string Explanation,
    int[] BoardAfter,
    int[] CandsAfter);

public partial class LogicalSolver
{
    // EVERY legal move on a board, across every technique the solver knows, given a
    // candidate grid (computed fresh from the board if not supplied — so the caller
    // can thread eliminations from earlier moves forward). Singles come first
    // (placements), then locked candidates, subsets, fish, wings (eliminations),
    // ordered by tier then primary cell. This is the view-agnostic model behind the
    // move tree: a board+candidate state -> all the ways to make progress from it.
    public List<MoveOption> AllMovesFull(int[] board, int[]? cands = null)
    {
        cands ??= BuildCandidates(board);
        var moves = new List<MoveOption>();
        var placed = new HashSet<int>();

        AddNakedSingles(board, cands, moves, placed);
        AddHiddenSingles(board, cands, moves, placed);
        AddPointing(board, cands, moves);
        AddClaiming(board, cands, moves);
        AddNakedSubset(board, cands, 2, "Naked Pair", moves);
        AddNakedSubset(board, cands, 3, "Naked Triple", moves);
        AddNakedSubset(board, cands, 4, "Naked Quad", moves);
        AddHiddenSubset(board, cands, 2, "Hidden Pair", moves);
        AddHiddenSubset(board, cands, 3, "Hidden Triple", moves);
        AddHiddenSubset(board, cands, 4, "Hidden Quad", moves);
        AddFish(board, cands, 2, "X-Wing", moves);
        AddFish(board, cands, 3, "Swordfish", moves);
        AddFish(board, cands, 4, "Jellyfish", moves);
        AddXyWing(board, cands, moves);
        AddXyzWing(board, cands, moves);

        moves.Sort((a, b) => a.Tier != b.Tier ? a.Tier - b.Tier : KeyCell(a) - KeyCell(b));
        return moves;
    }

    private static int KeyCell(MoveOption m) =>
        m.Places ? m.Placements[0].Cell : (m.Eliminations.Count > 0 ? m.Eliminations[0].Cell : 0);

    // ── State transitions ────────────────────────────────────────────────────
    private static MoveOption MakePlacement(int[] board, int[] cands, string tech, int tier,
        int cell, int digit, List<int> evidence, List<int> units, string explanation)
    {
        var nb = (int[])board.Clone(); nb[cell] = digit;
        var nc = (int[])cands.Clone(); nc[cell] = 0;
        int mask = 1 << (digit - 1);
        foreach (int p in Peers[cell]) nc[p] &= ~mask;
        return new MoveOption(tech, tier, true, new() { new Placement(cell, digit) }, new(),
            evidence, units, explanation, nb, nc);
    }

    private static MoveOption MakeElimination(int[] board, int[] cands, string tech, int tier,
        List<Elimination> elims, List<int> evidence, List<int> units, string explanation)
    {
        var nc = (int[])cands.Clone();
        foreach (var e in elims) nc[e.Cell] &= ~(1 << (e.Digit - 1));
        return new MoveOption(tech, tier, false, new(), elims, evidence, units, explanation,
            (int[])board.Clone(), nc);   // board unchanged — eliminations don't place
    }

    private static List<int> CellUnits(int cell) =>
        new() { cell / 9, 9 + cell % 9, 18 + (cell / 9 / 3 * 3 + cell % 9 / 3) };

    // ── Tier 1: Singles (placements) ─────────────────────────────────────────
    private static void AddNakedSingles(int[] board, int[] cands, List<MoveOption> moves, HashSet<int> placed)
    {
        for (int cell = 0; cell < 81; cell++)
        {
            if (board[cell] != 0) continue;
            int c = cands[cell];
            if (c == 0 || (c & (c - 1)) != 0) continue;
            int digit = Digits(c).First();
            placed.Add(cell);
            moves.Add(MakePlacement(board, cands, "Naked Single", (int)TechniqueTier.Single,
                cell, digit, new() { cell }, CellUnits(cell),
                $"{CellName(cell)} has only one candidate left: {digit}."));
        }
    }

    private static void AddHiddenSingles(int[] board, int[] cands, List<MoveOption> moves, HashSet<int> placed)
    {
        for (int unit = 0; unit < 27; unit++)
        {
            int[] cells = UnitsList[unit];
            for (int digit = 1; digit <= 9; digit++)
            {
                int mask = 1 << (digit - 1);
                int home = -1, count = 0;
                bool already = false;
                foreach (int cell in cells)
                {
                    if (board[cell] == digit) { already = true; break; }
                    if (board[cell] == 0 && (cands[cell] & mask) != 0) { home = cell; count++; }
                }
                if (already || count != 1 || placed.Contains(home)) continue;
                placed.Add(home);
                moves.Add(MakePlacement(board, cands, "Hidden Single", (int)TechniqueTier.Single,
                    home, digit, new() { home }, new() { unit },
                    $"In {UnitName(unit)}, {digit} fits only in {CellName(home)}."));
            }
        }
    }

    // ── Tier 2: Locked candidates (eliminations) ─────────────────────────────
    private static void AddPointing(int[] board, int[] cands, List<MoveOption> moves)
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

                int lineUnit = sameRow ? holders[0] / 9 : 9 + holders[0] % 9;
                var elims = UnitsList[lineUnit]
                    .Where(c => (c / 9 / 3 * 3 + c % 9 / 3) != box && (cands[c] & mask) != 0)
                    .Select(c => new Elimination(c, digit)).ToList();
                if (elims.Count == 0) continue;

                string lineName = sameRow ? $"row {holders[0] / 9 + 1}" : $"column {holders[0] % 9 + 1}";
                moves.Add(MakeElimination(board, cands,
                    holders.Count == 2 ? "Pointing Pair" : "Pointing Triple", (int)TechniqueTier.LockedCandidate,
                    elims, holders, new() { 18 + box, lineUnit },
                    $"In box {box + 1}, {digit} can only go in {lineName}, so it is removed from the rest of {lineName}."));
            }
        }
    }

    private static void AddClaiming(int[] board, int[] cands, List<MoveOption> moves)
    {
        for (int line = 0; line < 18; line++)
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

                moves.Add(MakeElimination(board, cands,
                    holders.Count == 2 ? "Claiming Pair" : "Claiming Triple", (int)TechniqueTier.LockedCandidate,
                    elims, holders, new() { line, 18 + box },
                    $"In {UnitName(line)}, {digit} only appears in box {box + 1}, so it is removed from the rest of box {box + 1}."));
            }
        }
    }

    // ── Tier 3: Subsets (eliminations) ───────────────────────────────────────
    private static void AddNakedSubset(int[] board, int[] cands, int n, string name, List<MoveOption> moves)
    {
        for (int unit = 0; unit < 27; unit++)
        {
            var pool = UnitsList[unit]
                .Where(c => board[c] == 0 && PopCount(cands[c]) is var p && p >= 2 && p <= n).ToList();
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

                moves.Add(MakeElimination(board, cands, name, (int)TechniqueTier.Subset,
                    elims, combo.ToList(), new() { unit },
                    $"In {UnitName(unit)}, {DigitList(union)} are confined to {CellList(combo)}, so they are removed from the other cells in {UnitName(unit)}."));
            }
        }
    }

    private static void AddHiddenSubset(int[] board, int[] cands, int n, string name, List<MoveOption> moves)
    {
        for (int unit = 0; unit < 27; unit++)
        {
            int[] cells = UnitsList[unit];
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

                moves.Add(MakeElimination(board, cands, name, (int)TechniqueTier.Subset,
                    elims, cellUnion.ToList(), new() { unit },
                    $"In {UnitName(unit)}, {DigitList(digitMask)} only fit in {CellList(cellUnion)}, so other candidates are removed from those cells."));
            }
        }
    }

    // ── Tier 4: Fish (eliminations) ──────────────────────────────────────────
    private static void AddFish(int[] board, int[] cands, int n, string name, List<MoveOption> moves)
    {
        for (int digit = 1; digit <= 9; digit++)
        {
            AddFishOrientation(board, cands, n, name, digit, byRow: true, moves);
            AddFishOrientation(board, cands, n, name, digit, byRow: false, moves);
        }
    }

    private static void AddFishOrientation(int[] board, int[] cands, int n, string name, int digit, bool byRow, List<MoveOption> moves)
    {
        int mask = 1 << (digit - 1);
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

        var baseLines = Enumerable.Range(0, 9)
            .Where(l => PopCount(basePositions[l]) is var p && p >= 2 && p <= n).ToList();
        if (baseLines.Count < n) return;

        foreach (var combo in Combinations(baseLines, n))
        {
            int cover = 0;
            foreach (int l in combo) cover |= basePositions[l];
            if (PopCount(cover) != n) continue;

            var baseSet = new HashSet<int>(combo);
            var evidence = new List<int>();
            foreach (int l in combo)
                for (int k = 0; k < 9; k++)
                    if ((basePositions[l] & (1 << k)) != 0)
                        evidence.Add(byRow ? l * 9 + k : k * 9 + l);

            // eliminate the digit on each cover position (0-based), on every non-base line.
            var elims = new List<Elimination>();
            for (int k = 0; k < 9; k++)
            {
                if ((cover & (1 << k)) == 0) continue;
                for (int line = 0; line < 9; line++)
                {
                    if (baseSet.Contains(line)) continue;
                    int cell = byRow ? line * 9 + k : k * 9 + line;
                    if (board[cell] == 0 && (cands[cell] & mask) != 0) elims.Add(new Elimination(cell, digit));
                }
            }
            if (elims.Count == 0) continue;

            // units: base lines + cover lines (perpendicular).
            var units = new List<int>();
            foreach (int l in combo) units.Add(byRow ? l : 9 + l);
            for (int k = 0; k < 9; k++) if ((cover & (1 << k)) != 0) units.Add(byRow ? 9 + k : k);

            string baseKind = byRow ? "rows" : "columns";
            string coverKind = byRow ? "columns" : "rows";
            moves.Add(MakeElimination(board, cands, name, (int)TechniqueTier.Fish,
                elims, evidence, units,
                $"{name} on {digit}: in {baseKind} {LineList(combo)} the digit is confined to {coverKind} {CoverList(cover)}, so it is removed elsewhere in those {coverKind}."));
        }
    }

    private static string CoverList(int cover)
    {
        var ks = new List<int>();
        for (int k = 0; k < 9; k++) if ((cover & (1 << k)) != 0) ks.Add(k + 1);
        return string.Join(", ", ks);
    }

    // ── Tier 5: Wings (eliminations) ─────────────────────────────────────────
    private static void AddXyWing(int[] board, int[] cands, List<MoveOption> moves)
    {
        var bivalue = Enumerable.Range(0, 81).Where(c => board[c] == 0 && PopCount(cands[c]) == 2).ToList();
        foreach (int pivot in bivalue)
        {
            var px = Digits(cands[pivot]).ToArray();
            int x = px[0], y = px[1];
            var seen = PeerSet[pivot];
            var pincers = bivalue.Where(c => c != pivot && seen.Contains(c)).ToList();

            foreach (int p1 in pincers)
            foreach (int p2 in pincers)
            {
                if (p1 >= p2) continue;
                int m1 = cands[p1], m2 = cands[p2];
                int shared = m1 & m2;
                if (PopCount(shared) != 1) continue;
                int z = Digits(shared).First();
                if ((cands[pivot] & shared) != 0) continue;
                int xb = 1 << (x - 1), yb = 1 << (y - 1);
                bool ok = (m1 == (xb | shared) && m2 == (yb | shared))
                       || (m1 == (yb | shared) && m2 == (xb | shared));
                if (!ok) continue;

                var elims = Enumerable.Range(0, 81)
                    .Where(c => board[c] == 0 && c != p1 && c != p2
                                && PeerSet[p1].Contains(c) && PeerSet[p2].Contains(c) && (cands[c] & shared) != 0)
                    .Select(c => new Elimination(c, z)).ToList();
                if (elims.Count == 0) continue;

                moves.Add(MakeElimination(board, cands, "XY-Wing", (int)TechniqueTier.Wing,
                    elims, new() { pivot, p1, p2 }, new(),
                    $"XY-Wing: pivot {CellName(pivot)} with pincers {CellName(p1)} and {CellName(p2)} forces {z} out of every cell they both see."));
            }
        }
    }

    private static void AddXyzWing(int[] board, int[] cands, List<MoveOption> moves)
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
                if ((cands[p1] | cands[p2]) != cands[pivot]) continue;
                int shared = cands[p1] & cands[p2];
                if (PopCount(shared) != 1) continue;
                int z = Digits(shared).First();

                var elims = Enumerable.Range(0, 81)
                    .Where(c => board[c] == 0 && c != pivot && c != p1 && c != p2
                                && PeerSet[pivot].Contains(c) && PeerSet[p1].Contains(c) && PeerSet[p2].Contains(c)
                                && (cands[c] & shared) != 0)
                    .Select(c => new Elimination(c, z)).ToList();
                if (elims.Count == 0) continue;

                moves.Add(MakeElimination(board, cands, "XYZ-Wing", (int)TechniqueTier.Wing,
                    elims, new() { pivot, p1, p2 }, new(),
                    $"XYZ-Wing: pivot {CellName(pivot)} with pincers {CellName(p1)} and {CellName(p2)} forces {z} out of every cell all three see."));
            }
        }
    }
}
