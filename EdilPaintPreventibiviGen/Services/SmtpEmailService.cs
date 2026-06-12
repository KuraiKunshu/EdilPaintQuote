using System.IO;
using System.Net.Mail;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;

namespace EdilPaintPreventibiviGen.Services;

public sealed class SmtpEmailRequest
{
    public string Recipient { get; set; } = string.Empty;
    public string CcRecipients { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string AttachmentPath { get; set; } = string.Empty;
}

public sealed class SmtpEmailSendResult
{
    public string MessageId { get; init; } = string.Empty;
    public string DebugLogPath { get; init; } = string.Empty;
    public DateTime AcceptedAtUtc { get; init; }
}

public sealed class SmtpEmailService
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(45);
    private readonly MailSettingsModel _settings;

    public SmtpEmailService(MailSettingsModel settings)
    {
        _settings = settings;
    }

    public async Task<SmtpEmailSendResult> SendAsync(
        SmtpEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        var debugLog = new SmtpEmailDebugLog();
        string messageId = CreateMessageId(_settings.EffectiveSenderEmail);

        debugLog.Separator();
        debugLog.Write("Avvio invio email preventivo.");
        debugLog.Write($"Message-ID: {messageId}");

        using var timeoutCts = AppShutdownManager.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(SendTimeout);
        var token = timeoutCts.Token;

        try
        {
            _settings.ValidateForSend();

            if (string.IsNullOrWhiteSpace(request.Recipient))
                throw new InvalidOperationException("Destinatario email non configurato.");
            if (string.IsNullOrWhiteSpace(request.AttachmentPath) || !File.Exists(request.AttachmentPath))
                throw new InvalidOperationException("PDF da allegare non trovato.");

            var from = new MailAddress(_settings.EffectiveSenderEmail, _settings.SenderName);
            var recipients = ParseRecipients(request.Recipient);
            if (recipients.Count == 0)
                throw new InvalidOperationException("Destinatario email non valido.");
            var ccRecipients = ParseRecipients(request.CcRecipients)
                .Where(cc => recipients.All(to => !string.Equals(to.Address, cc.Address, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            var smtpRecipients = recipients.Concat(ccRecipients).ToList();

            var attachmentInfo = new FileInfo(request.AttachmentPath);
            debugLog.Write($"SMTP: server={_settings.SmtpServer}, port={_settings.Port}, ssl={_settings.UseSsl}.");
            debugLog.Write($"Mittente: {from.Address}");
            debugLog.Write($"Destinatari: {string.Join(", ", recipients.Select(r => r.Address))}");
            if (ccRecipients.Count > 0)
                debugLog.Write($"CC: {string.Join(", ", ccRecipients.Select(r => r.Address))}");
            debugLog.Write($"Oggetto: {request.Subject}");
            debugLog.Write($"Allegato: {attachmentInfo.FullName} ({FormatBytes(attachmentInfo.Length)})");

            string message = await BuildMimeMessageAsync(from, recipients, ccRecipients, request, messageId, token);

            using var client = new TcpClient();
            client.SendTimeout = (int)SendTimeout.TotalMilliseconds;
            client.ReceiveTimeout = (int)SendTimeout.TotalMilliseconds;

            debugLog.Write("Connessione al server SMTP...");
            await client.ConnectAsync(_settings.SmtpServer, _settings.Port, token);
            debugLog.Write("Connessione TCP stabilita.");

            Stream stream = client.GetStream();
            SslStream? sslStream = null;
            if (_settings.UseSsl)
            {
                debugLog.Write("Avvio handshake SSL/TLS.");
                sslStream = new SslStream(stream, leaveInnerStreamOpen: false);
                await sslStream.AuthenticateAsClientAsync(_settings.SmtpServer).WaitAsync(token);
                stream = sslStream;
                debugLog.Write("Handshake SSL/TLS completato.");
            }

            using (sslStream)
            using (stream)
            using (var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, leaveOpen: true))
            using (var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true)
            {
                NewLine = "\r\n",
                AutoFlush = true
            })
            {
                await ExpectResponseAsync(reader, debugLog, token, 220);
                await SendCommandAsync(reader, writer, debugLog, $"EHLO {Environment.MachineName}", token, 250);
                await SendCommandAsync(reader, writer, debugLog, "AUTH LOGIN", token, 334);
                await SendCommandAsync(reader, writer, debugLog, ToBase64(_settings.Username), token, 334, "AUTH username <redacted>");
                await SendCommandAsync(reader, writer, debugLog, ToBase64(_settings.Password), token, 235, "AUTH password <redacted>");
                await SendCommandAsync(reader, writer, debugLog, $"MAIL FROM:<{from.Address}>", token, 250);

                foreach (var recipient in smtpRecipients)
                    await SendCommandAsync(reader, writer, debugLog, $"RCPT TO:<{recipient.Address}>", token, [250, 251]);

                await SendCommandAsync(reader, writer, debugLog, "DATA", token, 354);
                debugLog.Write($"C: [contenuto MIME omesso dal log, {message.Length:N0} caratteri]");
                await WriteDataAsync(writer, message, token);
                var finalResponse = await ExpectResponseAsync(reader, debugLog, token, 250);

                debugLog.Write($"Messaggio accettato dal server SMTP. Risposta finale: {finalResponse.Code}.");

                try
                {
                    await SendCommandAsync(reader, writer, debugLog, "QUIT", token, 221);
                }
                catch (Exception ex)
                {
                    debugLog.Write($"QUIT non confermato: {ex.Message}. Il messaggio era gia' stato accettato dopo DATA.");
                }
            }

            debugLog.Write("Invio SMTP completato.");

            return new SmtpEmailSendResult
            {
                MessageId = messageId,
                DebugLogPath = debugLog.FilePath,
                AcceptedAtUtc = DateTime.UtcNow
            };
        }
        catch (OperationCanceledException)
        {
            debugLog.Write($"Invio annullato o timeout dopo {SendTimeout.TotalSeconds:F0}s.");
            throw;
        }
        catch (Exception ex)
        {
            debugLog.Write($"ERRORE invio SMTP: {ex.GetType().Name}: {ex.Message}");
            throw new InvalidOperationException($"{ex.Message}\n\nLog SMTP: {debugLog.FilePath}", ex);
        }
    }

    private static List<MailAddress> ParseRecipients(string recipients)
    {
        return EmailAddressParser.ToMailAddresses(recipients);
    }

    private static async Task<string> BuildMimeMessageAsync(
        MailAddress from,
        IReadOnlyList<MailAddress> recipients,
        IReadOnlyList<MailAddress> ccRecipients,
        SmtpEmailRequest request,
        string messageId,
        CancellationToken cancellationToken)
    {
        byte[] attachmentBytes = await File.ReadAllBytesAsync(request.AttachmentPath, cancellationToken);
        string boundary = "----=_EdilPaint_" + Guid.NewGuid().ToString("N");
        string fileName = Path.GetFileName(request.AttachmentPath);

        var sb = new StringBuilder();
        sb.AppendLine($"From: {FormatMailbox(from)}");
        sb.AppendLine($"Reply-To: {FormatMailbox(from)}");
        sb.AppendLine($"To: {string.Join(", ", recipients.Select(FormatMailbox))}");
        if (ccRecipients.Count > 0)
            sb.AppendLine($"Cc: {string.Join(", ", ccRecipients.Select(FormatMailbox))}");
        sb.AppendLine($"Subject: {EncodeHeader(request.Subject)}");
        sb.AppendLine($"Date: {DateTimeOffset.Now:R}");
        sb.AppendLine($"Message-ID: {messageId}");
        sb.AppendLine("X-Mailer: EdilPaint Preventivi");
        sb.AppendLine("MIME-Version: 1.0");
        sb.AppendLine($"Content-Type: multipart/mixed; boundary=\"{boundary}\"");
        sb.AppendLine();

        sb.AppendLine($"--{boundary}");
        sb.AppendLine("Content-Type: text/plain; charset=utf-8");
        sb.AppendLine("Content-Transfer-Encoding: quoted-printable");
        sb.AppendLine("Content-Language: it");
        sb.AppendLine();
        AppendQuotedPrintableText(sb, request.Body ?? string.Empty);
        sb.AppendLine();

        sb.AppendLine($"--{boundary}");
        sb.AppendLine($"Content-Type: application/pdf; name=\"{EncodeQuoted(fileName)}\"");
        sb.AppendLine("Content-Transfer-Encoding: base64");
        sb.AppendLine($"Content-Disposition: attachment; filename=\"{EncodeQuoted(fileName)}\"");
        sb.AppendLine();
        AppendBase64Lines(sb, attachmentBytes);
        sb.AppendLine();
        sb.AppendLine($"--{boundary}--");

        return sb.ToString();
    }

    private static async Task SendCommandAsync(
        StreamReader reader,
        StreamWriter writer,
        SmtpEmailDebugLog debugLog,
        string command,
        CancellationToken cancellationToken,
        int acceptedCode,
        string? safeCommandForLog = null)
    {
        await SendCommandAsync(reader, writer, debugLog, command, cancellationToken, [acceptedCode], safeCommandForLog);
    }

    private static async Task SendCommandAsync(
        StreamReader reader,
        StreamWriter writer,
        SmtpEmailDebugLog debugLog,
        string command,
        CancellationToken cancellationToken,
        int[] acceptedCodes,
        string? safeCommandForLog = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        debugLog.Write($"C: {safeCommandForLog ?? command}");
        await writer.WriteLineAsync(command).WaitAsync(cancellationToken);
        await writer.FlushAsync().WaitAsync(cancellationToken);
        await ExpectResponseAsync(reader, debugLog, cancellationToken, acceptedCodes);
    }

    private static async Task WriteDataAsync(
        StreamWriter writer,
        string message,
        CancellationToken cancellationToken)
    {
        string normalized = message.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (string rawLine in normalized.Split('\n'))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string line = rawLine.StartsWith(".", StringComparison.Ordinal) ? "." + rawLine : rawLine;
            await writer.WriteLineAsync(line).WaitAsync(cancellationToken);
        }

        await writer.WriteLineAsync(".").WaitAsync(cancellationToken);
        await writer.FlushAsync().WaitAsync(cancellationToken);
    }

    private static async Task<(int Code, string Message)> ExpectResponseAsync(
        StreamReader reader,
        SmtpEmailDebugLog debugLog,
        CancellationToken cancellationToken,
        params int[] acceptedCodes)
    {
        var response = await ReadResponseAsync(reader, cancellationToken);
        foreach (string line in response.Message.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            debugLog.Write($"S: {line}");

        if (!acceptedCodes.Contains(response.Code))
        {
            throw new InvalidOperationException(
                $"Errore SMTP {response.Code}: {response.Message}");
        }

        return response;
    }

    private static async Task<(int Code, string Message)> ReadResponseAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        var lines = new List<string>();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (line == null)
                throw new InvalidOperationException("Il server SMTP ha chiuso la connessione.");

            lines.Add(line);
            if (line.Length < 4 || line[3] == ' ')
                break;
        }

        string first = lines.FirstOrDefault() ?? string.Empty;
        int code = first.Length >= 3 && int.TryParse(first[..3], out int parsedCode)
            ? parsedCode
            : 0;

        return (code, string.Join("\n", lines));
    }

    private static void AppendBase64Lines(StringBuilder sb, byte[] bytes)
    {
        string base64 = Convert.ToBase64String(bytes);
        for (int i = 0; i < base64.Length; i += 76)
            sb.AppendLine(base64.Substring(i, Math.Min(76, base64.Length - i)));
    }

    private static void AppendQuotedPrintableText(StringBuilder sb, string value)
    {
        string normalized = value.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (string rawLine in normalized.Split('\n'))
            AppendQuotedPrintableLine(sb, rawLine);
    }

    private static void AppendQuotedPrintableLine(StringBuilder sb, string line)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(line);
        var current = new StringBuilder();

        for (int i = 0; i < bytes.Length; i++)
        {
            string token = EncodeQuotedPrintableByte(bytes[i], i == bytes.Length - 1);
            if (current.Length + token.Length > 73)
            {
                sb.Append(current);
                sb.AppendLine("=");
                current.Clear();
            }

            current.Append(token);
        }

        sb.AppendLine(current.ToString());
    }

    private static string EncodeQuotedPrintableByte(byte value, bool isLastByte)
    {
        if (value is 9 or 32)
            return isLastByte ? $"={value:X2}" : ((char)value).ToString();

        if ((value >= 33 && value <= 60) || (value >= 62 && value <= 126))
            return ((char)value).ToString();

        return $"={value:X2}";
    }

    private static string FormatMailbox(MailAddress address)
    {
        if (string.IsNullOrWhiteSpace(address.DisplayName))
            return $"<{address.Address}>";

        return $"{EncodeHeader(address.DisplayName)} <{address.Address}>";
    }

    private static string EncodeHeader(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.All(c => c is >= ' ' and <= '~' && c != '=' && c != '?')
            ? value
            : "=?utf-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(value)) + "?=";
    }

    private static string EncodeQuoted(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string ToBase64(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static string CreateMessageId(string senderEmail)
    {
        string domain = "localhost";
        try
        {
            domain = new MailAddress(senderEmail).Host;
        }
        catch
        {
        }

        return $"<{Guid.NewGuid():N}@{domain}>";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";

        double kb = bytes / 1024d;
        if (kb < 1024)
            return $"{kb:N1} KB";

        return $"{kb / 1024d:N1} MB";
    }
}
