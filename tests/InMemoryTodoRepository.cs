using TodoMcp.Models;
using TodoMcp.Repositories;

namespace TodoMcp.Tests;

/// <summary>Test double so the endpoint tests need no database. Mirrors PgTodoRepository's ordering rules.</summary>
public sealed class InMemoryTodoRepository : ITodoRepository
{
    private readonly List<Todo> _items = []; // kept in priority order per project
    private long _next = 1;

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public Task<Todo> AddAsync(string project, string text, int? position, CancellationToken ct)
    {
        var existing = _items.Select(t => t.Project).FirstOrDefault(p => Same(p, project));
        var t = new Todo(_next++, existing ?? project, text, false, DateTimeOffset.UtcNow, 0);
        Place(t, position);
        return Task.FromResult(t);
    }

    private void Place(Todo t, int? rank)
    {
        // index in _items of the open item currently holding `rank` within the project
        var open = _items.Select((x, i) => (x, i)).Where(p => Same(p.x.Project, t.Project) && !p.x.Done).ToList();
        if (rank is { } r && r - 1 < open.Count)
        {
            _items.Insert(open[r - 1].i, t);
        }
        else
        {
            _items.Add(t);
        }
    }

    public Task<IReadOnlyList<Todo>> ListAsync(string? project, bool includeDone, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Todo>>(_items
            .Where(t => (project is null || Same(t.Project, project)) && (includeDone || !t.Done))
            .OrderBy(t => t.Project, StringComparer.OrdinalIgnoreCase).ToList()); // stable: keeps priority order

    public Task<IReadOnlyList<Todo>> NextAsync(string? project, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Todo>>(_items
            .Where(t => !t.Done && (project is null || Same(t.Project, project)))
            .GroupBy(t => t.Project, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First()).ToList());

    public Task<Todo?> MoveAsync(long id, int position, string? project, CancellationToken ct)
    {
        var i = _items.FindIndex(t => t.Id == id);
        if (i < 0)
        {
            return Task.FromResult<Todo?>(null);
        }

        var moved = _items[i] with { Project = project is null ? _items[i].Project : _items.Select(t => t.Project).FirstOrDefault(p => Same(p, project)) ?? project };
        _items.RemoveAt(i);
        Place(moved, position);
        return Task.FromResult<Todo?>(moved);
    }

    public Task<Todo?> CompleteAsync(long id, CancellationToken ct)
    {
        var i = _items.FindIndex(t => t.Id == id);
        if (i < 0)
        {
            return Task.FromResult<Todo?>(null);
        }

        _items[i] = _items[i] with { Done = true };
        return Task.FromResult<Todo?>(_items[i]);
    }

    public Task<bool> DeleteAsync(long id, CancellationToken ct) =>
        Task.FromResult(_items.RemoveAll(t => t.Id == id) > 0);

    public Task<IReadOnlyList<ProjectSummary>> ProjectsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ProjectSummary>>(_items
            .GroupBy(t => t.Project, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ProjectSummary(g.Key, g.Count(t => !t.Done), g.Count(t => t.Done))).ToList());
}
