using System.Reflection;

namespace FlowScheduler.WebApi.MinimalApi;
public static class EndpointDefinitionExtensions {
    public static IServiceCollection AddEndpointDefinitions(this IServiceCollection services) {
        var endpointDefinitionType = typeof(IEndpointDefinition);

        var implementations = Assembly.GetExecutingAssembly().GetTypes()
                    .Where(t => endpointDefinitionType.IsAssignableFrom(t)
                                && !t.IsInterface
                                && !t.IsAbstract);

        foreach (var implementation in implementations) {
            services.AddTransient(endpointDefinitionType, implementation);
        }
        return services;
    }
    public static WebApplication UseEndpointDefinitions(this WebApplication app) {
        using var scope = app.Services.CreateScope(); // <-- crea scope
        var defs = scope.ServiceProvider.GetServices<IEndpointDefinition>();
        foreach (var def in defs)
            def.DefineEndpoints(app);
        return app;
    }
}