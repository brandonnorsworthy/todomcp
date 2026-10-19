using TodoMcp.Models;
using TodoMcp.Repositories;

namespace TodoMcp.Services;

public sealed class TodoService(ITodoRepository repository) : ITodoService
{
    private const int MaxText = 500;
    private const int MaxProject = 100;
    private const string DefaultProject = "Inbox";

    public Task<Todo> AddAsync(string? text, string? project, int? position, CancellationToken ct)
    {
        text = text?.Trim() ?? "";
        if (text.Length is 0 or > MaxText)
        {
            throw new TodoValidationException($"text must be 1-{MaxText} characters.");
        }

        return repository.AddAsync(ProjectOrDefault(project), text, ValidPosition(position), ct);
    }

    public Task<IReadOnlyList<Todo>> ListAsync(string? project, bool includeDone, CancellationToken ct) =>
        repository.ListAsync(Clean(project), includeDone, ct);

    public Task<IReadOnlyList<Todo>> NextAsync(string? project, CancellationToken ct) =>
        repository.NextAsync(Clean(project), ct);

    public Task<Todo?> MoveAsync(long id, int position, string? project, CancellationToken ct)
    {
        var name = project is null ? null : ProjectOrDefault(project);
        return repository.MoveAsync(id, ValidPosition(position)!.Value, name, ct);
    }

    public Task<Todo?> CompleteAsync(long id, CancellationToken ct) => repository.CompleteAsync(id, ct);

    public Task<bool> DeleteAsync(long id, CancellationToken ct) => repository.DeleteAsync(id, ct);

    public Task<IReadOnlyList<ProjectSummary>> ProjectsAsync(CancellationToken ct) => repository.ProjectsAsync(ct);

    private static string? Clean(string? project) => string.IsNullOrWhiteSpace(project) ? null : project.Trim();

    private static string ProjectOrDefault(string? project)
    {
        var name = Clean(project) ?? DefaultProject;
        return name.Length > MaxProject
            ? throw new TodoValidationException($"project name must be at most {MaxProject} characters.")
            : name;
    }

    private static int? ValidPosition(int? position) =>
        position is < 1 ? throw new TodoValidationException("position must be 1 or greater.") : position;
}
