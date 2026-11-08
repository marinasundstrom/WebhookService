
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WebhookService.Models;

namespace WebhookService.Data;

public sealed class WebhookDbContext(
    DbContextOptions<WebhookDbContext> options)
    : DbContext(options)
{
    public DbSet<WebhookSubscription> Subscriptions =>
        Set<WebhookSubscription>();

    public DbSet<WebhookDelivery> Deliveries =>
        Set<WebhookDelivery>();

    public DbSet<WebhookDeliveryAttempt> Attempts =>
        Set<WebhookDeliveryAttempt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var subscription = modelBuilder.Entity<WebhookSubscription>();

        subscription.HasKey(x => x.Id);

        subscription.Property(x => x.EndpointUrl)
            .HasConversion(
                uri => uri.AbsoluteUri,
                value => new Uri(value))
            .HasMaxLength(2048);

        subscription.Property(x => x.Events)
            .HasColumnType("jsonb")
            .HasConversion(
                new ValueConverter<HashSet<string>, string>(
                    events => JsonSerializer.Serialize(
                        events, (JsonSerializerOptions?)null),
                    json => JsonSerializer.Deserialize<HashSet<string>>(
                        json, (JsonSerializerOptions?)null)
                        ?? new HashSet<string>()),
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<HashSet<string>>(
                    (a, b) => a != null && b != null &&
                        a.SetEquals(b),
                    v => v.Aggregate(0, (hash, item) =>
                        hash ^ item.GetHashCode()),
                    v => new HashSet<string>(v)));

        subscription.HasIndex(x => x.OwnerId);

        var delivery = modelBuilder.Entity<WebhookDelivery>();

        delivery.HasKey(x => x.Id);

        delivery.HasOne(x => x.Subscription)
            .WithMany()
            .HasForeignKey(x => x.SubscriptionId)
            .OnDelete(DeleteBehavior.Restrict);

        delivery.HasIndex(x => new
        {
            x.Status,
            x.NextAttemptAt
        });

        delivery.HasIndex(x => x.LockedUntil);

        modelBuilder.Entity<WebhookDeliveryAttempt>()
            .HasKey(x => x.Id);
    }
}
