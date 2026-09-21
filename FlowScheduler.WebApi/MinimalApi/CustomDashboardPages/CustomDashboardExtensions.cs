using Hangfire.Dashboard;

namespace FlowScheduler.WebApi.MinimalApi.CustomDashboardPages;

public static class CustomDashboardExtensions {
    public static void RegisterCustomDashboardPages() {
        DashboardRoutes.Routes.MapRazorPage("/rag-library-dash", _ => new RagLibraryDashboardRedirectPage());
        NavigationMenu.Items.Add(page => new MenuItem("Libreria RAG", page.Url.To("/rag-library-dash")) {
            Active = page.RequestPath.StartsWith("/rag-library-dash")
        });

        DashboardRoutes.Routes.MapRazorPage("/mcp-playground-dash", _ => new McpPlaygroundDashboardRedirectPage());
        NavigationMenu.Items.Add(page => new MenuItem("MCP Playground", page.Url.To("/mcp-playground-dash")) {
            Active = page.RequestPath.StartsWith("/mcp-playground-dash")
        });
    }
}
