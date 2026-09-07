# Maxio Subscription Billing - Quick Start Guide

## 30-Second Overview

✅ **Done**: Subscription billing system for eShopOnWeb using Maxio Advanced Billing
- 3 API endpoints ready to use
- JWT authentication built-in
- Idempotent customer management
- Full documentation included

## Setup (5 minutes)

### 1. Verify Build Works
```bash
cd repo
dotnet build src/PublicApi/PublicApi.csproj
```
Expected: ✅ Build succeeded

### 2. Set Maxio Credentials
```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" "your-sandbox-api-key"
dotnet user-secrets set "Maxio:Subdomain" "your-sandbox-subdomain"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
```

### 3. Start Application
```bash
cd repo
DOTNET_ROLL_FORWARD=Major dotnet run --project src/PublicApi/PublicApi.csproj
```

## Test (2 minutes)

### 1. Get JWT Token
```bash
curl -X POST https://localhost:28223/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass123$"}'
```
Copy the `token` value

### 2. List Plans
```bash
curl -H "Authorization: Bearer {your-token}" \
  https://localhost:28223/api/subscription-plans
```

### 3. Create Subscription
```bash
curl -X POST https://localhost:28223/api/subscriptions \
  -H "Authorization: Bearer {your-token}" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}'
```

### 4. List Your Subscriptions
```bash
curl -H "Authorization: Bearer {your-token}" \
  https://localhost:28223/api/my-subscriptions
```

## API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/subscription-plans` | List available plans |
| POST | `/api/subscriptions` | Subscribe to a plan |
| GET | `/api/my-subscriptions` | List your subscriptions |

All require `Authorization: Bearer {jwt-token}` header

## Key Features

- **Automatic Customer Creation**: First subscription auto-creates customer in Maxio
- **Idempotent**: Safe to call multiple times, no duplicates
- **State Tracking**: Shows subscription state and next billing date
- **Error Handling**: Clear error messages in responses
- **Security**: JWT authentication, secrets in user-secrets

## Files Added

```
src/PublicApi/
├── Maxio/
│   ├── MaxioConfiguration.cs
│   └── MaxioClient.cs
├── SubscriptionEndpoints/
│   ├── ListSubscriptionPlansEndpoint.cs
│   ├── CreateSubscriptionEndpoint.cs
│   └── ListMySubscriptionsEndpoint.cs
```

## Documentation

- **SUBSCRIPTION_BILLING_SETUP.md** - Complete setup and testing guide
- **IMPLEMENTATION_SUMMARY.md** - Architecture and design decisions
- **VERIFICATION_CHECKLIST.md** - Build and feature verification
- **QUICKSTART.md** - This file

## Troubleshooting

| Issue | Solution |
|-------|----------|
| "Maxio configuration is missing" | Set user-secrets (see Setup step 2) |
| "Failed to create customer" | Check API key and subdomain are correct |
| JWT auth fails | Use token from `/api/authenticate` endpoint |
| Can't start app | Try: `DOTNET_ROLL_FORWARD=Major dotnet run ...` |

## Next Steps

1. ✅ Build succeeds
2. ✅ Endpoints implemented
3. → Set real Maxio credentials
4. → Test endpoints locally
5. → Verify in Maxio dashboard
6. → Deploy to production

## Production Checklist

Before going live:
- [ ] Set production Maxio credentials
- [ ] Add rate limiting on subscription endpoint
- [ ] Add email verification before subscribe
- [ ] Implement Maxio webhooks for state changes
- [ ] Add subscription management UI
- [ ] Test with real payment methods
- [ ] Enable audit logging
- [ ] Monitor API errors

---

For complete details, see **SUBSCRIPTION_BILLING_SETUP.md**
