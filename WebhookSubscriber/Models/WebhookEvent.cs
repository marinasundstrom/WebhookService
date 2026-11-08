using System.Text.Json;

namespace WebhookSubscriber.Models;

public sealed record WebhookEvent(
    Guid Id,
    string Type,
    DateTimeOffset OccurredAt,
    JsonElement Data);
