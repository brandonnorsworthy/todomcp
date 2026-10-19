using TodoMcp.Models;

namespace TodoMcp.Repositories;

/// <summary>Persistence for projects and their ordered todos. Inputs are expected to be validated already.</summary>
public interface ITodoRepository
{
    /// <summary>Adds a todo; <paramref name="position"/> is the 1-based rank among open items (null = append).</summary>
    Task<Todo> AddAsync(string project, string text, int? position, CancellationToken ct);

    /// <summary>Todos ordered by project then priority. A null project means all projects.</summary>
    Task<IReadOnlyList<Todo>> ListAsync(string? project, bool includeDone, CancellationToken ct);

    /// <summary>The top open todo of one project, or of every project when null.</summary>
    Task<IReadOnlyList<Todo>> NextAsync(string? project, CancellationToken ct);

    /// <summary>Re-ranks a todo (and optionally moves it to another project).</summary>
    Task<Todo?> MoveAsync(long id, int position, string? project, CancellationToken ct);

    Task<Todo?> CompleteAsync(long id, CancellationToken ct);
    Task<bool> DeleteAsync(long id, CancellationToken ct);
    Task<IReadOnlyList<ProjectSummary>> ProjectsAsync(CancellationToken ct);
}
