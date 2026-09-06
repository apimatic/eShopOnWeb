# Quick Start Guide - Maxio Subscription Integration

## 30-Second Setup

### 1. Configure Maxio Credentials
```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" "YOUR_MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "YOUR_MAXIO_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
```

### 2. Build & Run
```bash
DOTNET_ROLL_FORWARD=Major dotnet run --launch-profile PublicApi
```

The API will start at: `https://localhost:24943`

## 5-Minute Testing

### Open Another Terminal

#### 1. Get Authentication Token
```bash
curl -X POST https://localhost:24943/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser","password":"Pass@word1"}' \
  -k
```

Copy the `token` value from the response.

#### 2. List Subscription Plans
```bash
curl -X GET https://localhost:24943/api/subscription-plans \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -k
```

You should see the available subscription plans.

#### 3. Create a Subscription
```bash
curl -X POST https://localhost:24943/api/subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Content-Type: application/json" \
  -d '{"planHandle":"eshop-pro"}' \
  -k
```

You should see a successful subscription response.

#### 4. List Your Subscriptions
```bash
curl -X GET https://localhost:24943/api/my-subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -k
```

You should see the subscription you just created.

## Environment Variables (Alternative to User Secrets)

```bash
export MAXIO_API_KEY=YOUR_KEY
export MAXIO_SITE_SUBDOMAIN=YOUR_SUBDOMAIN
export MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe
export MAXIO_ENVIRONMENT=sandbox
```

Then run the application with these environment variables set.

## Docker/Production Environment Variables

```bash
docker run -e MAXIO_API_KEY=YOUR_KEY \
           -e MAXIO_SITE_SUBDOMAIN=YOUR_SUBDOMAIN \
           -e MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe \
           -e MAXIO_ENVIRONMENT=sandbox \
           your-eshop-image
```

## Troubleshooting

### "Failed to retrieve subscription plans"
- Verify Maxio API key and subdomain are correct
- Check network connectivity to Maxio sandbox
- Verify ProductFamilyHandle "eshop-subscribe" exists in Maxio

### "User identification failed"
- Ensure token is included in Authorization header
- Format must be: `Authorization: Bearer TOKEN`

### "Failed to create subscription"
- Verify plan handle "eshop-pro" exists in your Maxio sandbox
- Check that the product family is "eshop-subscribe"

## Test Users

The application comes with a test user:
- **Username**: demouser
- **Password**: Pass@word1

## Test Subscription Plans

The following plans should be configured in Maxio:
- **eshop-pro**: $299.00/month
- **basic-plan**: $29.00/month

## Key Files

- **Configuration**: `src/PublicApi/MaxioSettings.cs`
- **API Integration**: `src/Infrastructure/Services/MaxioBillingService.cs`
- **Endpoints**:
  - `src/PublicApi/SubscriptionEndpoints/SubscriptionPlansListEndpoint.cs`
  - `src/PublicApi/SubscriptionEndpoints/SubscriptionCreateEndpoint.cs`
  - `src/PublicApi/SubscriptionEndpoints/SubscriptionListEndpoint.cs`

## API Endpoints Summary

| Method | Endpoint | Auth | Purpose |
|--------|----------|------|---------|
| GET | `/api/subscription-plans` | JWT | List available plans |
| POST | `/api/subscriptions` | JWT | Create subscription |
| GET | `/api/my-subscriptions` | JWT | List user subscriptions |

## Documentation

- **Integration Guide**: `MAXIO_INTEGRATION_GUIDE.md`
- **Implementation Details**: `IMPLEMENTATION_SUMMARY.md`
- **Verification Checklist**: `VERIFICATION_CHECKLIST.md`
- **This Guide**: `QUICK_START.md`

## Next Steps

1. Complete the 5-minute testing above
2. Review `MAXIO_INTEGRATION_GUIDE.md` for detailed testing
3. Check `IMPLEMENTATION_SUMMARY.md` for architecture details
4. Read `VERIFICATION_CHECKLIST.md` to confirm all components

## Support

For issues, refer to:
- **Maxio API Documentation**: https://docs.maxio.com/
- **eShopOnWeb Repository**: https://github.com/dotnet-architecture/eShopOnWeb
- **OpenAPI Specification**: `maxio-spec/openapi.yaml` in this repository

---

**Ready to test?** Start with the 30-second setup above! 🚀
