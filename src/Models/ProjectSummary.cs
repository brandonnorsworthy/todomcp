namespace TodoMcp.Models;

/// <summary>A project (list) of todos with its open/done counts.</summary>
public sealed record ProjectSummary(string Name, int Open, int Done);
