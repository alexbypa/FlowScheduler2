//using CSharpEssentials.HttpHelper;
//using CSharpEssentials.LoggerHelper;
//using GitHub.Copilot.SDK;

//namespace FlowScheduler.WebApi.MinimalApi.HttpHelper;
//public class HttpHelperApi : IEndpointDefinition {
//    private record logRequest(string Action = "test", string IdTransaction = "test", string ApplicationName= "HttpHelper Test") : IRequest;
//    public void DefineEndpoints(WebApplication app) {
//        app.MapGet("/test", async (string url, IhttpsClientHelperFactory httpFactory) => {
//            await using var client = new CopilotClient();
//            await client.StartAsync();

//            foreach (var model in await client.ListModelsAsync()) {
//                Console.WriteLine($"Model: {model.Name}");
//            }

//            await using var session = await client.CreateSessionAsync(
//                new SessionConfig {
//                    Model = "gpt-4o"
//                }
//            );

//            var httpclient = httpFactory.CreateOrGet("Test");
//            var response = await httpclient.SendAsync(url, HttpMethod.Get);
//            string body = await response.Content.ReadAsStringAsync();
//            return Results.Ok(body);
//        })
//        .WithName("First Sample")
//        .WithTags("HttpHelper Operations");

        
//        app.MapGet("/addAction", async (string url, IhttpsClientHelperFactory httpFactory) => {
//            var httpclient = httpFactory.CreateOrGet("Test");

//            Func<HttpRequestMessage, HttpResponseMessage, int, TimeSpan, Task> TraceAction = async (req, res, NrRetry, timebackOff) => {
//                loggerExtension<logRequest>.TraceAsync(new logRequest(), Serilog.Events.LogEventLevel.Information, null, "sending at retry : {retry} Reqest {url} with response {status}", NrRetry, req.RequestUri.ToString(), res.StatusCode.ToString());
//                await Task.CompletedTask;
//            };

//            httpclient.AddRequestAction(TraceAction);

//            var response = await httpclient.SendAsync(url, HttpMethod.Get);
//            string body = await response.Content.ReadAsStringAsync();
//            return Results.Ok(body);
//        })
//        .WithName("Add Logger actions")
//        .WithTags("HttpHelper Operations");


//        app.MapGet("/test-retry", async (string url, short retrycount, double backofffactor, IhttpsClientHelperFactory httpFactory) => {
//            var client = httpFactory.CreateOrGet("Test");

//            client.ClearRequestActions();
//            client.addRetryCondition(
//                res => res.StatusCode != System.Net.HttpStatusCode.OK,
//                retryCount: retrycount,
//                backoffFactor: backofffactor
//            );

//            // Aggiungi un log per vedere i retry in azione
//            client.AddRequestAction(async (req, res, NrRetry, timebackOff) => {
//                loggerExtension<logRequest>.TraceAsync(
//                    new logRequest(
//                        Action : "test-retry", IdTransaction: url), 
//                        res.StatusCode == System.Net.HttpStatusCode.OK ? Serilog.Events.LogEventLevel.Information : Serilog.Events.LogEventLevel.Error, 
//                        null, 
//                        "[RETRY TEST] Attempt: {NrRetry} | Response Body: {body} | Status: {StatusCode} | Wait: {TotalMilliseconds}ms", 
//                        NrRetry, await res.Content.ReadAsStringAsync(), res.StatusCode, timebackOff.TotalMilliseconds
//                    );
//                await Task.CompletedTask;
//            });
//            var response = await client.SendAsync(url, HttpMethod.Get);
                
//            return Results.Ok(new {
//                FinalStatus = response.StatusCode.ToString(),
//                Instructions = "Check logs on your container docker !",
//                body = await response.Content.ReadAsStringAsync()
//            });
//        })
//        .WithName("Add retry")
//        .WithTags("HttpHelper Operations");
//    }
//}