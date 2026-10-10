using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
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
    public string GetFlag() =>
        Environment.GetEnvironmentVariable("TODOMCP_FLAG") ?? "FLAG-lightbulb-1234";
}
