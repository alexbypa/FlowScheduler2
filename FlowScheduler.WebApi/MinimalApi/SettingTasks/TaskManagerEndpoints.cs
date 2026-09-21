using FlowScheduler.Core.Dtos;
using FlowScheduler.Core.Interfaces.Jobs;
using System.Runtime.CompilerServices;

namespace FlowScheduler.WebApi.MinimalApi.SettingTasks {
    public static class TaskManagerEndpoints {
        public static void MapTaskEndpoints(this IEndpointRouteBuilder app) {
            var group = app.MapGroup("/Tasks").WithTags("Task Hangfire Library");

            group.MapPost("/task", async (CreateTaskRequest task, ITaskSchedulerService monitorService) => {
                var retryIntervals = new TimeSpan[] {
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(20),
                TimeSpan.FromMinutes(30)
            };
                await monitorService.CreateTaskAsync(task, retryIntervals);
                return Results.Created($"/tasks/{task.Name}", task);
            })
            .WithName("CreateTask")
            .WithDescription("""
            {
            	"HangFireJobName": "Check betfair Prod",
            	"GitHubOptionName": "Betfair",
            	"ConnectionString": "Data Source=<host>;Initial Catalog=<db>;Persist Security Info=True;User ID=<user>;Password=<password>;Encrypt=False;TrustServerCertificate=True;",
            	"DatabaseType": "SqlServer",
            	"Name": "betfair.usp_checkbetfairworker",
            	"CommandType": "DatabaseExecuteCommand",
            	"ParametersTask": {},
            	"ParseInJsonResultsTask": "",
            	"ErrorCondition": "HasErrors == true",
            	"SqlTableToAnalizeWithLLM": "",
            	"CronExpression": "*/59 * * * *"
            }
            """);
        }
    }
}
