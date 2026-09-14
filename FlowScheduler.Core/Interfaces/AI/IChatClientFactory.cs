using Microsoft.Extensions.AI;

namespace FlowScheduler.Core.Interfaces.AI;

/// <summary>
/// Factory per la selezione del client AI in base al ModelTier richiesto.
///
/// SOLID — DIP: astrazione in Core, implementazione in Infrastructure (AdvisorChatClientFactory).
/// SOLID — OCP: nuovi tier aggiungibili estendendo ModelTier senza toccare i consumer.
/// </summary>
public interface IChatClientFactory {
    /// <summary>
    /// Restituisce il client appropriato per il tier specificato.
    /// </summary>
    /// <param name="tier">Tier dal descrittore dell'agente.</param>
    /// <param name="prompt">Prompt opzionale per Auto-routing (ignorato per Primary/Local).</param>
    IChatClient GetClient(ModelTier tier, string? prompt = null);

    /// <summary>
    /// Restituisce il client per un ModelTier espresso come stringa (dal config JSON).
    /// </summary>
    IChatClient GetClient(string modelTierStr, string? prompt = null);
}
