using FlowScheduler.Core.Configuration;
using FlowScheduler.Core.Interfaces.AI;
using Microsoft.Extensions.Logging;

namespace FlowScheduler.Infrastructure.AI.Advisor;

/// <summary>
/// Implementazione leggera dell'Advisor Strategy basata su keyword matching.
///
/// Analisi del prompt:
/// 1. Stima token count (chars / 4) — approssimazione standard GPT.
/// 2. Conta hit di keyword complesse (peso +2 ciascuna).
/// 3. Conta hit di keyword semplici (peso -1 ciascuna).
/// 4. Score = complexHits * 2 - simpleHits.
/// 5. Se Score > 0 AND estimatedTokens > threshold → Primary (modello pesante).
///    Altrimenti → Local (modello leggero).
///
/// SOLID — SRP: valuta SOLO la complessita, nessun side effect.
/// SOLID — OCP: nuove keyword aggiungibili da config senza toccare codice.
/// SOLID — DIP: implementa IAdvisorStrategy (Core).
///
/// Performance: O(prompt.Length * keywords.Count), nessuna allocazione,
/// nessun I/O, nessuna chiamata LLM. Benchmark atteso: &lt;0.05ms per prompt tipico.
/// </summary>
public sealed class ComplexityAdvisorStrategy : IAdvisorStrategy {
    private readonly AdvisorOptions _options;
    private readonly ILogger<ComplexityAdvisorStrategy> _logger;

    public ComplexityAdvisorStrategy(AdvisorOptions options, ILogger<ComplexityAdvisorStrategy> logger) {
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public ModelTier Evaluate(string prompt) {
        if (!_options.Enabled) {
            _logger.LogDebug("[Advisor] Disabilitato, routing a Primary");
            return ModelTier.Primary;
        }

        if (string.IsNullOrWhiteSpace(prompt)) {
            return ModelTier.Local;
        }

        // Step 1: Stima token
        int estimatedTokens = prompt.Length / 4;

        // Step 2-3: Keyword scoring (case-insensitive)
        var promptLower = prompt.AsSpan();
        int complexHits = CountKeywordHits(prompt, _options.ComplexKeywords);
        int simpleHits = CountKeywordHits(prompt, _options.SimpleKeywords);

        // Step 4: Score
        int score = (complexHits * 2) - simpleHits;

        // Step 5: Decision
        bool isComplex = score > 0 && estimatedTokens > _options.SimpleTaskMaxTokens;
        var tier = isComplex ? ModelTier.Primary : ModelTier.Local;

        _logger.LogDebug(
            "[Advisor] Prompt tokens~{Tokens}, complexHits={Complex}, simpleHits={Simple}, score={Score} → {Tier}",
            estimatedTokens, complexHits, simpleHits, score, tier);

        return tier;
    }

    private static int CountKeywordHits(string prompt, List<string> keywords) {
        int hits = 0;
        foreach (var keyword in keywords) {
            if (prompt.Contains(keyword, StringComparison.OrdinalIgnoreCase)) {
                hits++;
            }
        }
        return hits;
    }
}
