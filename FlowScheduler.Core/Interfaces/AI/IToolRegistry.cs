using Microsoft.Extensions.AI;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace FlowScheduler.Core.Interfaces.AI;

/// <summary>
/// Registry dei tool AI disponibili. Risolve un tool per nome stringa
/// restituendo una AIFunction pronta per essere assegnata a un agente.
///
/// SOLID — DIP: l'astrazione vive in Core, l'implementazione in Infrastructure.
/// SOLID — OCP: nuovi tool registrabili senza modificare il registry.
/// SOLID — ISP: interfaccia minimale con due soli membri.
/// </summary>
public interface IToolRegistry {
    /// <summary>
    /// Risolve un tool per nome (es. "Rag", "GitHub", "Diagnostic").
    /// Restituisce null se il tool non e registrato.
    /// </summary>
    AIFunction? Resolve(string toolName, IServiceProvider sp);

    /// <summary>Elenco di tutti i nomi di tool disponibili.</summary>
    IReadOnlyList<string> AvailableTools { get; }

    /// <summary>
    /// Registra un tool con un nome specifico.
    /// </summary>
    void Register(string toolName, Func<IServiceProvider, AIFunction> factory);
}
