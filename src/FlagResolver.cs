namespace TodoMcp;

/// <summary>Reads the flag from <c>TODOMCP_FLAG</c>; falls back to a compile-time default.</summary>
public static class FlagResolver
{
    public static string ReadFlag() =>
        Environment.GetEnvironmentVariable("TODOMCP_FLAG") ?? "FLAG-lightbulb-1234";
}
