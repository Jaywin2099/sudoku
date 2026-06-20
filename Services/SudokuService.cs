namespace Sudoku.Services;

public class SudokuService
{
    private static readonly Random Rng = new();

    // Returns a 81-element array (row-major). 0 = empty.
    public int[] GeneratePuzzle(int clues = 30)
    {
        var solved = new int[81];
        FillBoard(solved);

        var puzzle = (int[])solved.Clone();
        var positions = Enumerable.Range(0, 81).OrderBy(_ => Rng.Next()).ToList();

        int removed = 0;
        int target = 81 - Math.Clamp(clues, 17, 80);

        foreach (int pos in positions)
        {
            if (removed >= target) break;
            int backup = puzzle[pos];
            puzzle[pos] = 0;
            if (CountSolutions(puzzle, 2) != 1)
                puzzle[pos] = backup;
            else
                removed++;
        }

        return puzzle;
    }

    public bool[] GetGivens(int[] puzzle) =>
        puzzle.Select(v => v != 0).ToArray();

    // Returns null if unsolvable, otherwise the solved board.
    public int[]? Solve(int[] board)
    {
        var copy = (int[])board.Clone();
        return Backtrack(copy) ? copy : null;
    }

    // Returns 0, 1, or 2 (capped).
    public int CountSolutions(int[] board, int cap = 2)
    {
        var copy = (int[])board.Clone();
        int count = 0;
        CountBacktrack(copy, ref count, cap);
        return count;
    }

    // Returns list of conflict cell indices for a given board state.
    public List<int> GetConflicts(int[] board)
    {
        var conflicts = new HashSet<int>();
        // rows
        for (int r = 0; r < 9; r++)
        {
            var seen = new Dictionary<int, int>();
            for (int c = 0; c < 9; c++)
            {
                int v = board[r * 9 + c];
                if (v == 0) continue;
                if (seen.TryGetValue(v, out int prev)) { conflicts.Add(prev); conflicts.Add(r * 9 + c); }
                else seen[v] = r * 9 + c;
            }
        }
        // cols
        for (int c = 0; c < 9; c++)
        {
            var seen = new Dictionary<int, int>();
            for (int r = 0; r < 9; r++)
            {
                int v = board[r * 9 + c];
                if (v == 0) continue;
                if (seen.TryGetValue(v, out int prev)) { conflicts.Add(prev); conflicts.Add(r * 9 + c); }
                else seen[v] = r * 9 + c;
            }
        }
        // boxes
        for (int br = 0; br < 3; br++)
        for (int bc = 0; bc < 3; bc++)
        {
            var seen = new Dictionary<int, int>();
            for (int dr = 0; dr < 3; dr++)
            for (int dc = 0; dc < 3; dc++)
            {
                int idx = (br * 3 + dr) * 9 + (bc * 3 + dc);
                int v = board[idx];
                if (v == 0) continue;
                if (seen.TryGetValue(v, out int prev)) { conflicts.Add(prev); conflicts.Add(idx); }
                else seen[v] = idx;
            }
        }
        return conflicts.ToList();
    }

    private bool FillBoard(int[] board)
    {
        int pos = Array.IndexOf(board, 0);
        if (pos == -1) return true;
        int r = pos / 9, c = pos % 9;
        var digits = Enumerable.Range(1, 9).OrderBy(_ => Rng.Next()).ToArray();
        foreach (int d in digits)
        {
            if (IsValid(board, r, c, d))
            {
                board[pos] = d;
                if (FillBoard(board)) return true;
                board[pos] = 0;
            }
        }
        return false;
    }

    private bool Backtrack(int[] board)
    {
        int pos = FindBestEmpty(board);
        if (pos == -1) return true;
        int r = pos / 9, c = pos % 9;
        for (int d = 1; d <= 9; d++)
        {
            if (IsValid(board, r, c, d))
            {
                board[pos] = d;
                if (Backtrack(board)) return true;
                board[pos] = 0;
            }
        }
        return false;
    }

    private void CountBacktrack(int[] board, ref int count, int cap)
    {
        if (count >= cap) return;
        int pos = FindBestEmpty(board);
        if (pos == -1) { count++; return; }
        int r = pos / 9, c = pos % 9;
        for (int d = 1; d <= 9; d++)
        {
            if (count >= cap) return;
            if (IsValid(board, r, c, d))
            {
                board[pos] = d;
                CountBacktrack(board, ref count, cap);
                board[pos] = 0;
            }
        }
    }

    // MRV heuristic: pick empty cell with fewest legal values
    private int FindBestEmpty(int[] board)
    {
        int best = -1, bestCount = 10;
        for (int i = 0; i < 81; i++)
        {
            if (board[i] != 0) continue;
            int r = i / 9, c = i % 9;
            int cnt = 0;
            for (int d = 1; d <= 9; d++)
                if (IsValid(board, r, c, d)) cnt++;
            if (cnt == 0) return i; // dead end, return immediately
            if (cnt < bestCount) { bestCount = cnt; best = i; }
        }
        return best;
    }

    private bool IsValid(int[] board, int r, int c, int d)
    {
        for (int i = 0; i < 9; i++)
        {
            if (board[r * 9 + i] == d) return false;
            if (board[i * 9 + c] == d) return false;
        }
        int br = (r / 3) * 3, bc = (c / 3) * 3;
        for (int dr = 0; dr < 3; dr++)
        for (int dc = 0; dc < 3; dc++)
            if (board[(br + dr) * 9 + (bc + dc)] == d) return false;
        return true;
    }
}
