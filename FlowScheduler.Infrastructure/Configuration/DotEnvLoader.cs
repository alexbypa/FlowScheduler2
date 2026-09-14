namespace FlowScheduler.Infrastructure.Configuration;

/// <summary>
/// Carica variabili da .env nella root del repo (utile con dotnet run, oltre a docker-compose env_file).
/// Condiviso tra WebApi e BackgroundJobs per avere un unico punto di configurazione.
/// </summary>
public static class DotEnvLoader {
    public static string? LoadedPath { get; private set; }

    public static void LoadFromRepositoryRoot() {
        LoadedPath = null;
        var dir = Directory.GetCurrentDirectory();
        for (var i = 0; i < 8; i++) {
            var path = Path.Combine(dir, ".env");
            if (File.Exists(path)) {
                ApplyFile(path);
                LoadedPath = path;
                Console.WriteLine($"[DEBUG AI] .env caricato da: {path}");
                return;
            }

            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }

        Console.WriteLine("[DEBUG AI] Nessun file .env trovato (cercato dalla cwd verso le directory padre).");
    }

    static void ApplyFile(string path) {
        foreach (var raw in File.ReadAllLines(path)) {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"')) {
                value = value[1..^1];
            }

            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
