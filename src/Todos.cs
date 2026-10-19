using System.ComponentModel;
using ModelContextProtocol.Server;
using Npgsql;

namespace TodoMcp;

/// <summary>A saved todo item.</summary>
public sealed record Todo(long Id, string Text, bool Done, DateTimeOffset CreatedAt);

/// <summary>Storage for todos.</summary>
public interface ITodoStore
{
    Task<Todo> AddAsync(string text, CancellationToken ct);
    Task<IReadOnlyList<Todo>> ListAsync(bool includeDone, CancellationToken ct);
    Task<Todo?> CompleteAsync(long id, CancellationToken ct);
    Task<bool> DeleteAsync(long id, CancellationToken ct);
}

/// <summary>Postgres-backed store. Uses the limited app role (DML only).</summary>
public sealed class PgTodoStore(NpgsqlDataSource db) : ITodoStore
{
    private const string Columns = "id, text, done, created_at";

    public async Task<Todo> AddAsync(string text, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand($"INSERT INTO todos (text) VALUES ($1) RETURNING {Columns}");
        cmd.Parameters.AddWithValue(text);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);
        return Read(r);
    }

    public async Task<IReadOnlyList<Todo>> ListAsync(bool includeDone, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand(
            $"SELECT {Columns} FROM todos WHERE ($1 OR NOT done) ORDER BY id");
        cmd.Parameters.AddWithValue(includeDone);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var items = new List<Todo>();
        while (await r.ReadAsync(ct))
        {
            items.Add(Read(r));
        }

        return items;
    }

    public async Task<Todo?> CompleteAsync(long id, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand($"UPDATE todos SET done = true WHERE id = $1 RETURNING {Columns}");
        cmd.Parameters.AddWithValue(id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Read(r) : null;
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("DELETE FROM todos WHERE id = $1");
        cmd.Parameters.AddWithValue(id);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    private static Todo Read(NpgsqlDataReader r) =>
        new(r.GetInt64(0), r.GetString(1), r.GetBoolean(2), r.GetFieldValue<DateTimeOffset>(3));
}

/// <summary>
/// Creates the schema at startup using the migrator role (DDL). The running API only ever uses
/// the app role. Skipped when no migrator connection string is configured.
/// </summary>
public sealed class Migrator(IConfiguration config, ILogger<Migrator> log) : IHostedService
{
    private const string Ddl = """
        CREATE TABLE IF NOT EXISTS todos (
            id         bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            text       text        NOT NULL CHECK (length(text) BETWEEN 1 AND 500),
            done       boolean     NOT NULL DEFAULT false,
            created_at timestamptz NOT NULL DEFAULT now()
        );
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
    private const int MaxLength = 500;

    [McpServerTool(Name = "add_todo", Title = "Add todo")]
    [Description("Save a new todo item / note to the user's todo list. Returns the saved item with its id.")]
    public async Task<string> AddTodo(
        [Description("What needs to be done, e.g. 'change the front lightbulb'")] string text,
        CancellationToken ct)
    {
        text = text?.Trim() ?? "";
        if (text.Length is 0 or > MaxLength)
        {
            return $"Error: text must be 1-{MaxLength} characters.";
        }

        return "Saved: " + Format(await store.AddAsync(text, ct));
    }

    [McpServerTool(Name = "list_todos", Title = "List todos", ReadOnly = true)]
    [Description("Read the user's todo list. By default only open (not done) items are returned.")]
    public async Task<string> ListTodos(
        [Description("Also include completed items")] bool includeDone = false,
        CancellationToken ct = default)
    {
        var items = await store.ListAsync(includeDone, ct);
        return items.Count == 0 ? "No todos." : string.Join('\n', items.Select(Format));
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

    private static string Format(Todo t) => $"[{t.Id}] {(t.Done ? "(done) " : "")}{t.Text}";
}
