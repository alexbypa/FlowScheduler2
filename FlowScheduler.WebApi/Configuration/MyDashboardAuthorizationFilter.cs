using Hangfire.Dashboard;

namespace FlowScheduler.WebApi.Configuration {
    public class MyDashboardAuthorizationFilter : IDashboardAuthorizationFilter {
        /// <summary>
        /// Verifica se l'utente ha i permessi per accedere alla dashboard di Hangfire.
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public bool Authorize(DashboardContext context) {
            // In sviluppo, permettiamo l'accesso a tutti
            // In produzione (AZ-305), qui controlleresti i cookie o i ruoli JWT
            return true;
        }
    }
}
