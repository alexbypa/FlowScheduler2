using Hangfire.Dashboard;
using System.Reflection;

namespace FlowScheduler.WebApi.MinimalApi.CustomDashboardPages;

/// <summary>
/// Estensione per il routing di pagine Razor personalizzate all'interno del dashboard di Hangfire.
/// </summary>
public static class HangfireDashboardExtensions {
    public static void MapRazorPage(this Hangfire.Dashboard.RouteCollection routes, string pathTemplate, Func<DashboardContext, RazorPage> pageFactory) {
        routes.Add(pathTemplate, new LambdaDispatcher(pageFactory));
    }

    private class LambdaDispatcher : IDashboardDispatcher {
        private readonly Func<DashboardContext, RazorPage> _pageFactory;
        public LambdaDispatcher(Func<DashboardContext, RazorPage> pageFactory) => _pageFactory = pageFactory;

        public async Task Dispatch(DashboardContext context) {
            var page = _pageFactory(context);
            var assignMethod = typeof(RazorPage).GetMethod("Assign", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, new[] { typeof(DashboardContext) }, null);
            assignMethod?.Invoke(page, new object[] { context });
            await context.Response.WriteAsync(page.ToString());
        }
    }
}