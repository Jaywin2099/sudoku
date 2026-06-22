using Sudoku.Services;

namespace Sudoku.Tests;

// Tests for the deductive, human-style solver. Two layers:
//
//  1. Soundness — run the full solver on real puzzles and assert it NEVER places
//     a digit that disagrees with the puzzle's unique solution. This is the
//     strongest net: an unsound elimination in any technique eventually forces a
//     wrong single, which this catches without needing per-technique fixtures.
//
//  2. Targeted finders — exercise each new technique on a hand-built candidate
//     grid (an all-empty board plus a crafted `cands` array) so the exact pattern
//     and the exact eliminations it must produce are pinned down. The finders are
//     `internal` and reachable via InternalsVisibleTo("Sudoku.Tests").
public class LogicalSolverTests
{
    private readonly SudokuService _svc = new();
    private readonly LogicalSolver _logical = new();

    // ── Soundness / integration ──────────────────────────────────────────────

    [Fact]
    public void Solve_NeverPlacesADigitThatDisagreesWithTheSolution()
    {
        // Many independent puzzles; every placement the logical solver makes must
        // match the (unique) brute-force solution.
        for (int seed = 0; seed < 25; seed++)
        {
            var puzzle = _svc.GeneratePuzzle(30);
            var solution = _svc.Solve(puzzle)!;
            var result = _logical.Solve(puzzle);

            foreach (var step in result.Steps)
                foreach (var p in step.Placements)
                    Assert.Equal(solution[p.Cell], p.Digit);

            // and the board it ends on must agree with the solution everywhere
            // it has filled in.
            for (int i = 0; i < 81; i++)
                if (result.FinalBoard[i] != 0)
                    Assert.Equal(solution[i], result.FinalBoard[i]);
        }
    }

    [Fact]
    public void Solve_FullySolvesEasyPuzzles()
    {
        // Higher clue counts are reliably within reach of the implemented
        // techniques; assert a clean, complete, conflict-free solve.
        for (int seed = 0; seed < 10; seed++)
        {
            var puzzle = _svc.GeneratePuzzle(40);
            var result = _logical.Solve(puzzle);

            Assert.True(result.Solved);
            Assert.DoesNotContain(0, result.FinalBoard);
            Assert.Empty(_svc.GetConflicts(result.FinalBoard));
        }
    }

    [Fact]
    public void Solve_StepsAreInNonDecreasingTierOrderIsNotRequired_ButEachStepMakesProgress()
    {
        // Every recorded step must actually change something: a placement, or at
        // least one elimination. (Guards against a finder that spins on a pattern
        // without making progress.)
        var puzzle = _svc.GeneratePuzzle(28);
        var result = _logical.Solve(puzzle);

        foreach (var step in result.Steps)
            Assert.True(step.Placements.Count > 0 || step.Eliminations.Count > 0,
                $"step '{step.Technique}' made no progress");
    }

    // ── Targeted finder tests ────────────────────────────────────────────────

    [Fact]
    public void NakedPair_RemovesThePairDigitsFromTheRestOfTheUnit()
    {
        var board = new int[81];
        var cands = new int[81];
        // Row 0: cells 0 & 1 are a naked pair {1,2}; the rest of the row carries
        // those digits among others and must lose them.
        cands[0] = Mask(1, 2);
        cands[1] = Mask(1, 2);
        for (int c = 2; c < 9; c++) cands[c] = Mask(1, 2, 3, 4, 5);

        var step = LogicalSolver.FindNakedSubset(board, cands, 2, "Naked Pair");

        Assert.NotNull(step);
        Assert.Equal("Naked Pair", step!.Technique);
        Assert.Equal((int)TechniqueTier.Subset, step.Tier);
        // every other cell in the row loses 1 and 2 (7 cells × 2 digits)
        Assert.Equal(14, step.Eliminations.Count);
        Assert.Contains(step.Eliminations, e => e.Cell == 2 && e.Digit == 1);
        Assert.Contains(step.Eliminations, e => e.Cell == 8 && e.Digit == 2);
        Assert.DoesNotContain(step.Eliminations, e => e.Cell == 0 || e.Cell == 1);
    }

    [Fact]
    public void HiddenPair_RemovesOtherCandidatesFromThePairCells()
    {
        var board = new int[81];
        var cands = new int[81];
        // Row 0: digits 8 and 9 only fit in cells 0 and 1 (hidden pair), but those
        // cells also carry junk candidates that must be removed.
        cands[0] = Mask(8, 9, 1, 2);
        cands[1] = Mask(8, 9, 3, 4);
        for (int c = 2; c < 9; c++) cands[c] = Mask(1, 2, 3, 4, 5, 6, 7);

        var step = LogicalSolver.FindHiddenSubset(board, cands, 2, "Hidden Pair");

        Assert.NotNull(step);
        Assert.Equal("Hidden Pair", step!.Technique);
        // cell 0 loses 1,2 ; cell 1 loses 3,4
        Assert.Contains(step.Eliminations, e => e.Cell == 0 && e.Digit == 1);
        Assert.Contains(step.Eliminations, e => e.Cell == 0 && e.Digit == 2);
        Assert.Contains(step.Eliminations, e => e.Cell == 1 && e.Digit == 3);
        Assert.Contains(step.Eliminations, e => e.Cell == 1 && e.Digit == 4);
        // 8 and 9 are NEVER removed — they are the locked pair
        Assert.DoesNotContain(step.Eliminations, e => e.Digit == 8 || e.Digit == 9);
    }

    [Fact]
    public void Pointing_RemovesDigitFromTheRestOfTheLine()
    {
        var board = new int[81];
        var cands = new int[81];
        // Box 0: digit 5 is a candidate only in cells 0 & 1 (both in row 0).
        cands[0] = Mask(5, 6);
        cands[1] = Mask(5, 6);
        cands[2] = Mask(6, 7);
        foreach (int c in new[] { 9, 10, 11, 18, 19, 20 }) cands[c] = Mask(6, 7);
        // Rest of row 0 carries digit 5 and must lose it.
        for (int c = 3; c < 9; c++) cands[c] = Mask(5, 8);

        var step = LogicalSolver.FindPointing(board, cands);

        Assert.NotNull(step);
        Assert.StartsWith("Pointing", step!.Technique);
        Assert.Equal((int)TechniqueTier.LockedCandidate, step.Tier);
        Assert.Equal(6, step.Eliminations.Count);       // cells 3..8
        Assert.All(step.Eliminations, e => Assert.Equal(5, e.Digit));
        Assert.Contains(step.Eliminations, e => e.Cell == 3);
        Assert.Contains(step.Eliminations, e => e.Cell == 8);
    }

    [Fact]
    public void Claiming_RemovesDigitFromTheRestOfTheBox()
    {
        var board = new int[81];
        var cands = new int[81];
        // Row 0: digit 4 only appears in cells 0 & 1, which both sit in box 0.
        cands[0] = Mask(4, 6);
        cands[1] = Mask(4, 6);
        for (int c = 2; c < 9; c++) cands[c] = Mask(6, 7);   // no 4 elsewhere in row 0
        // Box 0 cells outside row 0 carry 4 and must lose it.
        foreach (int c in new[] { 9, 10, 11, 18, 19, 20 }) cands[c] = Mask(4, 8);

        var step = LogicalSolver.FindClaiming(board, cands);

        Assert.NotNull(step);
        Assert.StartsWith("Claiming", step!.Technique);
        Assert.Equal(6, step.Eliminations.Count);
        Assert.All(step.Eliminations, e => Assert.Equal(4, e.Digit));
        Assert.Contains(step.Eliminations, e => e.Cell == 9);
        Assert.Contains(step.Eliminations, e => e.Cell == 20);
    }

    [Fact]
    public void XWing_RemovesDigitFromTheCoverColumns()
    {
        var board = new int[81];
        var cands = new int[81];
        for (int i = 0; i < 81; i++) cands[i] = Mask(1, 2);   // background, no 7

        // Digit 7 forms an X-Wing on rows 0 & 4 in columns 2 & 5.
        cands[0 * 9 + 2] = Mask(7, 1);
        cands[0 * 9 + 5] = Mask(7, 1);
        cands[4 * 9 + 2] = Mask(7, 1);
        cands[4 * 9 + 5] = Mask(7, 1);
        // A victim: row 1, column 2 carries 7 but is not part of the X-Wing.
        cands[1 * 9 + 2] = Mask(7, 1);

        var step = LogicalSolver.FindFish(board, cands, 2, "X-Wing");

        Assert.NotNull(step);
        Assert.Equal("X-Wing", step!.Technique);
        Assert.Equal((int)TechniqueTier.Fish, step.Tier);
        Assert.Contains(step.Eliminations, e => e.Cell == 1 * 9 + 2 && e.Digit == 7);
        // the four corner cells must NOT be eliminated
        Assert.DoesNotContain(step.Eliminations, e => e.Cell == 0 * 9 + 2);
        Assert.DoesNotContain(step.Eliminations, e => e.Cell == 4 * 9 + 5);
    }

    [Fact]
    public void XyWing_RemovesTheSharedDigitFromCellsSeeingBothPincers()
    {
        var board = new int[81];
        var cands = new int[81];
        for (int i = 0; i < 81; i++) cands[i] = Mask(5, 6, 7);   // background (not bivalue)

        // pivot R1C1 {1,2}; pincers R1C2 {1,3} and R2C1 {2,3}; victim R2C2 has 3.
        cands[0]  = Mask(1, 2);   // pivot
        cands[1]  = Mask(1, 3);   // pincer 1 (shares row with pivot)
        cands[9]  = Mask(2, 3);   // pincer 2 (shares column with pivot)
        cands[10] = Mask(3, 4);   // victim (sees both pincers)

        var step = LogicalSolver.FindXyWing(board, cands);

        Assert.NotNull(step);
        Assert.Equal("XY-Wing", step!.Technique);
        Assert.Equal((int)TechniqueTier.Wing, step.Tier);
        Assert.Contains(step.Eliminations, e => e.Cell == 10 && e.Digit == 3);
    }

    private static int Mask(params int[] digits)
    {
        int m = 0;
        foreach (int d in digits) m |= 1 << (d - 1);
        return m;
    }
}
