using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace CiscoConfigGuiWpf;

public static class SshDeviceService
{
    public static async Task<SshOperationResult> TestTcpAsync(SshConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.ConnectionTimeoutSeconds, 1, 600)));
            await client.ConnectAsync(settings.Host, settings.Port, timeout.Token);
            return new(true, $"TCP-Verbindung zu {settings.Host}:{settings.Port} erfolgreich.", "", 0);
        }
        catch (Exception ex)
        {
            return new(false, "", ex.Message, -1);
        }
    }

    public static Task<SshOperationResult> SendConfigurationAsync(
        SshConnectionSettings settings,
        string configuration,
        CancellationToken cancellationToken = default)
    {
        var commands = new List<string> { "terminal length 0", "configure terminal" };
        commands.AddRange(FilterTransferLines(configuration));
        commands.Add("end");
        if (settings.SaveAfterTransfer) commands.Add("write memory");
        commands.Add("exit");
        return RunInteractiveAsync(settings, commands, cancellationToken);
    }

    public static Task<SshOperationResult> ReadConfigurationAsync(
        SshConnectionSettings settings,
        string backupType,
        CancellationToken cancellationToken = default)
    {
        var showCommand = backupType.Equals("Startup-Config", StringComparison.OrdinalIgnoreCase)
            ? "show startup-config"
            : "show running-config";
        return RunInteractiveAsync(settings, new[] { "terminal length 0", showCommand, "exit" }, cancellationToken);
    }

    private static async Task<SshOperationResult> RunInteractiveAsync(
        SshConnectionSettings settings,
        IEnumerable<string> commands,
        CancellationToken cancellationToken)
    {
        var usePlink = settings.AuthenticationMode.Equals("Plink + Passwort", StringComparison.OrdinalIgnoreCase);
        var executable = usePlink ? "plink.exe" : "ssh.exe";
        var start = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (usePlink)
        {
            start.ArgumentList.Add("-ssh");
            start.ArgumentList.Add("-batch");
            start.ArgumentList.Add("-P");
            start.ArgumentList.Add(settings.Port.ToString());
            start.ArgumentList.Add("-l");
            start.ArgumentList.Add(settings.Username);
            if (!string.IsNullOrWhiteSpace(settings.Password))
            {
                start.ArgumentList.Add("-pw");
                start.ArgumentList.Add(settings.Password);
            }
            start.ArgumentList.Add(settings.Host);
        }
        else
        {
            start.ArgumentList.Add("-tt");
            start.ArgumentList.Add("-o");
            start.ArgumentList.Add("BatchMode=yes");
            start.ArgumentList.Add("-o");
            start.ArgumentList.Add("StrictHostKeyChecking=accept-new");
            start.ArgumentList.Add("-p");
            start.ArgumentList.Add(settings.Port.ToString());
            if (!string.IsNullOrWhiteSpace(settings.PrivateKeyPath))
            {
                start.ArgumentList.Add("-i");
                start.ArgumentList.Add(settings.PrivateKeyPath);
            }
            start.ArgumentList.Add($"{settings.Username}@{settings.Host}");
        }

        try
        {
            using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            foreach (var command in commands)
            {
                await process.StandardInput.WriteLineAsync(command.AsMemory(), cancellationToken);
                await process.StandardInput.FlushAsync(cancellationToken);
                if (settings.LineDelayMs > 0)
                    await Task.Delay(settings.LineDelayMs, cancellationToken);
            }
            process.StandardInput.Close();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.CommandTimeoutSeconds, 10, 3600)));
            await process.WaitForExitAsync(timeout.Token);
            var output = await outputTask;
            var error = await errorTask;
            return new(process.ExitCode == 0, output, error, process.ExitCode);
        }
        catch (OperationCanceledException)
        {
            return new(false, "", "SSH-Vorgang wurde abgebrochen oder hat das Zeitlimit überschritten.", -1);
        }
        catch (Exception ex)
        {
            return new(false, "", $"{ex.Message}\n\nBenötigtes Programm: {executable}. OpenSSH arbeitet schlüsselbasiert; für Passwortauthentifizierung muss plink.exe im PATH liegen.", -1);
        }
    }

    private static IEnumerable<string> FilterTransferLines(string configuration)
    {
        foreach (var raw in (configuration ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = raw.TrimEnd();
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("!")) continue;
            if (trimmed.Equals("enable", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("configure terminal", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("conf t", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("end", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("write memory", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("copy running-config startup-config", StringComparison.OrdinalIgnoreCase))
                continue;
            yield return trimmed;
        }
    }
}
