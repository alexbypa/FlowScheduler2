namespace FlowScheduler.WebApi.Configuration {
    public class InspectingHandler(ILogger<InspectingHandler> logger) : DelegatingHandler {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            logger.LogInformation("[AI DEBUG REQUEST] {Method} {RequestUri}", request.Method, request.RequestUri);

            var response = await base.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode) {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError("[AI DEBUG ERROR RESPONSE] Status: {StatusCode} Body: {ErrorBody}", response.StatusCode, errorBody);
            }

            return response;
        }
    }
}