namespace FlowScheduler.Core.Configuration;
public class RedisCacheOptions {
    public string Host { get; set; }
    public int Port { get; set; }
    public bool IsRedisCacheEnabled { get; set; }
    public bool IsInMemoryCacheEnabled { get; set; }
    public int ConnectRetry { get; set; }
    public int ReconnectRetryPolicy { get; set; }
    public int ConnectTimeout { get; set; }
    public int SyncTimeout { get; set; }
    public bool AbortOnConnectFail { get; set; }
    public string Password { get; set; }
}