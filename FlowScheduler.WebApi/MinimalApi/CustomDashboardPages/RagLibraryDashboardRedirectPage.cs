using Hangfire.Dashboard;
using Hangfire.Dashboard.Pages;

namespace FlowScheduler.WebApi.MinimalApi.CustomDashboardPages;

public class RagLibraryDashboardRedirectPage : RazorPage {
    public override void Execute() {
        Layout = new LayoutPage("Libreria RAG");
        WriteLiteral("<div class='row'><div class='col-md-12' style='padding:0 8px;'>");
        WriteLiteral("<iframe title='Libreria RAG' src='/rag-library/index.html' style='width:100%;min-height:calc(100vh - 120px);border:0;display:block;background:#0f1419;'></iframe>");
        WriteLiteral("</div></div>");
    }
}
