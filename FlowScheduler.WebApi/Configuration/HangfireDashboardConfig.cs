using Hangfire;
using Hangfire.Dashboard;

namespace FlowScheduler.WebApi.Configuration;

public static class HangfireDashboardConfig {
    public static WebApplication MapAppHangfireDashboard(this WebApplication app) {
        app.MapHangfireDashboard("/dashboard", new DashboardOptions {
            Authorization = new[] { new MyDashboardAuthorizationFilter() }
        });
        return app;
    }
}

