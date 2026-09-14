using FlowScheduler.Core.Configuration;
using FlowScheduler.Core.Interfaces.Data;
using FlowScheduler.Core.Interfaces.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Text.Json;

namespace FlowScheduler.Infrastructure.AI.Tools;
/// <summary>
/// Esegue query SELECT arbitrarie (con validazione di sicurezza) su database multipli via IDbConnectionFactoryResolver. Salva i risultati nel ContentStore e restituisce il contentId.
/// </summary>
public class SqlTool {
    private readonly IContentStore _contentStore;
    private readonly IDbConnectionFactoryResolver _dbFactoryResolver;
    private readonly string _connectionString;
    private readonly ILogger<SqlTool> _logger;

    public SqlTool(IContentStore contentStore, IDbConnectionFactoryResolver dbFactoryResolver, HangFireOptions hangFireOptions, ILogger<SqlTool> logger) {
        _contentStore = contentStore;
        _dbFactoryResolver = dbFactoryResolver;
        _connectionString = hangFireOptions.ConnectionString!;
        _logger = logger;
    }

    [Description("Esegue una query SQL (SELECT) sul database" +
        " di diagnostica, salva i risultati e restituisce il ContentId.")]
    public async Task<string> ExecuteQueryAndStoreAsync(
        [Description("La query SQL (es. SELECT * FROM ...) da eseguire")] string sqlQuery,
        [Description("Il tipo di database (es. 'SqlServer', 'PostgreSql')")] string databaseType = "SqlServer") {
        _logger.LogInformation("[SQL TOOL] Esecuzione query su {DatabaseType}...", databaseType);
        var resultsList = new List<Dictionary<string, object>>();

        sqlQuery = sqlQuery.Trim();
        if (!sqlQuery.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)) {
            return "ERRORE DI SICUREZZA: Sono permesse solo query di tipo SELECT (lettura).";
        }

        // Controllo extra per evitare query multiple (es: SELECT ...; DROP TABLE ...)
        if (sqlQuery.Contains(";") || sqlQuery.Contains("--")) {
            return "ERRORE DI SICUREZZA: Caratteri non permessi (;) rilevati.";
        }
        try {
            var dbFactory = _dbFactoryResolver.Resolve(databaseType);

            using (var connection = dbFactory.CreateConnection(_connectionString)) {
                await connection.OpenAsync();

                using (var command = connection.CreateCommand()) {
                    command.CommandText = sqlQuery;

                    using (var reader = await command.ExecuteReaderAsync()) {
                        while (await reader.ReadAsync()) {
                            var row = new Dictionary<string, object>();
                            for (int i = 0; i < reader.FieldCount; i++) {
                                row[reader.GetName(i)] = reader.GetValue(i);
                            }
                            resultsList.Add(row);
                        }
                    }
                }
            }

            // 4. Trasformiamo in JSON e mettiamo in magazzino
            string resultsJson = JsonSerializer.Serialize(resultsList);
            string contentId = await _contentStore.SetAsync(resultsJson);

            // 5. Restituiamo il "Bigliettino"
            _logger.LogInformation("[SQL TOOL] Query ok. ContentId: {ContentId}", contentId);
            return $"Query eseguita con successo. I risultati ({resultsList.Count} righe) sono salvati. " +
                   $"ContentId: {contentId}. Comunica questo ID per l'analisi.";

        } catch (Exception ex) {
            _logger.LogError(ex, "[SQL TOOL ERROR] {Error}", ex.Message);
            return $"Errore durante l'esecuzione della query SQL: {ex.Message}";
        }
    }
}