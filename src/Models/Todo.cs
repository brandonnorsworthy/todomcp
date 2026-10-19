namespace TodoMcp.Models;

/// <summary>A saved todo item. <paramref name="Position"/> only orders items within their project.</summary>
public sealed record Todo(long Id, string Project, string Text, bool Done, DateTimeOffset CreatedAt, long Position);
