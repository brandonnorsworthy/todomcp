using TodoMcp;

namespace TodoMcp.Tests;

/// <summary>Test double so the endpoint tests need no database.</summary>
public sealed class InMemoryTodoStore : ITodoStore
{
    private readonly List<Todo> _items = [];
    private long _next = 1;

    public Task<Todo> AddAsync(string text, CancellationToken ct)
    {
        var t = new Todo(_next++, text, false, DateTimeOffset.UtcNow);
        _items.Add(t);
        return Task.FromResult(t);
    }

    public Task<IReadOnlyList<Todo>> ListAsync(bool includeDone, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Todo>>(_items.Where(t => includeDone || !t.Done).ToList());

    public Task<Todo?> CompleteAsync(long id, CancellationToken ct)
    {
        var i = _items.FindIndex(t => t.Id == id);
        if (i < 0)
        {
            return Task.FromResult<Todo?>(null);
        }

        _items[i] = _items[i] with { Done = true };
        return Task.FromResult<Todo?>(_items[i]);
    }

    public Task<bool> DeleteAsync(long id, CancellationToken ct) =>
        Task.FromResult(_items.RemoveAll(t => t.Id == id) > 0);
}
