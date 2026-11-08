
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WebhookService.Data;
using WebhookService.Models;

namespace WebhookService.Services;

public sealed class WebhookDeliveryWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    ILogger<WebhookDeliveryWorker> logger)
    : BackgroundService
{
    private const int MaxAttempts = 8;
    private bool _urlValidationEnabled;

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _urlValidationEnabled = configuration.GetValue<bool>("Webhooks:DisableUrlValidation")!;

        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(5));

        do
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Webhook worker failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<WebhookDbContext>();

        var now = DateTimeOffset.UtcNow;

        var ids = await db.Deliveries
            .AsNoTracking()
            .Where(x =>
                (x.Status == WebhookDeliveryStatus.Pending &&
                 x.NextAttemptAt <= now) ||
                (x.Status == WebhookDeliveryStatus.Processing &&
                 x.LockedUntil < now))
            .OrderBy(x => x.NextAttemptAt)
            .Select(x => x.Id)
            .Take(50)
            .ToListAsync(ct);

        foreach (var id in ids)
        {
            var token = Guid.NewGuid();
            now = DateTimeOffset.UtcNow;

            // Atomic claim: only one worker gets this delivery.
            var claimed = await db.Deliveries
                .Where(x => x.Id == id &&
                    ((x.Status == WebhookDeliveryStatus.Pending &&
                      x.NextAttemptAt <= now) ||
                     (x.Status == WebhookDeliveryStatus.Processing &&
                      x.LockedUntil < now)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status,
                        WebhookDeliveryStatus.Processing)
                    .SetProperty(x => x.LockToken, token)
                    .SetProperty(x => x.LockedUntil,
                        now.AddMinutes(2)), ct);

            if (claimed == 0)
                continue;

            await DeliverAsync(db, id, token, ct);
        }
    }

    private async Task DeliverAsync(
        WebhookDbContext db,
        Guid id,
        Guid token,
        CancellationToken ct)
    {
        var delivery = await db.Deliveries
            .AsNoTracking()
            .Include(x => x.Subscription)
            .SingleAsync(x => x.Id == id, ct);

        int? statusCode = null;
        string? error = null;
        bool success = false;

        try
        {
            var subscription = delivery.Subscription;

            if (!subscription.IsActive)
                throw new InvalidOperationException(
                    "Subscription is disabled");

            if(_urlValidationEnabled) {
                // URL validation is performed at registration and
                // again before delivery.
                await WebhookUrlValidator.ValidateAsync(
                    subscription.EndpointUrl, ct);
            }

            var timestamp = DateTimeOffset.UtcNow
                .ToUnixTimeSeconds();

            var signedContent = $"{timestamp}.{delivery.Payload}";

            var signature = Convert.ToHexString(
                HMACSHA256.HashData(
                    Encoding.UTF8.GetBytes(subscription.Secret),
                    Encoding.UTF8.GetBytes(signedContent)))
                .ToLowerInvariant();

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                subscription.EndpointUrl);

            request.Content = new StringContent(
                delivery.Payload,
                Encoding.UTF8,
                "application/json");

            request.Headers.Add("X-Webhook-Id", id.ToString());
            request.Headers.Add("X-Webhook-Timestamp",
                timestamp.ToString());
            request.Headers.Add("X-Webhook-Signature",
                $"sha256={signature}");

            var client = httpClientFactory.CreateClient("webhooks");

            using var response = await client.SendAsync(
                request, ct);

            statusCode = (int)response.StatusCode;
            success = response.IsSuccessStatusCode;

            if (!success)
                error = $"HTTP {statusCode}";
        }
        catch (OperationCanceledException)
            when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        // Update only if this worker still owns the claim.
        var attempts = delivery.AttemptCount + 1;
        var nextStatus = success
            ? WebhookDeliveryStatus.Succeeded
            : attempts >= MaxAttempts
                ? WebhookDeliveryStatus.Failed
                : WebhookDeliveryStatus.Pending;

        var delay = TimeSpan.FromSeconds(
            Math.Min(3600, 10 * Math.Pow(2, attempts - 1)));

        var updated = await db.Deliveries
            .Where(x => x.Id == id && x.LockToken == token)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, nextStatus)
                .SetProperty(x => x.AttemptCount, attempts)
                .SetProperty(x => x.NextAttemptAt,
                    DateTimeOffset.UtcNow.Add(delay))
                .SetProperty(x => x.LockToken, (Guid?)null)
                .SetProperty(x => x.LockedUntil,
                    (DateTimeOffset?)null), ct);

        if (updated == 1)
        {
            db.Attempts.Add(new WebhookDeliveryAttempt
            {
                DeliveryId = id,
                HttpStatusCode = statusCode,
                Error = error
            });

            await db.SaveChangesAsync(ct);
        }
    }
}
