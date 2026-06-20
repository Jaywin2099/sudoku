namespace Sudoku.Services;

// Difficulty tiers for solving techniques. Lower = easier / more obvious.
// Only Single is implemented in the skeleton; the rest reserve the contract
// (and the colour scale on the front end) for techniques added later.
public enum TechniqueTier
{
    Single = 1,           // naked / hidden singles
    LockedCandidate = 2,  // pointing / claiming
    Subset = 3,           // naked / hidden pairs, triples, quads
    Fish = 4,             // X-Wing, Swordfish, Jellyfish
    Wing = 5,             // XY-Wing, XYZ-Wing, W-Wing
    Chain = 6,            // chains, uniqueness
    BruteForce = 7        // guess fallback (the existing backtracker)
}

// A digit placed into a cell as the result of a step.
public record Placement(int Cell, int Digit);

// A candidate removed from a cell. Singles produce none, but the field is part
// of the contract so candidate-eliminating techniques (subsets, fish, ...) drop
// in without changing the shape the UI and grader already read.
public record Elimination(int Cell, int Digit);

// One move a human solver would make. This record is THE contract every later
// feature decodes: the named technique + tier (for labelling and colour-coding),
// what it concluded (placements / eliminations), the cells it reasoned over
// (EvidenceCells, for highlighting on the board), a plain-language explanation,
// and a full board snapshot AFTER the move so the UI can jump to any step with
// no recomputation.
public record SolveStep
{
    public required string Technique { get; init; }
    public required int Tier { get; init; }
    public required string Explanation { get; init; }
    public List<Placement> Placements { get; init; } = new();
    public List<Elimination> Eliminations { get; init; } = new();
    public List<int> EvidenceCells { get; init; } = new();
    public required int[] BoardAfter { get; init; }
}

// Result of running the logical solver over a board: whether it fully solved the
// board with the techniques it currently knows, the ordered steps it took, and
// the board it ended on (fully solved, or stuck needing a harder technique).
public record SolveResult(bool Solved, List<SolveStep> Steps, int[] FinalBoard);
