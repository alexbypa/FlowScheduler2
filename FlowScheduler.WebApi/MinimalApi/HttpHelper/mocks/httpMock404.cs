using CSharpEssentials.HttpHelper.HttpMocks;
using System.Net;

namespace FlowScheduler.WebApi.MinimalApi.HttpHelper.mocks {
    public class httpMock404 : IHttpMockScenario {
        public Func<HttpRequestMessage, bool> Match => (req) => req.RequestUri!.AbsoluteUri.Contains("mock404");
        public IReadOnlyList<Func<Task<HttpResponseMessage>>> ResponseFactory =>
            new List<Func<Task<HttpResponseMessage>>> {
            () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) {
                Content = new StringContent("{\"sessionToken\": \"MOCK_TOKEN_123\"}")
            })
        };
    }
}