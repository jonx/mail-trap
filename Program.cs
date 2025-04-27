using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        int port = 2525; // Default port

        if (args.Contains("--help") || args.Contains("-h"))
        {
            ShowHelp();
            return;
        }

        // Parse arguments
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--port" || arg == "-p")
            {
                if (i + 1 < args.Length && int.TryParse(args[i + 1], out int parsedPort))
                {
                    port = parsedPort;
                }
                else
                {
                    Console.WriteLine("Error: Missing or invalid port number after --port or -p");
                    return;
                }
            }
        }

        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        Console.WriteLine($"SMTP server listening on port {port}...");

        Directory.CreateDirectory("data");

        while (true)
        {
            var client = await listener.AcceptTcpClientAsync();
            _ = HandleClient(client);
        }
    }

    static void ShowHelp()
    {
        Console.WriteLine("mail-trap - Minimal local SMTP server for development");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run -- [options]");
        Console.WriteLine("  or mail-trap [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -p, --port <number>    Specify the listening port (default: 2525)");
        Console.WriteLine("  -h, --help             Show this help message");
        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  When running apps inside Docker (like n8n), use 'host.docker.internal' as the SMTP host.");
    }

    static async Task HandleClient(TcpClient client)
    {
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };

        await writer.WriteLineAsync("220 localhost Simple SMTP Server");

        string from = null;
        string to = null;
        StringBuilder dataBuilder = new();

        bool dataMode = false;

        while (true)
        {
            string line = await reader.ReadLineAsync();
            if (line == null)
                break;

            if (dataMode)
            {
                if (line == ".")
                {
                    dataMode = false;

                    Console.WriteLine("=== Received email ===");
                    Console.WriteLine($"Sender: {from}");
                    Console.WriteLine($"Recipient: {to}");
                    Console.WriteLine();
                    Console.WriteLine(dataBuilder.ToString());
                    Console.WriteLine("======================");

                    // Extract Subject
                    string subject = null;
                    foreach (var headerLine in dataBuilder.ToString().Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                    {
                        if (headerLine.StartsWith("Subject:", StringComparison.OrdinalIgnoreCase))
                        {
                            subject = headerLine.Substring("Subject:".Length).Trim();
                            break;
                        }
                    }

                    Console.WriteLine($"[Subject: {subject ?? "(no subject)"}]");

                    SaveEmail(dataBuilder.ToString());

                    await writer.WriteLineAsync("250 OK: Message accepted");
                    break;
                }
                else
                {
                    dataBuilder.AppendLine(line);
                }
            }
            else
            {
                Console.WriteLine($"C: {line}");

                if (line.StartsWith("HELO") || line.StartsWith("EHLO"))
                {
                    await writer.WriteLineAsync("250 Hello");
                }
                else if (line.StartsWith("MAIL FROM:"))
                {
                    from = line.Substring(10).Trim();
                    await writer.WriteLineAsync("250 OK");
                }
                else if (line.StartsWith("RCPT TO:"))
                {
                    to = line.Substring(8).Trim();
                    await writer.WriteLineAsync("250 OK");
                }
                else if (line == "DATA")
                {
                    await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                    dataMode = true;
                    dataBuilder.Clear();
                }
                else if (line == "QUIT")
                {
                    await writer.WriteLineAsync("221 Bye");
                    break;
                }
                else
                {
                    await writer.WriteLineAsync("250 OK");
                }
            }
        }

        client.Close();
    }

    static void SaveEmail(string content)
    {
        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff");
        string filename = Path.Combine("data", $"{timestamp}.eml");

        File.WriteAllText(filename, content, Encoding.UTF8);

        Console.WriteLine($"[Saved email to {filename}]");
    }
}
