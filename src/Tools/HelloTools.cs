using ModelContextProtocol.Server;

namespace TodoMcp.Tools;

[McpServerToolType]
public class HelloTools
{
    /// <summary>Returns the secret flag string.</summary>
    [McpServerTool(Name = "get_flag", Title = "Get flag", ReadOnly = true)]
    public string GetFlag() => FlagResolver.ReadFlag();
}
