using Npgsql;

namespace TodoMcp.Data;

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
