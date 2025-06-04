/*
 * mail-trap - Minimal local SMTP server for development
 * 
 * Copyright (c) 2025 John Knipper <code@jkn.me>
 * GitHub: https://github.com/jonx/mail-trap
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

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

        string? from = null;
        string? to = null;
        StringBuilder dataBuilder = new();

        bool dataMode = false;

        while (true)
        {
            string? line = await reader.ReadLineAsync();
            if (line == null)
                break;

            if (dataMode)
            {
                if (line == ".")
                {
                    dataMode = false;

                    Console.WriteLine("=== Received email ===");
                    Console.WriteLine($"Sender: {from ?? "unknown"}");
                    Console.WriteLine($"Recipient: {to ?? "unknown"}");
                    Console.WriteLine();

                    string rawEmailContent = dataBuilder.ToString();
                    
                    // Extract and display email headers and content
                    var emailInfo = ParseEmail(rawEmailContent);
                    
                    Console.WriteLine($"Subject: {emailInfo.Subject ?? "(no subject)"}");
                    Console.WriteLine($"Content-Type: {emailInfo.ContentType ?? "text/plain"}");
                    Console.WriteLine($"Format: {GetEmailFormat(emailInfo)}");
                    Console.WriteLine();
                    
                    if (!string.IsNullOrEmpty(emailInfo.DecodedContent))
                    {
                        Console.WriteLine("--- Email Content ---");
                        if (emailInfo.IsHtml)
                        {
                            // Display cleaned HTML for console
                            string cleanContent = CleanHtmlForConsole(emailInfo.DecodedContent);
                            Console.WriteLine(cleanContent);
                        }
                        else
                        {
                            Console.WriteLine(emailInfo.DecodedContent);
                        }
                        Console.WriteLine("--------------------");
                    }

                    Console.WriteLine("======================");

                    SaveEmail(rawEmailContent);

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

    static EmailInfo ParseEmail(string rawEmail)
    {
        var emailInfo = new EmailInfo();
        var lines = rawEmail.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        
        bool headerSection = true;
        bool isBase64 = false;
        StringBuilder contentBuilder = new StringBuilder();
        StringBuilder base64Builder = new StringBuilder();

        foreach (var line in lines)
        {
            if (headerSection)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    headerSection = false;
                    continue;
                }

                // Parse headers
                if (line.StartsWith("Subject:", StringComparison.OrdinalIgnoreCase))
                {
                    emailInfo.Subject = line.Substring(8).Trim();
                }
                else if (line.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase))
                {
                    emailInfo.ContentType = line.Substring(13).Trim();
                    emailInfo.IsHtml = emailInfo.ContentType.Contains("text/html", StringComparison.OrdinalIgnoreCase);
                }
                else if (line.StartsWith("Content-Transfer-Encoding:", StringComparison.OrdinalIgnoreCase))
                {
                    string encoding = line.Substring(26).Trim();
                    emailInfo.TransferEncoding = encoding;
                    isBase64 = encoding.Equals("base64", StringComparison.OrdinalIgnoreCase);
                }
                else if (line.StartsWith("MIME-Version:", StringComparison.OrdinalIgnoreCase))
                {
                    emailInfo.MimeVersion = line.Substring(13).Trim();
                }
            }
            else
            {
                // Content section
                if (isBase64)
                {
                    base64Builder.Append(line.Trim());
                }
                else
                {
                    contentBuilder.AppendLine(line);
                }
            }
        }

        // Decode content if needed
        if (isBase64 && base64Builder.Length > 0)
        {
            try
            {
                byte[] data = Convert.FromBase64String(base64Builder.ToString());
                emailInfo.DecodedContent = Encoding.UTF8.GetString(data);
            }
            catch (Exception ex)
            {
                emailInfo.DecodedContent = $"[Failed to decode base64 content: {ex.Message}]";
            }
        }
        else
        {
            emailInfo.DecodedContent = contentBuilder.ToString();
        }

        return emailInfo;
    }

    static string GetEmailFormat(EmailInfo emailInfo)
    {
        var formatParts = new List<string>();

        // Determine content type
        if (emailInfo.IsHtml)
        {
            formatParts.Add("HTML");
        }
        else if (emailInfo.ContentType?.Contains("text/plain", StringComparison.OrdinalIgnoreCase) == true)
        {
            formatParts.Add("Plain Text");
        }
        else if (!string.IsNullOrEmpty(emailInfo.ContentType))
        {
            formatParts.Add(emailInfo.ContentType.Split(';')[0].Trim());
        }
        else
        {
            formatParts.Add("Plain Text");
        }

        // Add encoding information
        if (!string.IsNullOrEmpty(emailInfo.TransferEncoding))
        {
            if (emailInfo.TransferEncoding.Equals("base64", StringComparison.OrdinalIgnoreCase))
            {
                formatParts.Add("Base64 Encoded");
            }
            else if (emailInfo.TransferEncoding.Equals("quoted-printable", StringComparison.OrdinalIgnoreCase))
            {
                formatParts.Add("Quoted-Printable");
            }
            else if (!emailInfo.TransferEncoding.Equals("7bit", StringComparison.OrdinalIgnoreCase) && 
                     !emailInfo.TransferEncoding.Equals("8bit", StringComparison.OrdinalIgnoreCase))
            {
                formatParts.Add($"{emailInfo.TransferEncoding} Encoded");
            }
        }

        // Add MIME information
        if (!string.IsNullOrEmpty(emailInfo.MimeVersion))
        {
            formatParts.Add($"MIME {emailInfo.MimeVersion}");
        }

        // Add charset if available
        if (!string.IsNullOrEmpty(emailInfo.ContentType) && emailInfo.ContentType.Contains("charset="))
        {
            var charsetMatch = Regex.Match(emailInfo.ContentType, @"charset=([^;\s]+)", RegexOptions.IgnoreCase);
            if (charsetMatch.Success)
            {
                formatParts.Add($"Charset: {charsetMatch.Groups[1].Value}");
            }
        }

        return string.Join(" | ", formatParts);
    }

    static string CleanHtmlForConsole(string htmlContent)
    {
        if (string.IsNullOrEmpty(htmlContent))
            return string.Empty;

        // Remove HTML tags but preserve some structure
        string cleaned = htmlContent;

        // Replace common HTML elements with readable equivalents
        cleaned = Regex.Replace(cleaned, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"<hr\s*/?>", "\n" + new string('-', 40) + "\n", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"<p[^>]*>", "\n", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"</p>", "\n", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"<div[^>]*>", "\n", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"</div>", "\n", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"<h[1-6][^>]*>", "\n=== ", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"</h[1-6]>", " ===\n", RegexOptions.IgnoreCase);

        // Remove all remaining HTML tags
        cleaned = Regex.Replace(cleaned, @"<[^>]+>", "", RegexOptions.IgnoreCase);

        // Decode HTML entities
        cleaned = cleaned.Replace("&lt;", "<");
        cleaned = cleaned.Replace("&gt;", ">");
        cleaned = cleaned.Replace("&amp;", "&");
        cleaned = cleaned.Replace("&quot;", "\"");
        cleaned = cleaned.Replace("&#39;", "'");
        cleaned = cleaned.Replace("&nbsp;", " ");

        // Clean up excessive whitespace
        cleaned = Regex.Replace(cleaned, @"\n\s*\n\s*\n", "\n\n"); // Max 2 consecutive newlines
        cleaned = Regex.Replace(cleaned, @"[ \t]+", " "); // Multiple spaces to single space
        cleaned = cleaned.Trim();

        return cleaned;
    }

    static void SaveEmail(string content)
    {
        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff");
        string filename = Path.Combine("data", $"{timestamp}.eml");

        File.WriteAllText(filename, content, Encoding.UTF8);

        Console.WriteLine($"[Saved email to {filename}]");
    }

    class EmailInfo
    {
        public string? Subject { get; set; }
        public string? ContentType { get; set; }
        public string? DecodedContent { get; set; }
        public string? TransferEncoding { get; set; }
        public string? MimeVersion { get; set; }
        public bool IsHtml { get; set; }
    }
}