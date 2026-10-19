using Microsoft.Extensions.Configuration;
using Npgsql;
using TodoMcp;

namespace TodoMcp.Tests;

/// <summary>
/// Runs the real SQL against Postgres. Set TODOMCP_TEST_PG to a connection string whose role can
/// create tables in a scratch database; skipped otherwise. The Migrator DDL is applied first.
/// </summary>
public class PgTodoStoreTests
{
    [Fact]
    public async Task Ordering_InsertMoveNext_RoundTrip()
    {
        var cs = Environment.GetEnvironmentVariable("TODOMCP_TEST_PG");
        Assert.SkipWhen(string.IsNullOrEmpty(cs), "TODOMCP_TEST_PG not set");

        var ct = TestContext.Current.CancellationToken;
        await using var db = NpgsqlDataSource.Create(cs!);
        await using (var drop = db.CreateCommand("DROP TABLE IF EXISTS todos; DROP TABLE IF EXISTS projects;"))
        {
            await drop.ExecuteNonQueryAsync(ct);
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Migrator"] = cs }).Build();
        var migrator = new Migrator(config, Microsoft.Extensions.Logging.Abstractions.NullLogger<Migrator>.Instance);
        await migrator.StartAsync(ct);
        await migrator.StartAsync(ct); // idempotent

        var store = new PgTodoStore(db);
        var a = await store.AddAsync("stacks", "a", null, ct);
        var c = await store.AddAsync("Stacks", "c", null, ct);
        var b = await store.AddAsync("stacks", "b", 2, ct);
        var top = await store.AddAsync("stacks", "top", 1, ct);
        var other = await store.AddAsync("home", "h", null, ct);

        Assert.Equal(["top", "a", "b", "c"], (await store.ListAsync("STACKS", false, ct)).Select(t => t.Text));
        Assert.Equal("top", (await store.NextAsync("stacks", ct)).Single().Text);

        await store.MoveAsync(c.Id, 2, null, ct);
        Assert.Equal(["top", "c", "a", "b"], (await store.ListAsync("stacks", false, ct)).Select(t => t.Text));

        await store.CompleteAsync(top.Id, ct);
        Assert.Equal("c", (await store.NextAsync("stacks", ct)).Single().Text);
        Assert.Equal(4, (await store.ListAsync("stacks", true, ct)).Count);

        var moved = await store.MoveAsync(a.Id, 1, "home", ct);
        Assert.Equal("home", moved!.Project);
        Assert.Equal(["a", "h"], (await store.ListAsync("home", false, ct)).Select(t => t.Text));

        var projects = await store.ProjectsAsync(ct);
        Assert.Equal([("home", 2, 0), ("Inbox", 0, 0), ("stacks", 2, 1)], projects.Select(p => (p.Name, p.Open, p.Done)));

        Assert.True(await store.DeleteAsync(b.Id, ct));
        Assert.False(await store.DeleteAsync(b.Id, ct));
        Assert.Null(await store.MoveAsync(9999, 1, null, ct));
        _ = other;
    }
}
