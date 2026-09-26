using FlowScheduler.Core.Configuration;
using FlowScheduler.Core.Dtos;
using FlowScheduler.Core.Interfaces.Data;
using FlowScheduler.Core.Interfaces.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;


namespace FlowScheduler.Infrastructure.AI.Tools;

/// <summary>
/// Layer 4: Registrazione DI dei Tool AI concreti.
/// Ogni tool viene registrato con la sua factory per risolvere dipendenze specifiche.
/// </summary>
public static class AiToolsExtension {
    public static IServiceCollection AddAiTools(this IServiceCollection services) {
        services.AddSingleton<DatabaseTool>(sp => {
            var dbFactoryResolver = sp.GetRequiredService<IDbConnectionFactoryResolver>();
            var opts = sp.GetRequiredService<HangFireOptions>();
            return new DatabaseTool(dbFactoryResolver, opts, sp.GetRequiredService<ILoggerFactory>().CreateLogger<DatabaseTool>());
        });

        services.AddSingleton<IFilePathResolver, StackTraceFilePathResolver>();

        services.AddSingleton<GitHubTool>(sp => {
            var githubOptions = sp.GetRequiredService<IOptions<GitHubOptions>>().Value;
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var pathResolver = sp.GetRequiredService<IFilePathResolver>();
            return new GitHubTool(httpClientFactory, githubOptions.Values ?? [], pathResolver, sp.GetRequiredService<ILoggerFactory>().CreateLogger<GitHubTool>());
        });

        services.AddScoped<RagTool>();
        services.AddScoped<DiagnosticTool>();

        services.AddScoped<SqlDiagnosticsTool>(sp => {
            var dbFactoryResolver = sp.GetRequiredService<IDbConnectionFactoryResolver>();
            var opts = sp.GetRequiredService<HangFireOptions>();
            return new SqlDiagnosticsTool(dbFactoryResolver, opts, sp.GetRequiredService<ILoggerFactory>().CreateLogger<SqlDiagnosticsTool>());
        });

        services.AddScoped<SqlTool>(sp => {
            var dbConnectionFactoryResolver = sp.GetRequiredService<IDbConnectionFactoryResolver>();
            var contentStore = sp.GetRequiredService<IContentStore>();
            var opts = sp.GetRequiredService<HangFireOptions>();
            return new SqlTool(contentStore, dbConnectionFactoryResolver, opts, sp.GetRequiredService<ILoggerFactory>().CreateLogger<SqlTool>());
        });

        return services;
    }
}