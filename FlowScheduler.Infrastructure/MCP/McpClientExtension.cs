using FlowScheduler.Core.Configuration;
using FlowScheduler.Core.Interfaces.MCP;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FlowScheduler.Infrastructure.MCP;

public static class  McpClientExtension{
    public static IServiceCollection AddMCPClient(this IServiceCollection services, IConfiguration configuration) {
        services.Configure<McpServerOptions>(configuration.GetSection("McpServerOptions"));
        services.AddSingleton<IMcpClientService, McpClientService>();
        return services;
    }
}