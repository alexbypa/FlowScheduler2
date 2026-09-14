namespace FlowScheduler.Core.Configuration;

/// <summary>
/// Options POCO per la configurazione di server MCP esterni.
/// Bind da appsettings.json sezione root (contiene McpServers dictionary).
///
/// SOLID — SRP: solo configurazione, nessuna logica.
/// </summary>
public sealed class McpServerOptions
{
    public Dictionary<string, McpServerEntry> McpServers { get; set; } = new();
}

/// <summary>
/// Configurazione singolo server MCP. Supporta transport stdio e HTTP.
/// </summary>
public sealed class McpServerEntry
{
    /// <summary>Tipo transport: "stdio" o "http". Default: "stdio".</summary>
    public string Transport { get; set; } = "stdio";

    /// <summary>Comando per stdio transport (es. "npx", "node").</summary>
    public string? Command { get; set; }

    /// <summary>Argomenti comando stdio (es. ["projectpulse-mcp"]).</summary>
    public string[]? Args { get; set; }

    /// <summary>Variabili d'ambiente per processo stdio.</summary>
    public Dictionary<string, string>? Env { get; set; }

    /// <summary>URL base per HTTP transport (es. "http://localhost:3001/mcp").</summary>
    public string? BaseUrl { get; set; }
}
