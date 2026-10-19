using TodoMcp;

namespace TodoMcp.Tests;

/// <summary>Tool behaviour over the in-memory store.</summary>
public class TodoToolsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly TodoTools _tools = new(new InMemoryTodoStore());

    [Fact]
    public async Task Add_DefaultsToInboxAndAppends()
    {
        await _tools.AddTodo("a", ct: Ct);
        await _tools.AddTodo("b", ct: Ct);
        Assert.Equal("## Inbox\n1. [1] a\n2. [2] b", await _tools.ListTodos(ct: Ct));
    }

    [Fact]
    public async Task Add_WithPosition_InsertsInTheMiddle()
    {
        await _tools.AddTodo("one", "stacks", ct: Ct);
        await _tools.AddTodo("three", "stacks", ct: Ct);
        await _tools.AddTodo("two", "Stacks", 2, ct: Ct); // case-insensitive project match
        Assert.Equal("## stacks\n1. [1] one\n2. [3] two\n3. [2] three", await _tools.ListTodos("stacks", ct: Ct));
        await _tools.AddTodo("zero", "stacks", 1, ct: Ct);
        Assert.Equal("stacks: [4] zero", await _tools.NextTodo("stacks", ct: Ct));
    }

    [Fact]
    public async Task Add_PositionPastEnd_Appends()
    {
        await _tools.AddTodo("a", "p", ct: Ct);
        await _tools.AddTodo("b", "p", 99, ct: Ct);
        Assert.Equal("## p\n1. [1] a\n2. [2] b", await _tools.ListTodos("p", ct: Ct));
    }

    [Fact]
    public async Task Next_SkipsDone_AndIsPerProject()
    {
        await _tools.AddTodo("w1", "work", ct: Ct);
        await _tools.AddTodo("w2", "work", ct: Ct);
        await _tools.AddTodo("h1", "home", ct: Ct);
        await _tools.CompleteTodo(1, Ct);
        Assert.Equal("home: [3] h1\nwork: [2] w2", await _tools.NextTodo(ct: Ct));
        Assert.Equal("Nothing open in 'nope'.", await _tools.NextTodo("nope", ct: Ct));
    }

    [Fact]
    public async Task Move_ReordersAndChangesProject()
    {
        await _tools.AddTodo("a", "p", ct: Ct);
        await _tools.AddTodo("b", "p", ct: Ct);
        await _tools.AddTodo("c", "p", ct: Ct);
        await _tools.MoveTodo(3, 1, ct: Ct);
        Assert.Equal("## p\n1. [3] c\n2. [1] a\n3. [2] b", await _tools.ListTodos("p", ct: Ct));
        Assert.Contains("(project: q)", await _tools.MoveTodo(1, 1, "q", ct: Ct));
        Assert.Equal("p: 2 open, 0 done\nq: 1 open, 0 done", await _tools.ListProjects(Ct));
    }

    [Fact]
    public async Task Validation()
    {
        Assert.StartsWith("Error", await _tools.AddTodo(" ", ct: Ct));
        Assert.StartsWith("Error", await _tools.AddTodo("x", null, 0, ct: Ct));
        Assert.StartsWith("Error", await _tools.AddTodo("x", new string('p', 101), ct: Ct));
        Assert.StartsWith("Error", await _tools.MoveTodo(9, 1, ct: Ct));
    }
}
