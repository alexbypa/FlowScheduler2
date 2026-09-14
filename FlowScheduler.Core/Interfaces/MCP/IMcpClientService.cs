using FlowScheduler.Core.Dtos;

namespace FlowScheduler.Core.Interfaces.MCP;

/// <summary>
/// Servizio client MCP. Gestisce connessioni a server MCP esterni,
/// discovery dei tool e registrazione nel ToolRegistry.
///
/// SOLID — DIP: astrazione in Core, implementazione in Infrastructure.
/// SOLID — ISP: interfaccia focalizzata su operazioni client MCP.
/// </summary>
public interface IMcpClientService {
    /// <summary>
    /// Invoca un tool su un server MCP specifico.
    /// </summary>
    Task<string> CallToolAsync(string serverName, string toolName, Dictionary<string, object?> arguments, CancellationToken ct = default);

    /// <summary>
    /// Scopre tool da un server MCP e li registra nel ToolRegistry con prefisso {serverName}_.
    /// </summary>
    Task RegisterToolsInRegistryAsync(string serverName, CancellationToken ct = default);

    /// <summary>
    /// Registra tool da tutti i server MCP configurati.
    /// </summary>
    Task RegisterAllServersAsync(CancellationToken ct = default);
}
