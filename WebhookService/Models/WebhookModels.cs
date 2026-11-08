
using System.Text.Json;

namespace WebhookService.Models;

public enum WebhookDeliveryStatus
{
    Pending,
    Processing,
    Succeeded,
    Failed
}

public sealed class WebhookSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Owner of the subscription, such as a business or tenant.
    public required string OwnerId { get; set; }

    public required Uri EndpointUrl { get; set; }

    // Store encrypted at rest in production.
    public required string Secret { get; set; }

    public HashSet<string> Events { get; set; } = [];

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
        = DateTimeOffset.UtcNow;
}

public sealed class WebhookDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SubscriptionId { get; set; }

    public WebhookSubscription Subscription { get; set; } = null!;

    public required string EventType { get; set; }

    public required string Payload { get; set; }

    public WebhookDeliveryStatus Status { get; set; }
        = WebhookDeliveryStatus.Pending;

    public int AttemptCount { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }
        = DateTimeOffset.UtcNow;

    public DateTimeOffset? LockedUntil { get; set; }

    public Guid? LockToken { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
        = DateTimeOffset.UtcNow;

    public List<WebhookDeliveryAttempt> Attempts { get; set; } = [];
}

public sealed class WebhookDeliveryAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DeliveryId { get; set; }

    public int? HttpStatusCode { get; set; }

    public string? Error { get; set; }

    public DateTimeOffset AttemptedAt { get; set; }
        = DateTimeOffset.UtcNow;
}
