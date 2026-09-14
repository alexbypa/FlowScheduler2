using Microsoft.Extensions.Logging;

namespace FlowScheduler.Infrastructure.Http;
/// <summary>
/// Handler HTTP globale che logga TUTTE le chiamate in uscita (Gemini, Embedding, ecc.)
/// </summary>
public class LoggingHttpHandler : DelegatingHandler {
    private readonly ILogger<LoggingHttpHandler> _logger;
    public LoggingHttpHandler(ILogger<LoggingHttpHandler> logger) => _logger = logger;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
        var logMsg = $"[HTTP OUT] {request.Method} {request.RequestUri}";
        _logger.LogInformation(logMsg);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try {
            var response = await base.SendAsync(request, cancellationToken);
            sw.Stop();

            var inMsg = $"[HTTP IN] {(int)response.StatusCode} from {request.RequestUri} ({sw.ElapsedMilliseconds}ms)";
            _logger.LogInformation(inMsg);

            if (!response.IsSuccessStatusCode) {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var errMsg = $"[HTTP ERROR] {(int)response.StatusCode} {body}";
                _logger.LogWarning(errMsg);
            }

            return response;
        } catch (Exception ex) {
            sw.Stop();
            var failMsg = $"[HTTP FAIL] {request.Method} {request.RequestUri} after {sw.ElapsedMilliseconds}ms: {ex.Message}";
            _logger.LogError(ex, failMsg);
            throw;
        }
    }
}
