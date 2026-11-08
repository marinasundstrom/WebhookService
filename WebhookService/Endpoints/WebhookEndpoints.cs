
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using WebhookService.Data;
using WebhookService.Models;
using WebhookService.Services;

namespace WebhookService.Endpoints;

public record CreateWebhookRequest(
    string Url,
    string[] Events);

public record UpdateWebhookRequest(
    string Url,
    string[] Events,
    bool IsActive);

public record WebhookResponse(
    Guid Id,
    string Url,
    string[] Events,
    bool IsActive);

public static class WebhookEndpoints
{
    public static RouteGroupBuilder MapWebhookEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/webhooks")
            .RequireAuthorization();

        group.MapGet("/", async (
            ClaimsPrincipal user,
            WebhookDbContext db,
            CancellationToken ct) =>
        {
            var ownerId = GetOwnerId(user);

            var subscriptions = await db.Subscriptions
                .AsNoTracking()
                .Where(x => x.OwnerId == ownerId)
                .ToListAsync(ct);

            return Results.Ok(subscriptions.Select(ToResponse));
        });

        group.MapPost("/", async (
            CreateWebhookRequest request,
            ClaimsPrincipal user,
            IConfiguration configuration,
            WebhookDbContext db,
            CancellationToken ct) =>
        {
            if (!Uri.TryCreate(request.Url,
                    UriKind.Absolute, out var url))
                return Results.BadRequest("Invalid URL.");

            var _urlValidationEnabled = configuration.GetValue<bool>("Webhooks:DisableUrlValidation")!;

            if (_urlValidationEnabled)
            {
                try
                {
                    await WebhookUrlValidator.ValidateAsync(url, ct);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(ex.Message);
                }
            }

            if (request.Events is not { Length: > 0 } ||
                request.Events.Any(string.IsNullOrWhiteSpace))
                return Results.BadRequest("Events are required.");

            var subscription = new WebhookSubscription
            {
                OwnerId = GetOwnerId(user),
                EndpointUrl = url,
                Events = request.Events.ToHashSet(
                    StringComparer.Ordinal),
                Secret = Convert.ToHexString(
                    RandomNumberGenerator.GetBytes(32))
            };

            db.Subscriptions.Add(subscription);
            await db.SaveChangesAsync(ct);

            // Secret is returned only at creation.
            return Results.Created(
                $"/api/webhooks/{subscription.Id}",
                new
                {
                    Subscription = ToResponse(subscription),
                    subscription.Secret
                });
        });

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateWebhookRequest request,
            ClaimsPrincipal user,
            IConfiguration configuration,
            WebhookDbContext db,
            CancellationToken ct) =>
        {
            var subscription = await db.Subscriptions
                .SingleOrDefaultAsync(x =>
                    x.Id == id &&
                    x.OwnerId == GetOwnerId(user), ct);

            if (subscription is null)
                return Results.NotFound();

            if (!Uri.TryCreate(request.Url,
                    UriKind.Absolute, out var url))
                return Results.BadRequest("Invalid URL.");

            var _urlValidationEnabled = configuration.GetValue<bool>("Webhooks:DisableUrlValidation")!;

            if (_urlValidationEnabled)
            {
                try
                {
                    await WebhookUrlValidator.ValidateAsync(url, ct);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(ex.Message);
                }
            }

            if (request.Events is not { Length: > 0 } ||
                request.Events.Any(string.IsNullOrWhiteSpace))
                return Results.BadRequest("Events are required.");

            subscription.EndpointUrl = url;
            subscription.Events = request.Events.ToHashSet(
                StringComparer.Ordinal);
            subscription.IsActive = request.IsActive;

            await db.SaveChangesAsync(ct);

            return Results.Ok(ToResponse(subscription));
        });

        group.MapDelete("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal user,
            WebhookDbContext db,
            CancellationToken ct) =>
        {
            var subscription = await db.Subscriptions
                .SingleOrDefaultAsync(x =>
                    x.Id == id &&
                    x.OwnerId == GetOwnerId(user), ct);

            if (subscription is null)
                return Results.NotFound();

            // Soft-delete to preserve delivery history.
            subscription.IsActive = false;
            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        });

        group.MapGet("/{id:guid}/deliveries", async (
            Guid id,
            ClaimsPrincipal user,
            WebhookDbContext db,
            CancellationToken ct) =>
        {
            var ownerId = GetOwnerId(user);

            var exists = await db.Subscriptions.AnyAsync(
                x => x.Id == id && x.OwnerId == ownerId, ct);

            if (!exists)
                return Results.NotFound();

            var deliveries = await db.Deliveries
                .AsNoTracking()
                .Where(x => x.SubscriptionId == id)
                .OrderByDescending(x => x.CreatedAt)
                .Take(100)
                .Select(x => new
                {
                    x.Id,
                    x.EventType,
                    x.Status,
                    x.AttemptCount,
                    x.CreatedAt,
                    x.NextAttemptAt
                })
                .ToListAsync(ct);

            return Results.Ok(deliveries);
        });

        return group;
    }

    private static string GetOwnerId(ClaimsPrincipal user) =>
        user.FindFirstValue("tenant_id")
        ?? throw new UnauthorizedAccessException(
            "Missing tenant identity.");

    private static WebhookResponse ToResponse(
        WebhookSubscription subscription) =>
        new(
            subscription.Id,
            subscription.EndpointUrl.AbsoluteUri,
            subscription.Events.ToArray(),
            subscription.IsActive);
}
