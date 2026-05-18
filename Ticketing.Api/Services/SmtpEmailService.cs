using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Ticketing.Api.Services;

public interface IEmailService
{
    Task<bool> SendHtmlAsync(string toAddress, string subject, string htmlBody, CancellationToken ct = default);
}

public class SmtpOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? User { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "tickets@example.com";
    public string FromName { get; set; } = "Ticketing";
    public bool UseStartTls { get; set; } = true;
}

public class SmtpEmailService : IEmailService
{
    private readonly SmtpOptions _opts;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IConfiguration config, ILogger<SmtpEmailService> logger)
    {
        _opts = config.GetSection("Smtp").Get<SmtpOptions>() ?? new SmtpOptions();
        _logger = logger;
    }

    public async Task<bool> SendHtmlAsync(string toAddress, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opts.Host))
        {
            _logger.LogWarning("Smtp:Host not configured — cannot send digest to {To}", toAddress);
            return false;
        }

        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(_opts.FromName, _opts.FromAddress));
        msg.To.Add(MailboxAddress.Parse(toAddress));
        msg.Subject = subject;
        msg.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        try
        {
            using var client = new SmtpClient();
            var secureOpt = _opts.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
            await client.ConnectAsync(_opts.Host, _opts.Port, secureOpt, ct);
            if (!string.IsNullOrEmpty(_opts.User))
                await client.AuthenticateAsync(_opts.User, _opts.Password ?? string.Empty, ct);
            await client.SendAsync(msg, ct);
            await client.DisconnectAsync(true, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMTP send failed (host={Host} port={Port})", _opts.Host, _opts.Port);
            return false;
        }
    }
}
