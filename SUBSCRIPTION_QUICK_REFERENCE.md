# Maxio Subscription Integration - Quick Reference

## Quick Start

```bash
cd src/PublicApi
$env:DOTNET_ROLL_FORWARD = "Major"
$env:UseOnlyInMemoryDatabase = "true"
dotnet run
```

Then in another terminal:

```bash
# 1. Get token
$token = (curl -X POST https://localhost:28463/api/authenticate `
  -H "Content-Type: application/json" `
  -d '{"username":"demouser@microsoft.com","password":"Pass@word123"}' `
  -k).token

# 2. List plans
curl -X GET https://localhost:28463/api/subscription-plans `
  -H "Authorization: Bearer $token" -k

# 3. Create subscription
curl -X POST https://localhost:28463/api/subscriptions `
  -H "Authorization: Bearer $token" `
  -H "Content-Type: application/json" `
  -d '{"planHandle":"eshop-pro"}' -k

# 4. List user subscriptions
curl -X GET https://localhost:28463/api/my-subscriptions `
  -H "Authorization: Bearer $token" -k
```

## Endpoint Reference

| Method | Endpoint | Purpose | Auth |
|--------|----------|---------|------|
| GET | `/api/subscription-plans` | List available plans | JWT |
| POST | `/api/subscriptions` | Create subscription | JWT |
| GET | `/api/my-subscriptions` | List user subscriptions | JWT |

## Request/Response Payloads

### Create Subscription

**Request:**
```json
{
  "planHandle": "eshop-pro"
}
```

**Response:**
```json
{
  "subscriptionId": 12345678,
  "state": "active",
  "productHandle": "eshop-pro",
  "nextBillingAt": "2026-10-07T12:34:56Z",
  "status": "success",
  "correlationId": "..."
}
```

## Code Structure

```
Infrastructure/
├── Services/
│   ├── MaxioClient.cs           # Low-level HTTP client
│   └── SubscriptionService.cs   # Business logic
└── Dependencies.cs              # DI registration

PublicApi/
├── appsettings.json             # Config placeholders
└── SubscriptionEndpoints/
    ├── ListSubscriptionPlansEndpoint.cs
    ├── CreateSubscriptionEndpoint.cs
    ├── ListUserSubscriptionsEndpoint.cs
    ├── SubscriptionPlanDto.cs
    └── [Request/Response DTOs in each file]
```

## Key Classes

**MaxioClient**
```csharp
public interface IMaxioClient
{
    Task<T?> GetAsync<T>(string endpoint);
    Task<T?> PostAsync<T>(string endpoint, object? body);
    Task<T?> ListAsync<T>(string endpoint);
}
```

**SubscriptionService**
```csharp
public interface ISubscriptionService
{
    Task<List<ProductResponse>> ListProductsAsync();
    Task<SubscriptionResponse?> CreateSubscriptionAsync(
        string userId, string firstName, string lastName,
        string email, string productHandle);
    Task<List<SubscriptionResponse>> ListUserSubscriptionsAsync(string userId);
}
```

## Configuration

**Environment Variables (→ User Secrets):**
- `MAXIO_API_KEY` → `Maxio:ApiKey`
- `MAXIO_SITE_SUBDOMAIN` → `Maxio:Subdomain`
- `MAXIO_DEFAULT_PRODUCT_FAMILY` → `Maxio:ProductFamilyHandle`

**Optional:**
- `Maxio:BaseUrl` - override Maxio endpoint (default: `https://{subdomain}.chargify.com`)

## Debugging

**Check user-secrets:**
```bash
cd src/PublicApi
dotnet user-secrets list
```

**Rebuild user-secrets:**
```bash
cd src/PublicApi
dotnet user-secrets init
dotnet user-secrets set "Maxio:ApiKey" "your-key"
dotnet user-secrets set "Maxio:Subdomain" "cp-exp-3"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
```

**View logs:**
Enable in appsettings.json:
```json
{
  "Logging": {
    "LogLevel": {
      "Microsoft.eShopWeb.Infrastructure.Services": "Debug"
    }
  }
}
```

## Common Issues

| Issue | Solution |
|-------|----------|
| "Build failed" | Run `$env:DOTNET_ROLL_FORWARD = "Major"` first |
| "Port already in use" | Kill previous dotnet process or change port |
| "401 Unauthorized" | Pass `-H "Authorization: Bearer <token>"` |
| "Maxio API not found" | Check Maxio credentials in user-secrets |
| "SSL certificate error" | Use `-k` flag in curl or run `dotnet dev-certs https --trust` |

## Testing Checklist

```bash
# Test endpoints in order
1. GET /api/subscription-plans           # Should list 2+ plans
2. POST /api/subscriptions (eshop-pro)   # Should succeed
3. POST /api/subscriptions (basic-plan)  # Should succeed
4. GET /api/my-subscriptions             # Should list both
5. POST /api/subscriptions (invalid)      # Should error gracefully
```

## Maxio Sandbox Details

**Site:** cp-exp-3  
**Plans:**
- `eshop-pro` ($299/mo) - for testing premium  
- `basic-plan` ($29/mo) - for testing basic tier

**Special Properties:**
- No payment method required (sandbox mode)
- Customer reference = userId (prevents duplicates)
- Test card not needed (no real charges)

## Production Checklist

- [ ] Update `Maxio:ApiKey` to production key
- [ ] Update `Maxio:Subdomain` to production subdomain
- [ ] Update `Maxio:ProductFamilyHandle` to production family
- [ ] Implement payment method capture (use Billing.js)
- [ ] Add webhook handlers for subscription events
- [ ] Set up error alerting/logging
- [ ] Test with production plans
- [ ] Load test the endpoints
- [ ] Document SLA/error codes for clients

---

**Last Updated:** 2026-09-07  
**Version:** 1.0
