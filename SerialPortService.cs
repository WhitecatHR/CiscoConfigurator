using System.IO.Ports;
using System.IO;
using System.Text;

namespace CiscoConfigGuiWpf;

public sealed record SerialPortSettings(string PortName, int BaudRate, int DataBits, Parity Parity, StopBits StopBits, int LineDelayMs);

public static class SerialPortService
{
    public const string TestPortName = "TEST-COM";

    public static string[] GetPorts() => SerialPort.GetPortNames().OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();

    public static async Task<string> SendCiscoConfigTestAsync(string config, SerialPortSettings settings, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(config))
            throw new InvalidOperationException("Die Konfiguration ist leer.");

        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CiscoKonfigurator");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "serial_test_send.log");

        var normalized = config.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');

        await using var writer = new StreamWriter(path, append: false, Encoding.UTF8);
        await writer.WriteLineAsync("Cisco Konfigurator - COM-Testmodus");
        await writer.WriteLineAsync($"Zeit: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        await writer.WriteLineAsync($"Port: {TestPortName}");
        await writer.WriteLineAsync($"Baudrate: {settings.BaudRate}");
        await writer.WriteLineAsync($"Zeilenverzögerung: {settings.LineDelayMs} ms");
        await writer.WriteLineAsync(new string('-', 72));

        for (var i = 0; i < lines.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync($"{i + 1:0000}: {lines[i]}");
            await writer.FlushAsync();

            if (settings.LineDelayMs > 0)
                await Task.Delay(settings.LineDelayMs, cancellationToken);
        }

        return path;
    }

    public static async Task SendCiscoConfigAsync(string config, SerialPortSettings settings, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.PortName))
            throw new InvalidOperationException("Kein COM-Port ausgewählt.");
        if (string.IsNullOrWhiteSpace(config))
            throw new InvalidOperationException("Die Konfiguration ist leer.");

        await Task.Run(async () =>
        {
            using var port = new SerialPort(settings.PortName, settings.BaudRate, settings.Parity, settings.DataBits, settings.StopBits)
            {
                Encoding = Encoding.ASCII,
                NewLine = "\r",
                Handshake = Handshake.None,
                ReadTimeout = 1000,
                WriteTimeout = 5000,
                DtrEnable = true,
                RtsEnable = true
            };

            port.Open();
            await Task.Delay(250, cancellationToken);

            var normalized = config.Replace("\r\n", "\n").Replace('\r', '\n');
            foreach (var line in normalized.Split('\n'))
            {
                cancellationToken.ThrowIfCancellationRequested();
                port.Write(line + "\r");
                if (settings.LineDelayMs > 0)
                    await Task.Delay(settings.LineDelayMs, cancellationToken);
            }
        }, cancellationToken);
    }
}
