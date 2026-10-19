using System.ComponentModel;
using ModelContextProtocol.Server;
using Npgsql;

namespace TodoMcp;

/// <summary>A saved todo item. <paramref name="Position"/> only orders items within their project.</summary>
public sealed record Todo(long Id, string Project, string Text, bool Done, DateTimeOffset CreatedAt, long Position);

/// <summary>A project (list) of todos with its open/done counts.</summary>
public sealed record ProjectSummary(string Name, int Open, int Done);

/// <summary>Storage for projects and their ordered todos.</summary>
public interface ITodoStore
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

/// <summary>Postgres-backed store. Uses the limited app role (DML only).</summary>
public sealed class PgTodoStore(NpgsqlDataSource db) : ITodoStore
{
    private const string Select =
        "SELECT t.id, p.name, t.text, t.done, t.created_at, t.position FROM todos t JOIN projects p ON p.id = t.project_id";

    public async Task<Todo> AddAsync(string project, string text, int? position, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var projectId = await EnsureProjectAsync(conn, tx, project, ct);
        var pos = await MakeRoomAsync(conn, tx, projectId, 0, position, ct);
        var id = await ScalarAsync<long>(conn, tx,
            "INSERT INTO todos (project_id, text, position) VALUES ($1, $2, $3) RETURNING id",
            ct, projectId, text, pos);
        var todo = await GetAsync(conn, tx, id, ct);
        await tx.CommitAsync(ct);
        return todo!;
    }

    public async Task<IReadOnlyList<Todo>> ListAsync(string? project, bool includeDone, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand(
            $"{Select} WHERE ($1::text IS NULL OR lower(p.name) = lower($1)) AND ($2 OR NOT t.done) ORDER BY lower(p.name), t.position, t.id");
        cmd.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Text, (object?)project ?? DBNull.Value);
        cmd.Parameters.AddWithValue(includeDone);
        return await ReadAllAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<Todo>> NextAsync(string? project, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand(
            $"{Select} WHERE NOT t.done AND ($1::text IS NULL OR lower(p.name) = lower($1)) AND t.id = " +
            "(SELECT t2.id FROM todos t2 WHERE t2.project_id = t.project_id AND NOT t2.done ORDER BY t2.position, t2.id LIMIT 1) " +
            "ORDER BY lower(p.name)");
        cmd.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Text, (object?)project ?? DBNull.Value);
        return await ReadAllAsync(cmd, ct);
    }

    public async Task<Todo?> MoveAsync(long id, int position, string? project, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var current = await GetAsync(conn, tx, id, ct);
        if (current is null)
        {
            return null;
        }

        var projectId = await EnsureProjectAsync(conn, tx, project ?? current.Project, ct);
        var pos = await MakeRoomAsync(conn, tx, projectId, id, position, ct);
        await ExecAsync(conn, tx, "UPDATE todos SET project_id = $1, position = $2 WHERE id = $3", ct, projectId, pos, id);
        var moved = await GetAsync(conn, tx, id, ct);
        await tx.CommitAsync(ct);
        return moved;
    }

    public async Task<Todo?> CompleteAsync(long id, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("UPDATE todos SET done = true WHERE id = $1");
        cmd.Parameters.AddWithValue(id);
        if (await cmd.ExecuteNonQueryAsync(ct) == 0)
        {
            return null;
        }

        await using var conn = await db.OpenConnectionAsync(ct);
        return await GetAsync(conn, null, id, ct);
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("DELETE FROM todos WHERE id = $1");
        cmd.Parameters.AddWithValue(id);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<IReadOnlyList<ProjectSummary>> ProjectsAsync(CancellationToken ct)
    {
        await using var cmd = db.CreateCommand(
            "SELECT p.name, count(*) FILTER (WHERE NOT t.done), count(*) FILTER (WHERE t.done) " +
            "FROM projects p LEFT JOIN todos t ON t.project_id = p.id GROUP BY p.id ORDER BY lower(p.name)");
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var items = new List<ProjectSummary>();
        while (await r.ReadAsync(ct))
        {
            items.Add(new ProjectSummary(r.GetString(0), (int)r.GetInt64(1), (int)r.GetInt64(2)));
        }

        return items;
    }

    /// <summary>Find-or-create a project (case-insensitive) and lock its row for the transaction.</summary>
    private static async Task<long> EnsureProjectAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string name, CancellationToken ct)
    {
        await ExecAsync(conn, tx, "INSERT INTO projects (name) VALUES ($1) ON CONFLICT ((lower(name))) DO NOTHING", ct, name);
        return await ScalarAsync<long>(conn, tx,
            "SELECT id FROM projects WHERE lower(name) = lower($1) FOR UPDATE", ct, name);
    }

    /// <summary>
    /// Returns the position value for a new/moved item so it lands at <paramref name="rank"/> among the
    /// open items (null or past the end = last), shifting later items down by one. Gaps are fine: only
    /// the order of positions matters.
    /// </summary>
    private static async Task<long> MakeRoomAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, long projectId, long excludeId, int? rank, CancellationToken ct)
    {
        if (rank is { } r)
        {
            var at = await ScalarAsync<long?>(conn, tx,
                "SELECT position FROM todos WHERE project_id = $1 AND NOT done AND id <> $2 ORDER BY position, id OFFSET $3 LIMIT 1",
                ct, projectId, excludeId, r - 1);
            if (at is { } p)
            {
                await ExecAsync(conn, tx,
                    "UPDATE todos SET position = position + 1 WHERE project_id = $1 AND id <> $2 AND position >= $3",
                    ct, projectId, excludeId, p);
                return p;
            }
        }

        return await ScalarAsync<long>(conn, tx,
            "SELECT COALESCE(MAX(position), 0) + 1 FROM todos WHERE project_id = $1 AND id <> $2", ct, projectId, excludeId);
    }

    private static async Task<Todo?> GetAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, long id, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand($"{Select} WHERE t.id = $1", conn, tx);
        cmd.Parameters.AddWithValue(id);
        return (await ReadAllAsync(cmd, ct)).FirstOrDefault();
    }

    private static async Task<List<Todo>> ReadAllAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var items = new List<Todo>();
        while (await r.ReadAsync(ct))
        {
            items.Add(new Todo(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetBoolean(3), r.GetFieldValue<DateTimeOffset>(4), r.GetInt64(5)));
        }

        return items;
    }

    private static async Task ExecAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string sql, CancellationToken ct, params object[] args)
    {
        await using var cmd = Command(conn, tx, sql, args);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection conn, NpgsqlTransaction tx, string sql, CancellationToken ct, params object[] args)
    {
        await using var cmd = Command(conn, tx, sql, args);
        var v = await cmd.ExecuteScalarAsync(ct);
        return v is null or DBNull ? default! : (T)v;
    }

    private static NpgsqlCommand Command(NpgsqlConnection conn, NpgsqlTransaction tx, string sql, object[] args)
    {
        var cmd = new NpgsqlCommand(sql, conn, tx);
        foreach (var a in args)
        {
            cmd.Parameters.AddWithValue(a);
        }

        return cmd;
    }
}

/// <summary>
/// Creates/upgrades the schema at startup using the migrator role (DDL). The running API only ever
/// uses the app role. Skipped when no migrator connection string is configured. Idempotent.
/// </summary>
public sealed class Migrator(IConfiguration config, ILogger<Migrator> log) : IHostedService
{
    private const string Ddl = """
        CREATE TABLE IF NOT EXISTS projects (
            id         bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            name       text        NOT NULL CHECK (length(name) BETWEEN 1 AND 100),
            created_at timestamptz NOT NULL DEFAULT now()
        );
        CREATE UNIQUE INDEX IF NOT EXISTS projects_name_key ON projects (lower(name));

        CREATE TABLE IF NOT EXISTS todos (
            id         bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            text       text        NOT NULL CHECK (length(text) BETWEEN 1 AND 500),
            done       boolean     NOT NULL DEFAULT false,
            created_at timestamptz NOT NULL DEFAULT now()
        );

        -- v0.3: projects + priority order. Existing todos land in "Inbox", ordered by creation.
        INSERT INTO projects (name) VALUES ('Inbox') ON CONFLICT ((lower(name))) DO NOTHING;
        ALTER TABLE todos ADD COLUMN IF NOT EXISTS project_id bigint REFERENCES projects (id) ON DELETE CASCADE;
        ALTER TABLE todos ADD COLUMN IF NOT EXISTS position bigint;
        UPDATE todos SET project_id = (SELECT id FROM projects WHERE lower(name) = 'inbox') WHERE project_id IS NULL;
        UPDATE todos SET position = id WHERE position IS NULL;
        ALTER TABLE todos ALTER COLUMN project_id SET NOT NULL;
        ALTER TABLE todos ALTER COLUMN position SET NOT NULL;
        CREATE INDEX IF NOT EXISTS todos_project_order ON todos (project_id, position);
        """;

    public async Task StartAsync(CancellationToken ct)
    {
        var cs = config.GetConnectionString("Migrator");
        if (string.IsNullOrWhiteSpace(cs))
        {
            return;
        }

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(Ddl, conn);
        await cmd.ExecuteNonQueryAsync(ct);
        log.LogInformation("Database schema is up to date");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

/// <summary>Todo tools exposed to the LLM over MCP.</summary>
[McpServerToolType]
public sealed class TodoTools(ITodoStore store)
{
    private const int MaxText = 500;
    private const int MaxProject = 100;
    private const string DefaultProject = "Inbox";

    [McpServerTool(Name = "add_todo", Title = "Add todo")]
    [Description("Save a new todo to a project's priority list. By default it goes at the end; give 'position' to insert it earlier " +
        "(e.g. position 1 = do it first, 2 = second). Projects are created automatically by name.")]
    public async Task<string> AddTodo(
        [Description("What needs to be done, e.g. 'change the front lightbulb'")] string text,
        [Description("Project/category name, e.g. 'Work' or 'stacks migration'. Defaults to 'Inbox'")] string? project = null,
        [Description("1-based priority position among open todos in the project. Omit to append to the end")] int? position = null,
        CancellationToken ct = default)
    {
        text = text?.Trim() ?? "";
        if (text.Length is 0 or > MaxText)
        {
            return $"Error: text must be 1-{MaxText} characters.";
        }

        if (!TryProject(project, out var name, out var error) || !ValidPosition(position, out error))
        {
            return error;
        }

        return "Saved: " + Format(await store.AddAsync(name, text, position, ct));
    }

    [McpServerTool(Name = "list_todos", Title = "List todos", ReadOnly = true)]
    [Description("Read todos in priority order (top = do first), grouped by project. Omit 'project' to see every project. " +
        "Open items only unless includeDone is true.")]
    public async Task<string> ListTodos(
        [Description("Only this project")] string? project = null,
        [Description("Also include completed items")] bool includeDone = false,
        CancellationToken ct = default)
    {
        var items = await store.ListAsync(Clean(project), includeDone, ct);
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
        var items = await store.NextAsync(Clean(project), ct);
        return items.Count == 0
            ? project is null ? "Nothing to do." : $"Nothing open in '{project.Trim()}'."
            : string.Join('\n', items.Select(t => $"{t.Project}: [{t.Id}] {t.Text}"));
    }

    [McpServerTool(Name = "move_todo", Title = "Move todo")]
    [Description("Change a todo's priority position (1 = top), optionally moving it to another project. Use list_todos to find ids.")]
    public async Task<string> MoveTodo(
        [Description("The todo id")] long id,
        [Description("New 1-based priority position among open todos")] int position,
        [Description("Move to this project (created if new); omit to stay in the current one")] string? project = null,
        CancellationToken ct = default)
    {
        string? name = null;
        if (project is not null && !TryProject(project, out name, out var perr))
        {
            return perr;
        }

        if (!ValidPosition(position, out var error))
        {
            return error;
        }

        var item = await store.MoveAsync(id, position, name, ct);
        return item is null ? $"Error: no todo with id {id}." : "Moved: " + Format(item);
    }

    [McpServerTool(Name = "complete_todo", Title = "Complete todo")]
    [Description("Mark a todo as done, by its id (from list_todos).")]
    public async Task<string> CompleteTodo([Description("The todo id")] long id, CancellationToken ct)
    {
        var item = await store.CompleteAsync(id, ct);
        return item is null ? $"Error: no todo with id {id}." : "Completed: " + Format(item);
    }

    [McpServerTool(Name = "delete_todo", Title = "Delete todo", Destructive = true)]
    [Description("Permanently delete a todo, by its id (from list_todos).")]
    public async Task<string> DeleteTodo([Description("The todo id")] long id, CancellationToken ct) =>
        await store.DeleteAsync(id, ct) ? $"Deleted todo {id}." : $"Error: no todo with id {id}.";

    [McpServerTool(Name = "list_projects", Title = "List projects", ReadOnly = true)]
    [Description("List all projects with how many open and done todos each has.")]
    public async Task<string> ListProjects(CancellationToken ct)
    {
        var projects = await store.ProjectsAsync(ct);
        return projects.Count == 0
            ? "No projects."
            : string.Join('\n', projects.Select(p => $"{p.Name}: {p.Open} open, {p.Done} done"));
    }

    private static string? Clean(string? project) => string.IsNullOrWhiteSpace(project) ? null : project.Trim();

    private static bool TryProject(string? project, out string name, out string error)
    {
        name = Clean(project) ?? DefaultProject;
        error = name.Length > MaxProject ? $"Error: project name must be at most {MaxProject} characters." : "";
        return error.Length == 0;
    }

    private static bool ValidPosition(int? position, out string error)
    {
        error = position is < 1 ? "Error: position must be 1 or greater." : "";
        return error.Length == 0;
    }

    private static string Format(Todo t) => $"[{t.Id}] {(t.Done ? "(done) " : "")}{t.Text} (project: {t.Project})";
}
