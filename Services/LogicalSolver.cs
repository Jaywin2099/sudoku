using System.Numerics;

namespace Sudoku.Services;

// A deductive, human-style solver. It applies named techniques in increasing
// order of difficulty and records each one as a SolveStep. Unlike the
// backtracking SudokuService (used for generation / uniqueness / the Solve
// button), this solver NEVER guesses — when it can find no forced move it stops,
// which is itself the useful signal that the puzzle needs a harder technique
// than it currently knows.
//
// Skeleton scope: Naked Single + Hidden Single, which together fully solve most
// easy/medium puzzles. Add a technique by writing a finder and slotting it into
// the FindNextStep chain in difficulty order; everything downstream (the step
// list, and later the branching graph + grading) reads the same SolveStep shape.
public class LogicalSolver
{
    public SolveResult Solve(int[] puzzle)
    {
        var board = (int[])puzzle.Clone();
        var steps = new List<SolveStep>();

        while (Array.IndexOf(board, 0) != -1)
        {
            var step = FindNextStep(board);
            if (step == null) break;          // stuck: needs a technique we don't have yet
            board = step.BoardAfter;          // BoardAfter is already a fresh snapshot
            steps.Add(step);
        }

        bool solved = Array.IndexOf(board, 0) == -1;
        return new SolveResult(solved, steps, board);
    }

    // Cheapest technique first. Each finder returns the first move it spots, or null.
    private static SolveStep? FindNextStep(int[] board) =>
        FindNakedSingle(board) ?? FindHiddenSingle(board);

    // Naked Single: an empty cell with exactly one remaining candidate.
    private static SolveStep? FindNakedSingle(int[] board)
    {
        for (int cell = 0; cell < 81; cell++)
        {
            if (board[cell] != 0) continue;
            int cand = Candidates(board, cell);
            if (cand == 0 || (cand & (cand - 1)) != 0) continue;   // need exactly one bit set

            int digit = BitOperations.TrailingZeroCount(cand) + 1;
            int r = cell / 9, c = cell % 9;
            return new SolveStep
            {
                Technique = "Naked Single",
                Tier = (int)TechniqueTier.Single,
                Explanation = $"R{r + 1}C{c + 1} has only one candidate left: {digit}.",
                Placements = new() { new Placement(cell, digit) },
                EvidenceCells = new() { cell },
                BoardAfter = With(board, cell, digit)
            };
        }
        return null;
    }

    // Hidden Single: within a unit (row/col/box) a digit has exactly one cell it
    // can legally go, even if that cell has other candidates too.
    private static SolveStep? FindHiddenSingle(int[] board)
    {
        for (int unit = 0; unit < 27; unit++)
        {
            int[] cells = UnitCells(unit);
            for (int digit = 1; digit <= 9; digit++)
            {
                int mask = 1 << (digit - 1);
                int home = -1, count = 0;
                bool alreadyPlaced = false;

                foreach (int cell in cells)
                {
                    if (board[cell] == digit) { alreadyPlaced = true; break; }
                    if (board[cell] == 0 && (Candidates(board, cell) & mask) != 0) { home = cell; count++; }
                }
                if (alreadyPlaced || count != 1) continue;

                int r = home / 9, c = home % 9;
                return new SolveStep
                {
                    Technique = "Hidden Single",
                    Tier = (int)TechniqueTier.Single,
                    Explanation = $"In {UnitName(unit)}, {digit} fits only in R{r + 1}C{c + 1}.",
                    Placements = new() { new Placement(home, digit) },
                    EvidenceCells = cells.ToList(),
                    BoardAfter = With(board, home, digit)
                };
            }
        }
        return null;
    }

    // 9-bit candidate mask for an empty cell (bit d-1 set => digit d is still legal).
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

    // unit 0-8 = rows, 9-17 = columns, 18-26 = boxes.
    private static int[] UnitCells(int unit)
    {
        var cells = new int[9];
        if (unit < 9)
            for (int c = 0; c < 9; c++) cells[c] = unit * 9 + c;
        else if (unit < 18)
            for (int r = 0; r < 9; r++) cells[r] = r * 9 + (unit - 9);
        else
        {
            int b = unit - 18, br = b / 3 * 3, bc = b % 3 * 3;
            for (int i = 0; i < 9; i++) cells[i] = (br + i / 3) * 9 + (bc + i % 3);
        }
        return cells;
    }

    private static string UnitName(int unit) =>
        unit < 9  ? $"row {unit + 1}" :
        unit < 18 ? $"column {unit - 8}" :
                    $"box {unit - 17}";
}
