using Npgsql;
using TodoMcp.Models;

namespace TodoMcp.Repositories;

/// <summary>Postgres-backed repository. Uses the limited app role (DML only).</summary>
public sealed class PgTodoRepository(NpgsqlDataSource db) : ITodoRepository
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
