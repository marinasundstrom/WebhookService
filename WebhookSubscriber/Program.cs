
using System.Text.Json;
using WebhookSubscriber.Models;
using WebhookSubscriber.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<WebhookSignatureValidator>();

var app = builder.Build();

app.UseHttpsRedirection();

app.MapPost("/webhooks", async (
    HttpRequest request,
    WebhookSignatureValidator validator,
    ILogger<Program> logger,
    CancellationToken ct) =>
{
    var timestamp = request.Headers[
        "X-Webhook-Timestamp"].ToString();

    var signature = request.Headers[
        "X-Webhook-Signature"].ToString();

    var deliveryId = request.Headers[
        "X-Webhook-Id"].ToString();

    using var stream = new MemoryStream();

    await request.Body.CopyToAsync(stream, ct);

    var body = stream.ToArray();

    if (!validator.Validate(body, timestamp, signature))
    {
        logger.LogWarning(
            "Webhook rejected: invalid signature");

        return Results.Unauthorized();
    }

    WebhookEvent? webhookEvent;

    try
    {
        webhookEvent = JsonSerializer.Deserialize<WebhookEvent>(
            body,
            new JsonSerializerOptions(
                JsonSerializerDefaults.Web));
    }
    catch (JsonException)
    {
        return Results.BadRequest("Invalid JSON payload.");
    }

    if (webhookEvent is null)
        return Results.BadRequest("Missing webhook event.");

    logger.LogInformation(
        "Received webhook {DeliveryId}: {EventType}, EventId: {EventId}",
        deliveryId,
        webhookEvent.Type,
        webhookEvent.Id);

    switch (webhookEvent.Type)
    {
        case "booking.created":
            logger.LogInformation(
                "Booking created: {Data}",
                webhookEvent.Data);
            break;

        case "booking.cancelled":
            logger.LogInformation(
                "Booking cancelled: {Data}",
                webhookEvent.Data);
            break;

        default:
            logger.LogInformation(
                "Unhandled webhook event: {EventType}",
                webhookEvent.Type);
            break;
    }

    return Results.Ok(new
    {
        Received = true,
        webhookEvent.Id
    });
});

app.Run();
