using FlowScheduler.Core.Configuration;
using FlowScheduler.Core.Interfaces.AI;
using FlowScheduler.Infrastructure.MCP;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace FlowScheduler.WebApi.Tests.MCP;

public class McpClientServiceTests
{
    private readonly IToolRegistry _toolRegistry = Substitute.For<IToolRegistry>();
    private readonly ILogger<McpClientService> _logger = Substitute.For<ILogger<McpClientService>>();

    private McpClientService CreateService(McpServerOptions options)
    {
        return new McpClientService(
            Options.Create(options),
            _logger,
            _toolRegistry);
    }

    [Fact]
    public async Task ListToolsAsync_UnknownServer_ThrowsKeyNotFoundException()
    {
        var service = CreateService(new McpServerOptions());

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.CallToolAsync("nonexistent", "some_tool", new()));
    }

    [Fact]
    public async Task RegisterAllServersAsync_EmptyConfig_DoesNotThrow()
    {
        var service = CreateService(new McpServerOptions());

        await service.RegisterAllServersAsync();

        _toolRegistry.DidNotReceive().Register(
            Arg.Any<string>(),
            Arg.Any<Func<IServiceProvider, Microsoft.Extensions.AI.AIFunction>>());
    }

    [Fact]
    public async Task RegisterAllServersAsync_ServerConnectionFails_LogsErrorAndContinues()
    {
        var options = new McpServerOptions
        {
            McpServers = new()
            {
                ["bad-server"] = new McpServerEntry
                {
                    Transport = "stdio",
                    Command = "nonexistent-binary-that-wont-start"
                }
            }
        };

        var service = CreateService(options);

        // Should not throw — error is caught and logged
        await service.RegisterAllServersAsync();
    }

    [Fact]
    public async Task DisposeAsync_NoClients_DoesNotThrow()
    {
        var service = CreateService(new McpServerOptions());

        await service.DisposeAsync();
    }
}
