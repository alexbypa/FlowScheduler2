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

    /// <summary>
    /// Middleware di "salvavita" per il routing delle Single Page Application (SPA).
    /// Evita che le richieste verso le URL base (/rag-library e /mcp-playground) 
    /// diano errore 404 o entrino in loop infinito, reindirizzandole forzatamente 
    /// al file fisico index.html affinché il motore JavaScript del frontend possa avviarsi correttamente.
    /// </summary>
    public static WebApplication UseCustomDashboardRedirects(this WebApplication app) {
        app.Use(async (context, next) => {
            var p = context.Request.Path.Value ?? "";
            if (string.Equals(p, "/rag-library", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p, "/rag-library/", StringComparison.OrdinalIgnoreCase)) {
                context.Response.Redirect("/rag-library/index.html");
                return;
            }

            if (string.Equals(p, "/mcp-playground", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p, "/mcp-playground/", StringComparison.OrdinalIgnoreCase)) {
                context.Response.Redirect("/mcp-playground/index.html");
                return;
            }

            await next();
        });

        return app;
    }
}