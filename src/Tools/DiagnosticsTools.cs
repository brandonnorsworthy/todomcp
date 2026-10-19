using ModelContextProtocol.Server;

namespace TodoMcp.Tools;

[McpServerToolType]
public class DiagnosticsTools
{
    /// <summary>Returns the secret flag string.</summary>
    [McpServerTool(Name = "get_flag", Title = "Get flag", ReadOnly = true)]
    public string GetFlag() => FlagResolver.ReadFlag();

    /// <summary>Returns the git version (tag and commit) this server was built from.</summary>
    [McpServerTool(Name = "get_version", Title = "Get version", ReadOnly = true)]
    public string GetVersion() => VersionResolver.ReadVersion();
}
