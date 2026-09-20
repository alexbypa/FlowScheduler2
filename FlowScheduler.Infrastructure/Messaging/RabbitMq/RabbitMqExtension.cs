using FlowScheduler.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FlowScheduler.Infrastructure.Messaging.RabbitMq;

public static class RabbitMqExtension
{
    public static IServiceCollection AddRabbitMqInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RabbitMqOptions>(configuration.GetSection("RabbitMq"));
        services.AddSingleton<RabbitMqConsumerService>();
        services.AddScoped<DynamicJsonDbWriter>();
        return services;
    }
}
