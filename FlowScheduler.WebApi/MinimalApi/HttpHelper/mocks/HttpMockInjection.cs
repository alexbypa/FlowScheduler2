using CSharpEssentials.HttpHelper;
using CSharpEssentials.HttpHelper.HttpMocks;

namespace FlowScheduler.WebApi.MinimalApi.HttpHelper.mocks;

public static class HttpMockInjection {
    public static IServiceCollection InjectMocks(this IServiceCollection services) {
        services.AddTransient<IHttpMockEngine, HttpMockEngine>();
        services.AddSingleton<HttpMockDelegatingHandler>();
        services.Scan(scan => scan
            .FromApplicationDependencies() // Esplora le dipendenze dell'app
            .AddClasses(classes => classes.AssignableTo<IHttpMockScenario>()
            .Where(type => type != typeof(HttpMockScenario))
            ) // Trova chi implementa l'interfaccia
            .AsImplementedInterfaces() // Registrali come IHttpMockScenario
            .WithTransientLifetime()); // Mantieni il ciclo di vita Transient come raccomandato

        return services;
    }
}