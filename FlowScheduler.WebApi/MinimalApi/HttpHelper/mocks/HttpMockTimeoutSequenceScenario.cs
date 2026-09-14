using System.Net;
using CSharpEssentials.HttpHelper.HttpMocks;

namespace FlowScheduler.WebApi.MinimalApi.HttpHelper.mocks;
public class MockTimeoutSequenceScenario : IHttpMockScenario {
    // Intercettiamo le chiamate a un endpoint specifico per il test dei timeout
    public Func<HttpRequestMessage, bool> Match => req =>
        req.RequestUri.AbsoluteUri.Contains("api/timeout-sequence-test");

    public IReadOnlyList<Func<Task<HttpResponseMessage>>> ResponseFactory =>
        new List<Func<Task<HttpResponseMessage>>> {
            // 1° Tentativo: Forza il timeout (es. attende 10 secondi)
            async () => {
                await Task.Delay(10000);
                return new HttpResponseMessage(HttpStatusCode.BadGateway);
            },
            // 2° Tentativo: Forza ancora il timeout
            async () => {
                await Task.Delay(10000);
                return new HttpResponseMessage(HttpStatusCode.RequestTimeout);
            },
            // 3° Tentativo: Risponde immediatamente con 200 OK
            () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent("{\"status\":\"success_after_two_timeouts\"}")
            })
        };
}