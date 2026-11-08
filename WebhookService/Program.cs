
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using WebhookService.Data;
using WebhookService.Endpoints;
using WebhookService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddDbContext<WebhookDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("Webhooks")));

builder.Services.AddScoped<IWebhookPublisher, WebhookPublisher>();

builder.Services.AddHttpClient("webhooks", client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = false,
    UseProxy = false
});

builder.Services.AddHostedService<WebhookDeliveryWorker>();

// Configure your application's real JWT or cookie
// authentication scheme before enabling authorization.
// builder.Services.AddAuthentication(...);
builder.Services.AddAuthentication();

if (builder.Environment.IsDevelopment())
{
    // INFO: Workaround

    builder.Services.AddAuthorization(options =>
    {
        options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
            .RequireAssertion(_ => true)
            .Build();
    });
}

var app = builder.Build();

app.UseHttpsRedirection();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (app.Environment.IsDevelopment())
{
    // INFO: Workaround
    
    app.Use(async (context, next) =>
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "test-user"),
            new Claim(ClaimTypes.Name, "Development User"),
            new Claim("tenant_id", "test-tenant"),
            new Claim(ClaimTypes.Role, "Admin")
        };

        context.User = new ClaimsPrincipal(
            new ClaimsIdentity(claims, "Development"));

        await next(context);
    });
}
else
{
    app.UseAuthentication();
}

app.UseAuthorization();

app.MapWebhookEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapPost("/api/webhooks/test", async (
        IWebhookPublisher publisher,
        CancellationToken ct) =>
    {
        await publisher.PublishAsync(
            ownerId: "test-tenant",
            eventType: "booking.created",
            payload: new
            {
                BookingId = Guid.NewGuid(),
                StartsAt = DateTimeOffset.UtcNow.AddDays(1)
            },
            ct);

        return Results.Accepted();
    });
}

app.Run();
