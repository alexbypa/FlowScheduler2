namespace FlowScheduler.WebApi.MinimalApi.health;

public static class HealtEndpoints {
    public static void MaphealthEndpoints(this IEndpointRouteBuilder app) {
        var group = app.MapGroup("/Healts").WithTags("healts Library");

        group.MapGet("/health", () => {
            var assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var buildTime = System.IO.File.GetLastWriteTime(assemblyLocation);

            return Results.Ok(new {
                status = "healthy",
                buildTime = buildTime.ToString("yyyy-MM-dd HH:mm:ss")
            });
        });

    }
}

