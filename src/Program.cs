using ModelContextProtocol.Server;
using Npgsql;
using TodoMcp;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddHostedService<Migrator>();
builder.Services.AddSingleton(sp =>
    NpgsqlDataSource.Create(sp.GetRequiredService<IConfiguration>().GetConnectionString("App")
        ?? throw new InvalidOperationException("ConnectionStrings:App is not configured")));
builder.Services.AddSingleton<ITodoStore, PgTodoStore>();
builder.Services
    .AddMcpServer()
    .WithHttpTransport(o => o.Stateless = true)
    .WithToolsFromAssembly(typeof(HelloTools).Assembly);

var app = builder.Build();

app.MapMcp("mcp");
app.MapHealthChecks("/health");

app.Run();

[McpServerToolType]
public class HelloTools
{
    /// <summary>Returns the secret flag string.</summary>
    [McpServerTool(Name = "get_flag", Title = "Get flag", ReadOnly = true)]
    public string GetFlag() => FlagResolver.ReadFlag();
}

/// <summary>Reads the flag from <c>TODOMCP_FLAG</c>; falls back to a compile-time default.</summary>
public static partial class FlagResolver
{
    public static string ReadFlag() =>
        Environment.GetEnvironmentVariable("TODOMCP_FLAG") ?? "FLAG-lightbulb-1234";
}
