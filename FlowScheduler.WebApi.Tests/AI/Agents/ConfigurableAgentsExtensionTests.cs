using FlowScheduler.Core.Configuration;
using FlowScheduler.Infrastructure.AI.Agents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Agents.AI.Hosting;
using Xunit;
using System.Linq;

namespace FlowScheduler.WebApi.Tests.AI.Agents;

public class ConfigurableAgentsExtensionTests
{
    [Fact]
    public void AddConfigurableAgentsFromOptions_ShouldRegisterAgentFactory()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new AiOrchestrationOptions();

        // Act
        services.AddConfigurableAgentsFromOptions(options);

        // Assert
        var hasAgentFactory = services.Any(d => d.ServiceType == typeof(AgentFactory) && d.Lifetime == ServiceLifetime.Singleton);
        Assert.True(hasAgentFactory, "AgentFactory should be registered as a Singleton.");
    }

    [Fact]
    public void AddConfigurableAgentsFromOptions_ShouldRegisterEnabledAgentsAsKeyedServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new AiOrchestrationOptions();
        options.Agents.Add("enabled_agent", new AgentDescriptor { Enabled = true, Name = "EnabledAgent" });
        options.Agents.Add("disabled_agent", new AgentDescriptor { Enabled = false, Name = "DisabledAgent" });

        // Act
        services.AddConfigurableAgentsFromOptions(options);

        // Assert
        var keyedServices = services.Where(d => d.IsKeyedService).ToList();
        
        var enabledAgentRegistration = keyedServices.FirstOrDefault(d => 
            d.ServiceKey is string key && key == "enabled_agent");
            
        Assert.NotNull(enabledAgentRegistration);

        var disabledAgentRegistration = keyedServices.FirstOrDefault(d => 
            d.ServiceKey is string key && key == "disabled_agent");
            
        Assert.Null(disabledAgentRegistration);
    }
}
