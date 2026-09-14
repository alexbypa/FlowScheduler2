using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace FlowScheduler.WebApi.Tests;

[Collection("redis")]
public class RagLibraryEndpointsTests(RedisWebAppFixture fixture) {
    static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [SkippableFact]
    public async Task Get_documents_returns_ok_and_json_shape() {
        Skip.If(fixture.Client is null, "Docker non disponibile: eseguire i test con Docker avviato.");

        var res = await fixture.Client.GetAsync("/rag/library/documents?page=0&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("items", out _));
        Assert.True(doc.RootElement.TryGetProperty("totalCount", out _));
    }

    [SkippableFact]
    public async Task Post_get_roundtrip_library_document() {
        Skip.If(fixture.Client is null, "Docker non disponibile: eseguire i test con Docker avviato.");

        var create = new {
            title = "Test Doc API",
            markdown = "## Hello\n\nWorld.",
            category = "test-api",
            subCategory = "smoke",
            documentType = "tutorial",
        };

        var post = await fixture.Client.PostAsJsonAsync("/rag/library/documents", create);
        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        var created = await post.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();
        Assert.False(string.IsNullOrEmpty(id));

        var get = await fixture.Client.GetAsync($"/rag/library/documents/{id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var entity = await get.Content.ReadFromJsonAsync<RagLibDocDto>(JsonOpts);
        Assert.NotNull(entity);
        Assert.Equal("Test Doc API", entity!.Title);
        Assert.Equal("test-api", entity.Category);
        Assert.Equal("smoke", entity.SubCategory);
        Assert.Equal("tutorial", entity.DocumentType);
        Assert.Equal("library", entity.Context);
        Assert.Contains("Hello", entity.Markdown, StringComparison.Ordinal);

        var del = await fixture.Client.DeleteAsync($"/rag/documents/{id}");
        Assert.True(del.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound);
    }

    [SkippableFact]
    public async Task Post_put_ops_roundtrip_via_library_ui_api() {
        Skip.If(fixture.Client is null, "Docker non disponibile: eseguire i test con Docker avviato.");

        var create = new {
            content = "Task: TestJob\nTemplates: timeout",
            resolution = "Aumentare il timeout del job.",
            source = "test-ui-ops",
            category = "test-ops-api",
            title = "Timeout job",
        };

        var post = await fixture.Client.PostAsJsonAsync("/rag/library/documents/ops", create);
        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        var created = await post.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();
        Assert.False(string.IsNullOrEmpty(id));

        var update = new {
            content = "Task: TestJob\nTemplates: timeout | retry",
            resolution = "Aumentare timeout e abilitare retry.",
            source = "test-ui-ops",
            category = "test-ops-api",
            title = "Timeout job v2",
        };

        var put = await fixture.Client.PutAsJsonAsync($"/rag/library/documents/{id}/ops", update);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var get = await fixture.Client.GetAsync($"/rag/library/documents/{id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var entity = await get.Content.ReadFromJsonAsync<RagLibOpsDto>(JsonOpts);
        Assert.NotNull(entity);
        Assert.Equal("ops", entity!.Context);
        Assert.Contains("retry", entity.Content, StringComparison.Ordinal);
        Assert.Contains("retry", entity.Resolution, StringComparison.Ordinal);
        Assert.Equal("Timeout job v2", entity.Title);

        var del = await fixture.Client.DeleteAsync($"/rag/documents/{id}");
        Assert.True(del.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound);
    }

    sealed class RagLibDocDto {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Category { get; set; } = "";
        public string SubCategory { get; set; } = "";
        public string DocumentType { get; set; } = "";
        public string Context { get; set; } = "";
        public string Markdown { get; set; } = "";
    }

    sealed class RagLibOpsDto {
        public string Context { get; set; } = "";
        public string Content { get; set; } = "";
        public string Resolution { get; set; } = "";
        public string Title { get; set; } = "";
    }
}
