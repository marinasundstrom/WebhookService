# README

## Start Postgres

```sh
docker compose up -d
```

## Set up database

```sh
dotnet ef migrations add InitialWebhooks
dotnet ef database update
```

## API: Publish event

```csharp
await publisher.PublishAsync(
    ownerId: business.Id.ToString(),
    eventType: "booking.created",
    payload: new
    {
        BookingId = booking.Id,
        CustomerId = booking.CustomerId,
        StartsAt = booking.StartsAt
    },
    cancellationToken);
```