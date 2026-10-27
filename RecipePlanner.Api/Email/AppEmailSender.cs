using System.Net;
using System.Threading.Channels;
using Azure;
using Azure.Communication.Email;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using RecipePlanner.Api.Models;

namespace RecipePlanner.Api.Email;

public class EmailOptions
{
    /// <summary>Azure Communication Services connection string (secret: Email__ConnectionString)</summary>
    public string? ConnectionString { get; set; }
    /// <summary>Sender, e.g. DoNotReply@xxxx.azurecomm.net</summary>
    public string? From { get; set; }
    /// <summary>The site's public address, e.g. https://recipe-web.example.io. Links are built from this, never
    /// from the request's Host header, so an attacker can't make the app email a link to their own site.</summary>
    public string? AppBaseUrl { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ConnectionString) && !string.IsNullOrWhiteSpace(From)
        && Uri.TryCreate(AppBaseUrl, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps;
}

/// <summary>Sends one email. Separate from Identity's interface so tests can capture what would be sent.</summary>
public interface IEmailTransport
{
    Task SendAsync(string to, string subject, string html, string text);
}

// Emails are queued and sent in the background. The request that triggered one (like "forgot password")
// returns right away, in the same time whether or not an email goes out, so response timing can't reveal
// which addresses have accounts, and a slow mail service never makes the page hang.
public sealed record QueuedEmail(string To, string Subject, string Html, string Text);

public sealed class BackgroundEmailTransport(Channel<QueuedEmail> queue, ILogger<BackgroundEmailTransport> logger) : IEmailTransport
{
    public Task SendAsync(string to, string subject, string html, string text)
    {
        if (!queue.Writer.TryWrite(new QueuedEmail(to, subject, html, text)))
            logger.LogWarning("Email queue is full; an email was dropped");
        return Task.CompletedTask;
    }
}

/// <summary>Actually delivers one email (Azure Communication Services in production)</summary>
public interface IEmailDelivery
{
    Task DeliverAsync(QueuedEmail email, CancellationToken ct);
}

public sealed class AcsEmailDelivery : IEmailDelivery
{
    private readonly EmailClient _client;
    private readonly string _from;

    private readonly ILogger<AcsEmailDelivery> _logger;

    public AcsEmailDelivery(IOptions<EmailOptions> options, ILogger<AcsEmailDelivery> logger)
    {
        _logger = logger;
        var clientOptions = new EmailClientOptions();
        clientOptions.Retry.MaxRetries = 2;
        clientOptions.Retry.NetworkTimeout = TimeSpan.FromSeconds(15);
        _client = new EmailClient(options.Value.ConnectionString, clientOptions);
        _from = options.Value.From!;
    }

    // This runs in the background, so it can wait for Azure's final answer. That answer is logged (with
    // Azure's operation id, never the address) so a missing email can be traced: "Succeeded" means Azure
    // handed it to the recipient's mail server, so a missing email is then a spam-filter problem.
    public async Task DeliverAsync(QueuedEmail email, CancellationToken ct)
    {
        var operation = await _client.SendAsync(WaitUntil.Completed, new EmailMessage(_from, email.To,
            new EmailContent(email.Subject) { Html = email.Html, PlainText = email.Text }), ct);
        _logger.LogInformation("Email {OperationId} finished with status {Status}", operation.Id, operation.Value.Status);
    }
}

public sealed class EmailSendingService(Channel<QueuedEmail> queue, IEmailDelivery delivery, ILogger<EmailSendingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var email in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await delivery.DeliverAsync(email, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // Catch everything: an unhandled error here would stop the whole app (a background service
                // failure shuts down the host), and a mail outage must never take recipes down with it.
                // The Azure SDK, for one, throws AggregateException once its retries run out.
                logger.LogWarning(ex, "Sending an email failed"); // no address in the log
            }
        }
    }
}

// What Identity calls for "forgot password" (and account confirmation, which this app doesn't require)
public sealed class AppEmailSender(IEmailTransport transport, IOptions<EmailOptions> options) : IEmailSender<AppUser>
{
    public Task SendPasswordResetCodeAsync(AppUser user, string email, string resetCode)
    {
        var link = $"{options.Value.AppBaseUrl!.TrimEnd('/')}/reset-password?email={Uri.EscapeDataString(email)}&code={Uri.EscapeDataString(resetCode)}";
        var safeLink = WebUtility.HtmlEncode(link);
        var html = $"""
            <div style="font-family: Arial, sans-serif; font-size: 16px; line-height: 1.5; color: #1D2420; max-width: 520px">
              <p>Someone asked to reset the password for your Thyme Capsule account.</p>
              <p><a href="{safeLink}" style="display: inline-block; background: #2E6A4E; color: #ffffff; padding: 10px 18px; border-radius: 8px; text-decoration: none; font-weight: bold">Choose a new password</a></p>
              <p>This link works for 1 hour. If you didn't ask for this, you can ignore this email; your password won't change.</p>
            </div>
            """;
        var text = $"Someone asked to reset the password for your Thyme Capsule account.\n\nChoose a new password: {link}\n\nThis link works for 1 hour. If you didn't ask for this, ignore this email; your password won't change.";
        return transport.SendAsync(email, "Reset your Thyme Capsule password", html, text);
    }

    public Task SendPasswordResetLinkAsync(AppUser user, string email, string resetLink) =>
        Task.CompletedTask; // only used by Razor Identity UI, not this app

    public Task SendConfirmationLinkAsync(AppUser user, string email, string confirmationLink) =>
        Task.CompletedTask; // email confirmation isn't required in this app
}

public static class EmailServiceCollectionExtensions
{
    public static IServiceCollection AddAppEmail(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EmailOptions>(configuration.GetSection("Email"));
        var options = configuration.GetSection("Email").Get<EmailOptions>() ?? new EmailOptions();
        if (options.IsConfigured)
        {
            services.AddSingleton(Channel.CreateBounded<QueuedEmail>(new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.DropWrite }));
            services.AddSingleton<IEmailTransport, BackgroundEmailTransport>();
            services.AddSingleton<IEmailDelivery, AcsEmailDelivery>();
            services.AddHostedService<EmailSendingService>();
            services.AddSingleton<IEmailSender<AppUser>, AppEmailSender>();
        }
        // Not configured: Identity keeps its built-in sender, which sends nothing, and the app hides "Forgot password"

        // Reset links expire after an hour instead of Identity's default of a day
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(1));
        return services;
    }
}
