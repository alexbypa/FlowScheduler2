using FlowScheduler.Core.Configuration;
using FlowScheduler.Core.Interfaces.Monitoring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FlowScheduler.Infrastructure.Monitoring;

public static class MonitoringExtension {
    public static IServiceCollection AddMonitoring(this IServiceCollection services, IConfiguration configuration){
        services.Configure<HealthMonitorOptions>(configuration.GetSection("HealthMonitorOptions"));
        services.AddScoped<IHealthMonitorService, HealthMonitorService>();
        return services;
    }
}

