
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebhookService.Data;
using WebhookService.Models;

namespace WebhookService.Services;

public interface IWebhookPublisher
{
    Task PublishAsync<T>(
        string ownerId,
        string eventType,
        T payload,
        CancellationToken cancellationToken = default);
}

public sealed class WebhookPublisher(
    WebhookDbContext db) : IWebhookPublisher
{
    public async Task PublishAsync<T>(
        string ownerId,
        string eventType,
        T payload,
        CancellationToken cancellationToken = default)
    {
        var subscriptions = await db.Subscriptions
            .Where(x => x.OwnerId == ownerId && x.IsActive)
            .ToListAsync(cancellationToken);

        var json = JsonSerializer.Serialize(new
        {
            Id = Guid.NewGuid(),
            Type = eventType,
            OccurredAt = DateTimeOffset.UtcNow,
            Data = payload
        });

        foreach (var subscription in subscriptions)
        {
            if (!subscription.Events.Contains(eventType) &&
                !subscription.Events.Contains("*"))
                continue;

            db.Deliveries.Add(new WebhookDelivery
            {
                SubscriptionId = subscription.Id,
                EventType = eventType,
                Payload = json
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
