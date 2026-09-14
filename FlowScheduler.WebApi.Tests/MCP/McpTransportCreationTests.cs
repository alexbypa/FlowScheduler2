using FlowScheduler.Core.Configuration;
using FlowScheduler.Infrastructure.MCP;
using ModelContextProtocol.Client;

namespace FlowScheduler.WebApi.Tests.MCP;

public class McpTransportCreationTests
{
    [Fact]
    public void CreateTransport_Stdio_ReturnsStdioClientTransport()
    {
        var config = new McpServerEntry
        {
            Transport = "stdio",
            Command = "npx",
            Args = ["projectpulse-mcp"]
        };

        var transport = McpClientService.CreateTransport(config);

        Assert.IsType<StdioClientTransport>(transport);
    }

    [Fact]
    public void CreateTransport_Http_ReturnsHttpClientTransport()
    {
        var config = new McpServerEntry
        {
            Transport = "http",
            BaseUrl = "http://localhost:3001/mcp"
        };

        var transport = McpClientService.CreateTransport(config);

        Assert.IsType<HttpClientTransport>(transport);
    }

    [Fact]
    public void CreateTransport_Stdio_MissingCommand_Throws()
    {
        var config = new McpServerEntry { Transport = "stdio", Command = null };

        Assert.Throws<ArgumentException>(() => McpClientService.CreateTransport(config));
    }

    [Fact]
    public void CreateTransport_Http_MissingBaseUrl_Throws()
    {
        var config = new McpServerEntry { Transport = "http", BaseUrl = null };

        Assert.Throws<ArgumentException>(() => McpClientService.CreateTransport(config));
    }

    [Fact]
    public void CreateTransport_UnknownTransport_Throws()
    {
        var config = new McpServerEntry { Transport = "grpc" };

        Assert.Throws<ArgumentException>(() => McpClientService.CreateTransport(config));
    }

    [Fact]
    public void CreateTransport_CaseInsensitive()
    {
        var config = new McpServerEntry
        {
            Transport = "STDIO",
            Command = "node",
            Args = ["server.js"]
        };

        var transport = McpClientService.CreateTransport(config);

        Assert.IsType<StdioClientTransport>(transport);
    }

    [Fact]
    public void CreateTransport_Stdio_WithEnvVars()
    {
        var config = new McpServerEntry
        {
            Transport = "stdio",
            Command = "npx",
            Args = ["projectpulse-mcp"],
            Env = new() { ["GITHUB_TOKEN"] = "test-token" }
        };

        var transport = McpClientService.CreateTransport(config);

        Assert.IsType<StdioClientTransport>(transport);
    }
}
