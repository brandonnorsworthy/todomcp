using System.Reflection;

namespace TodoMcp;

/// <summary>Reports the release version (e.g. <c>0.3.1</c>) set by <c>&lt;Version&gt;</c> in the csproj.</summary>
public static class VersionResolver
{
    public static string ReadVersion()
    {
        var version = typeof(VersionResolver).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        // Drop any "+<commit>" build metadata the SDK may append.
        return version?.Split('+')[0] ?? "unknown";
    }
}
