using Npgsql;
using TodoMcp.Data;
using TodoMcp.Repositories;
using TodoMcp.Services;
using TodoMcp.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddHostedService<Migrator>();
builder.Services.AddSingleton(sp =>
    NpgsqlDataSource.Create(sp.GetRequiredService<IConfiguration>().GetConnectionString("App")
        ?? throw new InvalidOperationException("ConnectionStrings:App is not configured")));
builder.Services.AddSingleton<ITodoRepository, PgTodoRepository>();
builder.Services.AddSingleton<ITodoService, TodoService>();
builder.Services
    .AddMcpServer()
    .WithHttpTransport(o => o.Stateless = true)
    .WithToolsFromAssembly(typeof(HelloTools).Assembly);

var app = builder.Build();

app.MapMcp("mcp");
app.MapHealthChecks("/health");

app.Run();
