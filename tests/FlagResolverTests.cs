using TodoMcp;

namespace TodoMcp.Tests;

public class FlagResolverTests
{
    [Fact]
    public void ReadFlag_ReturnsDefault_WhenEnvironmentVariableUnset()
    {
        var previous = Environment.GetEnvironmentVariable("TODOMCP_FLAG");
        Environment.SetEnvironmentVariable("TODOMCP_FLAG", null);
        try
        {
            Assert.Equal("FLAG-lightbulb-1234", FlagResolver.ReadFlag());
        }
        finally
        {
            Environment.SetEnvironmentVariable("TODOMCP_FLAG", previous);
        }
    }

    [Fact]
    public void ReadFlag_ReturnsEnvValue_WhenEnvironmentVariableSet()
    {
        var previous = Environment.GetEnvironmentVariable("TODOMCP_FLAG");
        Environment.SetEnvironmentVariable("TODOMCP_FLAG", "FLAG-test-42");
        try
        {
            Assert.Equal("FLAG-test-42", FlagResolver.ReadFlag());
        }
        finally
        {
            Environment.SetEnvironmentVariable("TODOMCP_FLAG", previous);
        }
    }
}
