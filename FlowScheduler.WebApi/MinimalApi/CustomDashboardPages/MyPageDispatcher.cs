using Hangfire.Dashboard;
using System;
using System.Threading.Tasks;
namespace FlowScheduler.WebApi.MinimalApi.CustomDashboardPages;
public class MyPageDispatcher : IDashboardDispatcher {
    private readonly Func<DashboardContext, RazorPage> _pageFactory;

    public MyPageDispatcher(Func<DashboardContext, RazorPage> pageFactory) {
        _pageFactory = pageFactory;
    }
    public async Task Dispatch(DashboardContext context) {
        var page = _pageFactory(context);

        // Specifichiamo i tipi dei parametri per evitare l'ambiguità
        var assignMethod = typeof(RazorPage).GetMethod(
            "Assign",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public,
            null,
            new[] { typeof(DashboardContext) }, // <--- Questo risolve l'ambiguità
            null
        );

        if (assignMethod == null) {
            throw new InvalidOperationException("Non è stato possibile trovare il metodo Assign su RazorPage.");
        }

        assignMethod.Invoke(page, new object[] { context });

        await context.Response.WriteAsync(page.ToString());
    }
}