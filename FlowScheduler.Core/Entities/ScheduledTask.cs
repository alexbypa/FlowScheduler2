using System.ComponentModel.DataAnnotations;

namespace FlowScheduler.Core.Entities;
public class ScheduledTask {
    [Required]
    public string Name { get; set; }
    [Required]
    public string CommandType { get; set; }
    [Required]
    public string[] ParametersTask { get; set; }
    [Required]
    public string ParseInJsonResultsTask { get; set; }
    [Required]
    public string CronExpression { get; set; }
    public ScheduledTask(string name, string commandtype, string[] _ParametersTask, string _ParseInJsonResultsTask, string cronexpression) {
        Name = name;
        CommandType = commandtype;
        ParametersTask = _ParametersTask;
        ParseInJsonResultsTask = _ParseInJsonResultsTask;
        CronExpression = cronexpression;
    }
}