using TodoMcp.Models;

namespace TodoMcp.Services;

/// <summary>Todo business rules: input normalisation and validation on top of the repository.</summary>
public interface ITodoService
{
    /// <exception cref="TodoValidationException">Text, project or position is invalid.</exception>
    Task<Todo> AddAsync(string? text, string? project, int? position, CancellationToken ct);

    Task<IReadOnlyList<Todo>> ListAsync(string? project, bool includeDone, CancellationToken ct);
    Task<IReadOnlyList<Todo>> NextAsync(string? project, CancellationToken ct);

    /// <exception cref="TodoValidationException">Project or position is invalid.</exception>
    Task<Todo?> MoveAsync(long id, int position, string? project, CancellationToken ct);

    Task<Todo?> CompleteAsync(long id, CancellationToken ct);
    Task<bool> DeleteAsync(long id, CancellationToken ct);
    Task<IReadOnlyList<ProjectSummary>> ProjectsAsync(CancellationToken ct);
}
