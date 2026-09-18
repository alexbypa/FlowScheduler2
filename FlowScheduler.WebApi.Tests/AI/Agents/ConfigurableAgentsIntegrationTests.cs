using FlowScheduler.Core.Configuration;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowScheduler.WebApi.Tests.AI.Agents;

public class ConfigurableAgentsIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ConfigurableAgentsIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public void AiOrchestration_Agents_AreRegisteredAndResolvable_FromAppSettings()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var options = configuration.GetSection(AiOrchestrationOptions.SectionName).Get<AiOrchestrationOptions>();
        
        Assert.NotNull(options);
        Assert.NotEmpty(options.Agents);

        // Act & Assert
        // Verifichiamo che per ogni agente abilitato in appsettings.json, la dependency injection
        // riesca a costruire l'oggetto AIAgent. Questo prova che l'eliminazione di AddConfigurableAgents
        // non ha rotto la risoluzione a runtime per via della completa validità di AddConfigurableAgentsFromOptions.
        foreach (var (key, descriptor) in options.Agents)
        {
            if (descriptor.Enabled)
            {
                // Tenta di risolvere il KeyedService. Se la factory o le sue dipendenze mancassero,
                // qui verrebbe generata una eccezione di DI.
                var agent = scope.ServiceProvider.GetKeyedService<AIAgent>(key);
                
                Assert.NotNull(agent);
                Assert.Equal(descriptor.Name, agent.Name);
                Assert.Equal(descriptor.Description, agent.Description);
            }
        }
    }
}
