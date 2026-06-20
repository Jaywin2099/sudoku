using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Sudoku.Models;
using Sudoku.Services;

namespace Sudoku.Controllers;

public class HomeController : Controller
{
    private readonly SudokuService _sudoku;
    private readonly LogicalSolver _logical;

    public HomeController(SudokuService sudoku, LogicalSolver logical)
    {
        _sudoku = sudoku;
        _logical = logical;
    }

    public IActionResult Index() => View();
    public IActionResult Sudoku() => View();

    [HttpGet("/api/sudoku/new")]
    public IActionResult NewPuzzle([FromQuery] int clues = 30)
    {
        clues = Math.Clamp(clues, 17, 80);
        var puzzle = _sudoku.GeneratePuzzle(clues);
        var givens = _sudoku.GetGivens(puzzle);
        return Json(new { board = puzzle, givens });
    }

    [HttpPost("/api/sudoku/solve")]
    public IActionResult SolvePuzzle([FromBody] BoardRequest req)
    {
        if (req?.Board is not { Length: 81 }) return BadRequest();
        var result = _sudoku.Solve(req.Board);
        if (result == null) return Json(new { solvable = false });
        return Json(new { solvable = true, board = result });
    }

    [HttpPost("/api/sudoku/analyze")]
    public IActionResult Analyze([FromBody] BoardRequest req)
    {
        if (req?.Board is not { Length: 81 }) return BadRequest();
        var conflicts = _sudoku.GetConflicts(req.Board);
        if (conflicts.Count > 0)
            return Json(new { valid = false, conflicts, solutionCount = 0, message = "Board has conflicts." });

        // cap at 2 for speed; only count more if user asks for exact
        int count = _sudoku.CountSolutions(req.Board, 2);
        string message = count switch
        {
            0 => "No solution exists for this board.",
            1 => "Exactly 1 solution — this puzzle is valid and unique.",
            _ => "Multiple solutions exist."
        };
        return Json(new { valid = true, conflicts, solutionCount = count, message });
    }

    [HttpPost("/api/sudoku/conflicts")]
    public IActionResult Conflicts([FromBody] BoardRequest req)
    {
        if (req?.Board is not { Length: 81 }) return BadRequest();
        var conflicts = _sudoku.GetConflicts(req.Board);
        return Json(new { conflicts });
    }

    // Deductive, human-style solve: returns the ordered named steps (with a board
    // snapshot after each) that the logical solver took, and whether it finished.
    [HttpPost("/api/sudoku/solve-steps")]
    public IActionResult SolveSteps([FromBody] BoardRequest req)
    {
        if (req?.Board is not { Length: 81 }) return BadRequest();
        var result = _logical.Solve(req.Board);
        return Json(new { solved = result.Solved, steps = result.Steps, finalBoard = result.FinalBoard });
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}

public class BoardRequest
{
    public int[] Board { get; set; } = [];
}
