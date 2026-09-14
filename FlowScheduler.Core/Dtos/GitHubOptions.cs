namespace FlowScheduler.Core.Dtos;
public class GitHubOptions {
    public List<GitHubOption>? Values { get; set; }
}
public class GitHubOption {
    public string? Name { get; set; }
    public string? Token { get; set; }
    public string? Owner { get; set; }
    public string? Repository { get; set; }
    public string? Branch { get; set; }
    public string? LocalPath { get; set; }
}