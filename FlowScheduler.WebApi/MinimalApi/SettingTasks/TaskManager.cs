using FlowScheduler.Core.Dtos;
using FlowScheduler.Core.Entities;
using FlowScheduler.Core.Interfaces.Jobs;

namespace FlowScheduler.WebApi.MinimalApi.SettingTasks;
public class TaskManager : IEndpointDefinition {

    public void DefineEndpoints(WebApplication app) {

        app.MapGet("/tasks", async (ITaskSchedulerService monitorService) => {
            var tasks = await monitorService.GetAllTasksAsync();
            return Results.Ok(tasks);
        })
        .WithName("GetAllTasks")
        .Produces<IEnumerable<MonitorTask>>(StatusCodes.Status200OK);

        app.MapPost("/task", async (CreateTaskRequest task, ITaskSchedulerService monitorService) => {
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