namespace FlowScheduler.Core.Entities;

public class MonitorTask {
    public string CommandType { get; set; }
    // Obbligatorio per Cosmos DB
    public string id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    // L'URL della Web API da chiamare
    public string TargetUrl { get; set; } = string.Empty;
    // Espressione Cron (es: "*/5 * * * *" per ogni 5 minuti)
    /// <summary>
    /// * (Asterisco): Sempre (ogni minuto, ogni ora, ecc.).
    /// , (Virgola): Lista(es: 1,15,30 significa al minuto 1, 15 e 30).
    /// - (Trattino): Intervallo(es: 1-5 nel campo giorni significa da Lunedì a Venerdì).
    /// / (Slash): Incremento(es: */10 significa ogni 10 unità).
    /// </summary>
    public string CronExpression { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public DateTime LastExecution { get; set; }
    // Fondamentale per AZ-305: La Partition Key. 
    // In un sistema di task, potremmo partizionare per "OwnerId" o "TaskType"
    public string Version { get; set; }
    public string PartitionKey { get; set; } = "General";
}