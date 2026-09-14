using FlowScheduler.Core.Configuration;
using FlowScheduler.Core.Interfaces.AI;
using FlowScheduler.Core.Interfaces.MCP;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using System.Collections.Concurrent;

namespace FlowScheduler.Infrastructure.MCP;

public sealed class McpClientService : IMcpClientService, IAsyncDisposable {
    private readonly ConcurrentDictionary<string, McpClient> _clients = new();
    private readonly ILogger<McpClientService> _logger;
    private readonly IToolRegistry _toolRegistry;
    private readonly McpServerOptions _options;

    public McpClientService(IOptions<McpServerOptions> options, ILogger<McpClientService> logger, IToolRegistry toolRegistry) {
        _options = options.Value;
        _logger = logger;
        _toolRegistry = toolRegistry;
    }
    public async Task RegisterAllServersAsync(CancellationToken ct = default) {
        if (_options.McpServers == null || _options.McpServers.Count == 0) {
            _logger.LogWarning("[MCP] Nessun server MCP configurato in McpServerOptions");
            return;
        }
        foreach (var server in _options.McpServers) {
            try {
                await RegisterToolsInRegistryAsync(server.Key, ct);
            } catch (Exception ex) {
                _logger.LogError(ex, "[MCP] Errore registrazione tool da server {ServerName}", server.Key);
            }
        }
    }

    public async Task RegisterToolsInRegistryAsync(string serverName, CancellationToken ct = default) {
        // → GetOrCreateClient → ListToolsAsync → foreach: WithName + Register in ToolRegistry
        var client = await GetOrCreateClientAsync(serverName, ct);
        var tools = await client.ListToolsAsync(cancellationToken: ct);
        foreach(var tool in tools) {
            var renamedTool = tool.WithName($"{serverName}_{tool.Name}");
            _toolRegistry.Register(renamedTool.Name, _=> renamedTool);
            _logger.LogInformation("[MCP] Tool registrato: {ToolName} da server {ServerName}", renamedTool.Name, serverName);
        }
    }
    private async Task<McpClient> GetOrCreateClientAsync(string serverName, CancellationToken ct) {
        var client = _clients.TryGetValue(serverName, out var existingClient) ? existingClient : null;
        if (client == null) {
            _logger.LogInformation("[MCP] Creating new client for server '{Server}'", serverName);

            var config = _options.McpServers?.FirstOrDefault(s => s.Key.Equals(serverName, StringComparison.OrdinalIgnoreCase)).Value;
            if (config == null) {
                _logger.LogError("[MCP] Server '{Server}' not found in config. Available servers: {Servers}",
                    serverName, _options.McpServers != null
                        ? string.Join(", ", _options.McpServers.Keys)
                        : "(none)");
                throw new KeyNotFoundException($"Server MCP '{serverName}' non trovato in configurazione.");
            }

            _logger.LogInformation("[MCP] Config for '{Server}': Transport={Transport}, BaseUrl={BaseUrl}, Command={Command}",
                serverName, config.Transport ?? "(null)", config.BaseUrl ?? "(null)", config.Command ?? "(null)");

            client = await McpClient.CreateAsync(CreateTransport(config));
            _clients.TryAdd(serverName, client);
            _logger.LogInformation("[MCP] Client created and cached for '{Server}'", serverName);
        } else {
            _logger.LogDebug("[MCP] Using cached client for '{Server}'", serverName);
        }
        return _clients[serverName];
    }
    internal static IClientTransport CreateTransport(McpServerEntry config) {
        switch (config.Transport?.ToLowerInvariant()) {
            case "stdio":
                if (string.IsNullOrWhiteSpace(config.Command)) {
                    throw new ArgumentException("Il campo 'Command' è obbligatorio per il trasporto 'stdio'.");
                }
                return new StdioClientTransport(new StdioClientTransportOptions {
                    Command = config.Command,
                    Arguments = config.Args ?? Array.Empty<string>(),
                    EnvironmentVariables = config.Env ?? new Dictionary<string, string>()
                });
            case "http":
                if (string.IsNullOrWhiteSpace(config.BaseUrl)) {
                    throw new ArgumentException("Il campo 'BaseUrl' è obbligatorio per il trasporto 'http'.");
                }
                return new HttpClientTransport(new HttpClientTransportOptions {
                    Endpoint = new Uri(config.BaseUrl)
                });
            default:
                throw new ArgumentException($"Trasporto '{config.Transport}' non supportato. Valori validi: 'stdio', 'http'.");
        }
    }
    public async Task<string> CallToolAsync(string serverName, string toolName, Dictionary<string, object?> arguments, CancellationToken ct = default) {
        _logger.LogInformation("[MCP] CallToolAsync: server={Server}, tool={Tool}, args={Args}",
            serverName, toolName, System.Text.Json.JsonSerializer.Serialize(arguments));

        var client = await GetOrCreateClientAsync(serverName, ct);
        var result = await client.CallToolAsync(toolName, arguments, cancellationToken: ct);

        _logger.LogInformation("[MCP] CallToolAsync result: ContentCount={Count}, IsError={IsError}",
            result.Content.Count, result.IsError);

        for (int i = 0; i < result.Content.Count; i++) {
            var content = result.Content[i];
            var text = content.ToString();
            _logger.LogInformation("[MCP] Content[{Index}] Type={Type}, Len={Len}, Preview={Preview}",
                i, content.GetType().Name, text?.Length ?? -1,
                text?.Length > 300 ? text[..300] + "..." : text ?? "(null)");
        }

        var response = result.Content.FirstOrDefault()?.ToString() ?? "{}";
        _logger.LogInformation("[MCP] Final response (len={Len}): {Preview}",
            response.Length, response.Length > 500 ? response[..500] + "..." : response);

        return response;
    }

    public async ValueTask DisposeAsync() {
        foreach (var client in _clients.Values) {
            try {
                await client.DisposeAsync();
            } catch { }
        }
    }
}