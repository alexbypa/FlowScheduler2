# CSharpEssentials.HttpHelper

A lightweight, robust wrapper around `HttpClient` designed for .NET 9. It simplifies API consumption by providing a fluent factory, automated configuration loading, and built-in support for certificates, proxies, and rate-limiting.

## 📑 Table of Contents <a id='table-of-contents'></a>

* [🚀 Installation & First Use](#using-httphelper)
* [🔍 Observability: Request Interception & Logging](#using-addaction)
* [🛡️ Resilience: Advanced Retries with Polly](#using-retries)
* [🎭 Unit Testing: The HTTP Mocking Engine](#using-mock)

## 🚀 Installation & First Use<a id='using-httphelper'></a>[🔝](#table-of-contents)

### 1. Configuration

The library is designed to be "Configuration-First." You can define your clients directly in your `appsettings.json` or, for a cleaner setup, in a dedicated file named **`appsettings.httphelper.json`**.

```json
{
  "HttpClientOptions": [
    {
      "Name": "Test",
      "Certificate": {
        "Path": "",
        "Password": ""
      },
      "RateLimitOptions": {
        "AutoReplenishment": true,
        "PermitLimit": 1,
        "QueueLimit": 1,
        "Window": "00:00:15",
        "SegmentsPerWindow": 2,
        "IsEnabled": false
      }
    }
  ]
}

```
### 2. Service Registration

Inject the library into your DI container with a single line in `Program.cs`. This will automatically scan for your configuration and register all named clients.

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Register HttpHelper infrastructure and configured clients
builder.Services.AddHttpClients(builder.Configuration);

var app = builder.Build();

```

### 3. Basic Usage (Minimal API)

Use the `IhttpsClientHelperFactory` to resolve a named client and perform requests with an elegant, streamlined syntax.

```csharp
app.MapGet("/test", async (string url, IhttpsClientHelperFactory httpFactory) => 
{
    // Resolve the "Test" client defined in your JSON
    var httpclient = httpFactory.CreateOrGet("Test");

    // Perform a GET request. 
    // Note: ContentBuilder and CancellationToken are optional.
    var response = await httpclient.SendAsync(url, HttpMethod.Get);

    // Read and return the response body
    var body = await response.Content.ReadAsStringAsync();
    
    return Results.Ok(body);
})
.WithName("First Sample")
.WithTags("HttpHelper Operations");

```
---
## 🔍 Observability: The `AddRequestAction` Method<a id='using-addaction'></a>[🔝](#table-of-contents)

The core of HttpHelper's observability is the **`AddRequestAction`** method. This allows you to inject custom logic—such as advanced logging or telemetry—directly into the HTTP execution pipeline without modifying the service logic.

### Understanding the Lifecycle Callback

When you register an action, you provide a `Func<HttpRequestMessage, HttpResponseMessage, int, TimeSpan, Task>`. Here is what each parameter provides at runtime:

* **`HttpRequestMessage` (req)**: The complete outgoing request, including URI, headers, and method.
* **`HttpResponseMessage` (res)**: The final response received from the server.
* **`int` (NrRetry)**: The total number of retries performed by the Polly policy before reaching the final result.
* **`TimeSpan` (timebackOff)**: The cumulative time spent waiting during Rate Limiting or backoff periods.

### Integration with LoggerHelper

This method is specifically designed to bridge the gap with **CSharpEssentials.LoggerHelper**. By implementing the `IRequest` interface, you can trace every detail of the HTTP lifecycle.

#### 1. Define your Log Record

```csharp
// Implementation of IRequest for LoggerHelper integration
private record logRequest(
    string Action = "test", 
    string IdTransaction = "test", 
    string ApplicationName = "HttpHelper Test"
) : IRequest;

```

#### 2. Implement and Attach the Trace Action

The following example shows how to resolve a client and attach a tracing action that logs every retry and status code.

```csharp
app.MapGet("/addAction", async (string url, IhttpsClientHelperFactory httpFactory) => {
    var httpclient = httpFactory.CreateOrGet("Test");

    // Define the tracing callback
    Func<HttpRequestMessage, HttpResponseMessage, int, TimeSpan, Task> TraceAction = async (req, res, NrRetry, timebackOff) => {
        await loggerExtension<logRequest>.TraceAsync(
            new logRequest(), 
            Serilog.Events.LogEventLevel.Information, 
            null, 
            "Sending at retry: {retry} | Request: {url} | Status: {status} | Wait: {wait}ms", 
            NrRetry, req.RequestUri, res.StatusCode, timebackOff.TotalMilliseconds);
    };

    // Attach the action to the client
    httpclient.AddRequestAction(TraceAction);

    var response = await httpclient.SendAsync(url, HttpMethod.Get);
    return Results.Ok(await response.Content.ReadAsStringAsync());
})
.WithTags("HttpHelper Operations");

```

---
## 🛡️ Resilience: Advanced Retries with Polly<a id='using-retries'></a>[🔝](#table-of-contents)

The library integrates **Polly** to handle transient failures through an exponential backoff strategy. This implementation is designed for high observability, capturing the exact **wait time** between attempts.

### Understanding `addRetryCondition` Parameters

The method signature allows you to define exactly how the client should behave when a request fails:

* **`RetryCondition`**: A function that evaluates the `HttpResponseMessage` to determine if a retry is needed (e.g., checking for specific status codes like 503).
* **`retryCount`**: The total number of attempts to make after the initial failure.
* **`backoffFactor`**: The base value for exponential growth. A factor of `2.0` means the wait time will double at each attempt (2s, 4s, 8s...).

### Basic Example (Logging the Backoff)

In this example, we monitor the retry cycle and print the wait time directly to the console:

```csharp
app.MapGet("/test-retry", async (string url, IhttpsClientHelperFactory httpFactory) => {
    var client = httpFactory.CreateOrGet("Test");

    // Configure: Retry 3 times on 'Service Unavailable' with 2.0 backoff factor
    client.addRetryCondition(
        res => res.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable,
        retryCount: 3,
        backoffFactor: 2.0
    );

    // Attach an action to see what's happening
    client.AddRequestAction(async (req, res, NrRetry, timebackOff) => {
        // timebackOff captures the real wait time between attempts
        Console.WriteLine($"[RETRY EVENT] Attempt: {NrRetry} | Status: {res.StatusCode} | Wait: {timebackOff.TotalMilliseconds}ms");
        await Task.CompletedTask;
    });

    var response = await client.SendAsync(url, HttpMethod.Get);
    return Results.Ok(new { FinalStatus = response.StatusCode.ToString() });
});

```

### Visualizing the Result

When the system encounters a 503 error, you will see the exponential progression in your logs:

* **Attempt 1**: Wait ~2000ms
* **Attempt 2**: Wait ~4000ms
* **Attempt 3**: Wait ~8000ms

---

## 🎭 Unit Testing: The HTTP Mocking Engine<a id='using-mock'></a>[🔝](#table-of-contents)

The library includes a native **Mocking Engine** powered by `Moq`. This allows you to intercept outgoing HTTP requests and return predefined response sequences, which is ideal for testing **Polly Retries** or edge cases without a real internet connection.

### 1. Define a Mock Scenario

To create a mock, implement the `IHttpMockScenario` interface. You must define a `Match` condition (to identify the request) and a `ResponseFactory` (the sequence of responses to return).

```csharp
public class MockRetryScenario : IHttpMockScenario {
    // Intercepts only requests to this specific URL
    public Func<HttpRequestMessage, bool> Match => req => 
        req.RequestUri.AbsoluteUri.Contains("api/retry-test");

    public IReadOnlyList<Func<Task<HttpResponseMessage>>> ResponseFactory => 
        new List<Func<Task<HttpResponseMessage>>> {
            // First call: 503 error
            () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)),
            // Second call: 200 OK success
            () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent("{\"status\":\"mocked_success\"}")
            })
        };
}

```

### 2. Usage in Minimal API

The client factory automatically uses the registered mocks via the `HttpMockDelegatingHandler`. The application logic remains identical to production code:

```csharp
app.MapGet("/test-mock-retry", async (IhttpsClientHelperFactory httpFactory) => {
    var client = httpFactory.CreateOrGet("Test");

    client.addRetryCondition(
        res => res.StatusCode == HttpStatusCode.ServiceUnavailable,
        retryCount: 3,
        backoffFactor: 2.0
    );

    // This call will be intercepted by MockRetryScenario
    var response = await client.SendAsync("https://real-api.com/api/retry-test", HttpMethod.Get);
    return Results.Ok(await response.Content.ReadAsStringAsync());
});

```

### 💡 Pro Tip: Auto-Discovery with Scrutor

Instead of registering every mock scenario manually in `Program.cs`, you can use **Scrutor** to automatically scan your assemblies.

In your `InjectMock` configuration, you can use the following pattern to load all scenarios at once, excluding base DTOs:

```csharp
services.Scan(scan => scan
    .FromApplicationDependencies()
    .AddClasses(classes => classes
        .AssignableTo<IHttpMockScenario>()
        .Where(type => type != typeof(HttpMockScenario)) // Exclude the base DTO
    )
    .AsImplementedInterfaces()
    .WithTransientLifetime());

```

---

## 📚 External Documentation

For detailed information on how to configure the logging engine used in the examples above, please refer to the:
👉 **[LoggerHelper Documentation](https://github.com/alexbypa/Csharp.Essentials.Extensions/blob/main/README.md#introduction)**