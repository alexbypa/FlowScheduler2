using Hangfire.Dashboard;
using Hangfire.Dashboard.Pages;
using System;

namespace FlowScheduler.WebApi.MinimalApi.CustomDashboardPages;

public class CustomDashboardPage : RazorPage {
    public override void Execute() {
        // Definisce il layout della dashboard (sidebar, header, ecc.)
        Layout = new LayoutPage("Gestione Task");

        WriteLiteral("<h2>La mia Pagina Personalizzata</h2>");
        WriteLiteral("<div class='row'>");
        WriteLiteral("<div class='col-md-12'>");
        WriteLiteral("<div class='panel panel-default'>");
        WriteLiteral("<div class='panel-body'>");
        WriteLiteral("<p>Questa è una pagina semplice per testare l'estensione della dashboard di Hangfire.</p>");
        WriteLiteral("<button class='btn btn-danger'>Esempio Pulsante Disabilita</button>");
        WriteLiteral("</div>");
        WriteLiteral("</div>");
        WriteLiteral("</div>");
        WriteLiteral("</div>");
    }
}