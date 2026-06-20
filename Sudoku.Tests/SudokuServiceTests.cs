using Sudoku.Services;

namespace Sudoku.Tests;

public class SudokuServiceTests
{
    private readonly SudokuService _svc = new();

    // ── Generation ────────────────────────────────────────────────────────────

    // Note: we intentionally avoid clue counts near the 17-clue floor here.
    // Generating a *minimal* puzzle is pathologically slow — almost every cell
    // removal near the floor fails the uniqueness check, and each failure runs a
    // full dual-solution backtracking search. The uniqueness property holds at
    // any clue count, so mid-range counts give the same coverage cheaply.
    [Theory]
    [InlineData(30)]
    [InlineData(45)]
    public void GeneratePuzzle_HasUniqueSolution(int clues)
    {
        var puzzle = _svc.GeneratePuzzle(clues);

        Assert.Equal(81, puzzle.Length);
        Assert.Equal(1, _svc.CountSolutions(puzzle, cap: 2));
    }

    [Fact]
    public void GeneratePuzzle_HasNoConflicts()
    {
        var puzzle = _svc.GeneratePuzzle(30);
        Assert.Empty(_svc.GetConflicts(puzzle));
    }

    [Fact]
    public void GeneratePuzzle_ClampsCluesAboveMaximum()
    {
        // clues > 80 are clamped to 80 (only 1 cell removed — fast), and the
        // result is still a valid, unique puzzle.
        var puzzle = _svc.GeneratePuzzle(200);
        Assert.Equal(81, puzzle.Length);
        Assert.Equal(1, _svc.CountSolutions(puzzle, cap: 2));
    }

    // ── Solving ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Solve_FillsBoardCompletelyAndValidly()
    {
        var puzzle = _svc.GeneratePuzzle(30);
        var solved = _svc.Solve(puzzle);

        Assert.NotNull(solved);
        Assert.DoesNotContain(0, solved!);
        Assert.Empty(_svc.GetConflicts(solved));
    }

    [Fact]
    public void Solve_PreservesGivenClues()
    {
        var puzzle = _svc.GeneratePuzzle(30);
        var solved = _svc.Solve(puzzle)!;

        for (int i = 0; i < 81; i++)
            if (puzzle[i] != 0)
                Assert.Equal(puzzle[i], solved[i]);
    }

    [Fact]
    public void Solve_ReturnsNull_ForUnsolvableBoard()
    {
        // Two 5s in the top row makes the board contradictory.
        var board = new int[81];
        board[0] = 5;
        board[1] = 5;
        Assert.Null(_svc.Solve(board));
    }

    [Fact]
    public void Solve_EmptyBoard_ProducesValidSolution()
    {
        var solved = _svc.Solve(new int[81]);
        Assert.NotNull(solved);
        Assert.Empty(_svc.GetConflicts(solved!));
    }

    // ── Solution counting ────────────────────────────────────────────────────────

    [Fact]
    public void CountSolutions_EmptyBoard_HitsCap()
    {
        // Empty board has billions of solutions; counting is capped.
        Assert.Equal(2, _svc.CountSolutions(new int[81], cap: 2));
    }

    [Fact]
    public void CountSolutions_RespectsCap()
    {
        Assert.Equal(5, _svc.CountSolutions(new int[81], cap: 5));
    }

    // ── Conflicts ─────────────────────────────────────────────────────────────────

    [Fact]
    public void GetConflicts_EmptyBoard_HasNone()
    {
        Assert.Empty(_svc.GetConflicts(new int[81]));
    }

    [Fact]
    public void GetConflicts_DetectsRowDuplicate()
    {
        var board = new int[81];
        board[0] = 7; // row 0, col 0
        board[8] = 7; // row 0, col 8
        var conflicts = _svc.GetConflicts(board);
        Assert.Contains(0, conflicts);
        Assert.Contains(8, conflicts);
    }

    [Fact]
    public void GetConflicts_DetectsColumnDuplicate()
    {
        var board = new int[81];
        board[0] = 3;       // row 0, col 0
        board[9 * 8] = 3;   // row 8, col 0
        var conflicts = _svc.GetConflicts(board);
        Assert.Contains(0, conflicts);
        Assert.Contains(9 * 8, conflicts);
    }

    [Fact]
    public void GetConflicts_DetectsBoxDuplicate()
    {
        var board = new int[81];
        board[0] = 4;   // box 0, top-left
        board[10] = 4;  // box 0, (row 1, col 1) — same box, different row & col
        var conflicts = _svc.GetConflicts(board);
        Assert.Contains(0, conflicts);
        Assert.Contains(10, conflicts);
    }

    // ── Givens ─────────────────────────────────────────────────────────────────

    [Fact]
    public void GetGivens_MarksNonZeroCells()
    {
        var board = new int[81];
        board[0] = 1;
        board[80] = 9;
        var givens = _svc.GetGivens(board);

        Assert.True(givens[0]);
        Assert.True(givens[80]);
        Assert.False(givens[40]);
    }
}
