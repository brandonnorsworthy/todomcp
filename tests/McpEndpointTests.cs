using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TodoMcp.Repositories;

namespace TodoMcp.Tests;

/// <summary>Integration tests: drive the real MCP HTTP endpoint with JSON-RPC requests.</summary>
public class McpEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string DefaultFlag = "FLAG-lightbulb-1234";

    private readonly HttpClient _client;

    public McpEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.AddSingleton<ITodoRepository, InMemoryTodoRepository>())).CreateClient();
    }

    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/health", Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Xunit.TestContext.Current.CancellationToken);
        Assert.Contains("Healthy", body);
    }

    [Fact]
    public async Task Mcp_Initialize_ReturnsServerInfo()
    {
        var response = await PostJsonRpc("initialize", new
        {
            protocolVersion = "2025-03-26",
            capabilities = new { },
            clientInfo = new { name = "test", version = "0" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync(Xunit.TestContext.Current.CancellationToken);
        var result = ParseResult(body);
        var serverName = result.GetProperty("serverInfo").GetProperty("name").GetString();
        Assert.False(string.IsNullOrWhiteSpace(serverName));
        Assert.StartsWith("TodoMcp", serverName);
    }

    [Fact]
    public async Task Mcp_ToolsList_ContainsGetFlag()
    {
        var response = await PostJsonRpc("tools/list", new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(Xunit.TestContext.Current.CancellationToken);
        var result = ParseResult(body);
        var names = result.GetProperty("tools")
            .EnumerateArray()
            .Select(t => t.GetProperty("name").GetString())
            .ToArray();

        Assert.Contains("get_flag", names);
    }

    [Fact]
    public async Task Mcp_GetFlag_ReturnsFlagText()
    {
        var response = await PostJsonRpc("tools/call", new { name = "get_flag", arguments = new { } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(Xunit.TestContext.Current.CancellationToken);
        var result = ParseResult(body);
        var content = result.GetProperty("content").EnumerateArray().ToArray();
        Assert.Single(content);
        Assert.Equal(DefaultFlag, content[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Mcp_GetVersion_ReturnsVersionText()
    {
        var version = await CallTool("get_version", new { });
        Assert.Equal(VersionResolver.ReadVersion(), version);
        Assert.Matches(@"^\d+\.\d+\.\d+$", version);
    }

    [Fact]
    public async Task Mcp_Todos_AddListCompleteDelete_RoundTrip()
    {
        Assert.Contains("Saved: [1] change the front lightbulb", await CallTool("add_todo", new { text = "change the front lightbulb" }));
        Assert.Contains("[1] change the front lightbulb", await CallTool("list_todos", new { }));
        Assert.Contains("Completed: [1]", await CallTool("complete_todo", new { id = 1 }));
        Assert.Equal("No todos.", await CallTool("list_todos", new { }));
        Assert.Contains("(done)", await CallTool("list_todos", new { includeDone = true }));
        Assert.Contains("Deleted todo 1", await CallTool("delete_todo", new { id = 1 }));
        Assert.StartsWith("Error", await CallTool("delete_todo", new { id = 1 }));
        Assert.StartsWith("Error", await CallTool("add_todo", new { text = "  " }));
    }

    private async Task<string> CallTool(string name, object args)
    {
        var response = await PostJsonRpc("tools/call", new { name, arguments = args });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = ParseResult(await response.Content.ReadAsStringAsync(Xunit.TestContext.Current.CancellationToken));
        return result.GetProperty("content")[0].GetProperty("text").GetString()!;
    }

    private Task<HttpResponseMessage> PostJsonRpc(string method, object? @params)
    {
        var body = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method,
            @params
        });
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        return _client.SendAsync(request, Xunit.TestContext.Current.CancellationToken);
    }

    private static JsonElement ParseResult(string sse)
    {
        var line = sse.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(l => l.TrimStart().StartsWith("data:", StringComparison.Ordinal));
        Assert.NotNull(line);
        var envelope = JsonDocument.Parse(line!.TrimStart()["data:".Length..].Trim());
        return envelope.RootElement.GetProperty("result");
    }
}
