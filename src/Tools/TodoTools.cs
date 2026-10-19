using System.ComponentModel;
using ModelContextProtocol.Server;
using TodoMcp.Models;
using TodoMcp.Services;

namespace TodoMcp.Tools;

/// <summary>Todo tools exposed to the LLM over MCP. Thin adapter: delegates to <see cref="ITodoService"/> and formats text.</summary>
[McpServerToolType]
public sealed class TodoTools(ITodoService todos)
{
    [McpServerTool(Name = "add_todo", Title = "Add todo")]
    [Description("Save a new todo to a project's priority list. By default it goes at the end; give 'position' to insert it earlier " +
        "(e.g. position 1 = do it first, 2 = second). Projects are created automatically by name.")]
    public Task<string> AddTodo(
        [Description("What needs to be done, e.g. 'change the front lightbulb'")] string text,
        [Description("Project/category name, e.g. 'Work' or 'stacks migration'. Defaults to 'Inbox'")] string? project = null,
        [Description("1-based priority position among open todos in the project. Omit to append to the end")] int? position = null,
        CancellationToken ct = default) =>
        Guard(async () => "Saved: " + Format(await todos.AddAsync(text, project, position, ct)));

    [McpServerTool(Name = "list_todos", Title = "List todos", ReadOnly = true)]
    [Description("Read todos in priority order (top = do first), grouped by project. Omit 'project' to see every project. " +
        "Open items only unless includeDone is true.")]
    public async Task<string> ListTodos(
        [Description("Only this project")] string? project = null,
        [Description("Also include completed items")] bool includeDone = false,
        CancellationToken ct = default)
    {
        var items = await todos.ListAsync(project, includeDone, ct);
        if (items.Count == 0)
        {
            return project is null ? "No todos." : $"No todos in '{project.Trim()}'.";
        }

        var lines = new List<string>();
        foreach (var group in items.GroupBy(t => t.Project))
        {
            lines.Add($"## {group.Key}");
            var rank = 0;
            lines.AddRange(group.Select(t => t.Done ? $"-  [{t.Id}] (done) {t.Text}" : $"{++rank}. [{t.Id}] {t.Text}"));
        }

        return string.Join('\n', lines);
    }

    [McpServerTool(Name = "next_todo", Title = "Next todo", ReadOnly = true)]
    [Description("What should I do next? Returns the highest-priority open todo of a project, or of every project when none is given.")]
    public async Task<string> NextTodo(
        [Description("Project name; omit for the top item of every project")] string? project = null,
        CancellationToken ct = default)
    {
        var items = await todos.NextAsync(project, ct);
        return items.Count == 0
            ? project is null ? "Nothing to do." : $"Nothing open in '{project.Trim()}'."
            : string.Join('\n', items.Select(t => $"{t.Project}: [{t.Id}] {t.Text}"));
    }

    [McpServerTool(Name = "move_todo", Title = "Move todo")]
    [Description("Change a todo's priority position (1 = top), optionally moving it to another project. Use list_todos to find ids.")]
    public Task<string> MoveTodo(
        [Description("The todo id")] long id,
        [Description("New 1-based priority position among open todos")] int position,
        [Description("Move to this project (created if new); omit to stay in the current one")] string? project = null,
        CancellationToken ct = default) =>
        Guard(async () =>
        {
            var item = await todos.MoveAsync(id, position, project, ct);
            return item is null ? NotFound(id) : "Moved: " + Format(item);
        });

    [McpServerTool(Name = "complete_todo", Title = "Complete todo")]
    [Description("Mark a todo as done, by its id (from list_todos).")]
    public async Task<string> CompleteTodo([Description("The todo id")] long id, CancellationToken ct)
    {
        var item = await todos.CompleteAsync(id, ct);
        return item is null ? NotFound(id) : "Completed: " + Format(item);
    }

    [McpServerTool(Name = "delete_todo", Title = "Delete todo", Destructive = true)]
    [Description("Permanently delete a todo, by its id (from list_todos).")]
    public async Task<string> DeleteTodo([Description("The todo id")] long id, CancellationToken ct) =>
        await todos.DeleteAsync(id, ct) ? $"Deleted todo {id}." : NotFound(id);

    [McpServerTool(Name = "list_projects", Title = "List projects", ReadOnly = true)]
    [Description("List all projects with how many open and done todos each has.")]
    public async Task<string> ListProjects(CancellationToken ct)
    {
        var projects = await todos.ProjectsAsync(ct);
        return projects.Count == 0
            ? "No projects."
            : string.Join('\n', projects.Select(p => $"{p.Name}: {p.Open} open, {p.Done} done"));
    }

    /// <summary>Turns validation failures into the plain-text error the LLM sees.</summary>
    private static async Task<string> Guard(Func<Task<string>> action)
    {
        try
        {
            return await action();
        }
        catch (TodoValidationException e)
        {
            return "Error: " + e.Message;
        }
    }

    private static string NotFound(long id) => $"Error: no todo with id {id}.";

    private static string Format(Todo t) => $"[{t.Id}] {(t.Done ? "(done) " : "")}{t.Text} (project: {t.Project})";
}
