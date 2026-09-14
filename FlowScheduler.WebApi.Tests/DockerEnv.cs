using System.Diagnostics;

namespace FlowScheduler.WebApi.Tests;

internal static class DockerEnv {
    public static bool IsDockerRunning() {
        try {
            using var p = Process.Start(new ProcessStartInfo {
                FileName = "docker",
                Arguments = "info",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (p is null) return false;
            p.WaitForExit(8000);
            return p.ExitCode == 0;
        } catch {
            return false;
        }
    }
}
