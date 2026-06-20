using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using System.Reflection;
using Sudoku.Controllers; // <-- for the cross-reference
using Sudoku.Models; // <-- for NavLink

namespace Sudoku.ViewComponents;

public class NavMenuViewComponent : ViewComponent
{
    private readonly IFileProvider _fileProvider;

    public NavMenuViewComponent(IWebHostEnvironment env)
    {
        _fileProvider = env.ContentRootFileProvider;
    }

    public IViewComponentResult Invoke()
    {
        // 1. Get all public action methods on HomeController
        var validActions = typeof(HomeController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.ReturnType.IsAssignableTo(typeof(IActionResult)))
            .Select(m => m.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase); // case-insensitive

        // 2. Scan Views/Home/, filter to only files that have a matching action
        var links = _fileProvider
            .GetDirectoryContents("Views/Home")
            .Where(f => !f.IsDirectory 
                     && f.Name.EndsWith(".cshtml") 
                     && !f.Name.StartsWith("_"))
            .Select(f => Path.GetFileNameWithoutExtension(f.Name))
            .Where(action => validActions.Contains(action))  // <-- the cross-reference
            .Select(action => new NavLink
            {
                Label = action,
                Controller = "Home",
                Action = action
            })
            .ToList();

        return View(links);
    }
}