using FlowScheduler.Core.Configuration;
using FlowScheduler.Core.Interfaces.Data;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Net.Sockets;
using System.Text;

namespace FlowScheduler.Infrastructure.AI.Tools;

/// <summary>
/// Interroga direttamente SQL Server per estrarre errori recenti dalla tabella LogEntry. È un tool "meccanico" usato dall'orchestratore senza ragionamento LLM intermedio.
/// </summary>
public class DatabaseTool {
    private readonly IDbConnectionFactoryResolver _dbFactoryResolver;
    private readonly string _connectionString;
    private readonly ILogger<DatabaseTool> _logger;
    public DatabaseTool(IDbConnectionFactoryResolver dbFactoryResolver, HangFireOptions hangFireOptions, ILogger<DatabaseTool> logger) {
        _dbFactoryResolver = dbFactoryResolver;
        _connectionString = hangFireOptions.ConnectionString!;
        _logger = logger;
    }
    [Description("Interroga il database del monitoraggio per trovare errori tecnici o anomalie negli ultimi minuti.")]
    public async Task<string> GetDatabaseErrors(
        [Description("Il numero di minuti indietro nel tempo (es. 60 per l'ultima ora)")] int minutes,
        [Description("Il tipo di database (es. 'SqlServer', 'PostgreSql')")] string databaseType = "SqlServer") {

        _logger.LogInformation("[DB TOOL] Esecuzione query per gli ultimi {Minutes} minuti su {DatabaseType}...", minutes, databaseType);

        try {
            var dbFactory = _dbFactoryResolver.Resolve(databaseType);
            using var connection = dbFactory.CreateConnection(_connectionString);
            await connection.OpenAsync();
            _logger.LogInformation("[DB TOOL] Connessione SQL aperta con successo");

            var sql = @"
                select TimeStamp, Message, Level, Exception
                from betfair.LogEntry with (NOLOCK)
                where TimeStamp > dateadd(minute, -@mins, GETDATE())
                and Level IN ('Error', 'Fatal')
                order by timeStamp";

            using var command = dbFactory.CreateCommand(sql, connection);
            dbFactory.AddParameter(command, "@mins", minutes);

            using var reader = await command.ExecuteReaderAsync();

            var sb = new StringBuilder();
            sb.AppendLine($"--- REPORT ERRORI REALI (Ultimi {minutes} min) ---");

            bool hasRows = false;
            while (await reader.ReadAsync()) {
                hasRows = true;
                var date = reader.GetDateTime(0).ToString("HH:mm:ss");
                var msg = reader.GetString(1);
                var exp = reader.GetString(2);
                sb.AppendLine($"[{date}] {exp}: {msg}");
            }

            if (!hasRows)
                return $"Nessun errore trovato negli ultimi {minutes} minuti. Tutto sembra funzionare correttamente.";

            return sb.ToString();
        } catch (Exception ex) {
            return $"Errore durante l'interrogazione del database: {ex.Message}";
        }
    }
}