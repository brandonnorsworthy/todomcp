using System.Reflection;

namespace TodoMcp;

/// <summary>
/// Reports the build version stamped into the assembly (the git tag/sha passed as
/// <c>GIT_VERSION</c> at image build time); <c>dev</c> for local builds.
/// </summary>
public static class VersionResolver
{
    public static string ReadVersion() =>
        typeof(VersionResolver).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "dev";
}
