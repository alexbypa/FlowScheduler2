using FlowScheduler.BackgroundJobs.Consumers;
using FlowScheduler.BackgroundJobs.Hosting;
using FlowScheduler.Core.Interfaces.Messaging;
using FlowScheduler.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace FlowScheduler.BackgroundJobs.Extensions;

/// <summary>
/// Registrazione DI per il sottosistema Telegram: polling, handler e service.
/// </summary>
public static class TelegramServicesExtension {
    public static IServiceCollection AddTelegramServices(this IServiceCollection services) {
        services.AddHostedService<TelegramPollingService>();
        services.AddSingleton<ITelegramService, TelegramService>();
        services.AddTransient<TelegramChatHandler>();
        return services;
    }
}
