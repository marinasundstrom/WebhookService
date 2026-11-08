# README

## Set up secrets

```sh
cd WebhookSubscriber

dotnet user-secrets init

dotnet user-secrets set \
  "Webhooks:Secret" "YOUR_SUBSCRIPTION_SECRET"
```

## Start service

```sh
dotnet run --project WebhookSubscriber --urls https://localhost:7227
```

## Callback URL

```http
POST https://localhost:7227/webhooks
```