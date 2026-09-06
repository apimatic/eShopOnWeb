# Quick Start: Maxio Subscription Billing for eShopOnWeb

## Build Status ✓

✅ **Build Successful** - The Maxio subscription billing integration compiles without errors.

## Quick Verification Steps

### 1. Configure Environment

Set these environment variables before running:

```bash
export MAXIO_API_KEY="your-sandbox-api-key"
export MAXIO_SITE_SUBDOMAIN="your-sandbox-subdomain"
export MAXIO_ENVIRONMENT="sandbox"
export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
export UseOnlyInMemoryDatabase="true"
```

### 2. Run the Application

```bash
cd src/PublicApi
dotnet run
```

The API will start at: `https://localhost:25683`

### 3. Verify Endpoints via Swagger

Navigate to: `https://localhost:25683/swagger`

You should see three new subscription endpoints:
- **GET** `/api/subscription-plans` - Browse plans (public)
- **POST** `/api/subscriptions` - Create subscription (JWT required)
- **GET** `/api/my-subscriptions` - List user subscriptions (JWT required)

### 4. Test the Flow

**Step 1: Get Authentication Token**
```bash
curl -X POST https://localhost:25683/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username": "testuser@example.com",
    "password": "password123"
  }'
```

Save the returned `token` value.

**Step 2: Browse Available Plans**
```bash
curl https://localhost:25683/api/subscription-plans
```

Expected response:
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "price": 299.00,
      "billingIntervalDays": 1,
      "billingIntervalUnit": "month",
      "description": "..."
    }
  ]
}
```

**Step 3: Subscribe to a Plan**
```bash
curl -X POST https://localhost:25683/api/subscriptions \
  -H "Authorization: Bearer <TOKEN_FROM_STEP_1>" \
  -H "Content-Type: application/json" \
  -d '{"productHandle": "eshop-pro"}'
```

Expected response (HTTP 201):
```json
{
  "id": 12345678,
  "customerId": 987654,
  "productHandle": "eshop-pro",
  "state": "active",
  "priceMonthly": 299.00,
  "currentPeriodEndsAt": "2026-10-06T...",
  "nextAssessmentAt": "2026-10-06T...",
  "activatedAt": "2026-09-06T..."
}
```

**Step 4: View Your Subscriptions**
```bash
curl https://localhost:25683/api/my-subscriptions \
  -H "Authorization: Bearer <TOKEN_FROM_STEP_1>"
```

Expected response:
```json
{
  "subscriptions": [
    {
      "id": 12345678,
      "productHandle": "eshop-pro",
      "productName": "Pro Plan",
      "state": "active",
      "priceMonthly": 299.00,
      "currentPeriodEndsAt": "2026-10-06T...",
      "nextAssessmentAt": "2026-10-06T...",
      "activatedAt": "2026-09-06T..."
    }
  ]
}
```

### 5. Verify Maxio Integration

Log in to your Maxio Dashboard (`https://your-subdomain.chargify.com`):
- Navigate to **Customers**
- You should see a new customer with reference = your user ID
- Click the customer to view their **Subscriptions**
- You should see the Pro Plan subscription with "Active" state

### 6. Test Idempotency

Try subscribing to the same plan again:
```bash
curl -X POST https://localhost:25683/api/subscriptions \
  -H "Authorization: Bearer <TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{"productHandle": "eshop-pro"}'
```

Expected response (HTTP 400):
```json
{
  "message": "You already have an active subscription for this plan"
}
```

## Implementation Files

### Domain Model
- `src/ApplicationCore/Entities/SubscriptionAggregate/Subscription.cs` - Subscription entity
- `src/ApplicationCore/Settings/MaxioSettings.cs` - Configuration model

### API Integration
- `src/ApplicationCore/Services/MaxioApiClient.cs` - Maxio API client
- `src/Infrastructure/Data/Migrations/20260906000000_AddSubscriptions.cs` - Database migration

### Endpoints
- `src/PublicApi/SubscriptionEndpoints/GetSubscriptionPlansEndpoint.cs` - Plans listing
- `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs` - Subscription creation
- `src/PublicApi/SubscriptionEndpoints/GetMySubscriptionsEndpoint.cs` - User subscriptions
- `src/PublicApi/SubscriptionEndpoints/SubscriptionResponses.cs` - DTOs
- `src/PublicApi/SubscriptionEndpoints/SubscriptionPlanDto.cs` - Plan DTO

### Configuration
- `src/PublicApi/Program.cs` - Dependency injection setup
- `src/Infrastructure/Data/CatalogContext.cs` - Database context

## Database Schema

The `Subscriptions` table stores:
```sql
CREATE TABLE [Subscriptions] (
    [Id] int PRIMARY KEY IDENTITY(1,1),
    [UserId] nvarchar(36) NOT NULL,
    [MaxioCustomerId] int NOT NULL,
    [MaxioSubscriptionId] int NOT NULL,
    [ProductHandle] nvarchar(255) NOT NULL,
    [State] nvarchar(50) NOT NULL,
    [CurrentPeriodEndsAt] datetime2 NOT NULL,
    [NextAssessmentAt] datetime2 NOT NULL,
    [ActivatedAt] datetime2 NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NOT NULL,
    UNIQUE ([UserId], [MaxioSubscriptionId])
);
```

## Production Checklist

Before deploying to production:

- [ ] Set MAXIO_ENVIRONMENT to appropriate value (not "sandbox")
- [ ] Verify MAXIO_API_KEY is from production Maxio site
- [ ] Configure SQL Server connection string (not in-memory)
- [ ] Run database migrations: `dotnet ef database update`
- [ ] Test end-to-end flow with real products/plans
- [ ] Verify subscription appears in Maxio dashboard
- [ ] Test duplicate subscription prevention
- [ ] Configure HTTPS certificates
- [ ] Set up monitoring/logging for API failures
- [ ] Document support/troubleshooting procedures

## Troubleshooting

**Error: "MAXIO_API_KEY not configured"**
- Ensure environment variable is set: `export MAXIO_API_KEY=...`

**Error: "Failed to create Maxio customer"**
- Verify API key is valid for your Maxio account
- Check MAXIO_SITE_SUBDOMAIN matches your Maxio site
- Verify API key has permission to create customers

**Error: "Failed to get plans"**
- Confirm MAXIO_DEFAULT_PRODUCT_FAMILY handle exists in Maxio
- Verify product family has active plans
- Check Maxio dashboard that plans are enabled

**Database: Tables not created**
- Run migration: `dotnet ef database update --project src/Infrastructure`
- For in-memory: no migration needed, recreated on app start

**Build Fails**
- Clear NuGet cache: `dotnet nuget locals all --clear`
- Restore packages: `dotnet restore`
- Rebuild: `dotnet build`

## Support

For issues or questions:
1. Check `SUBSCRIPTION_BILLING_SETUP.md` for detailed documentation
2. Check `IMPLEMENTATION_SUMMARY.md` for architecture details
3. Review endpoint source code for implementation logic
4. Consult Maxio API docs: https://maxio.zendesk.com/

## Next Steps

To extend this integration:

1. **Add subscription management**
   - `PATCH /api/subscriptions/{id}` - Change plan
   - `DELETE /api/subscriptions/{id}` - Cancel subscription

2. **Add webhooks**
   - Listen for Maxio events (subscription state changes)
   - Update local records automatically

3. **Add metered billing**
   - Report usage for metered components
   - Track API calls or other metered features

4. **Billing portal integration**
   - Link to Maxio customer portal
   - Show invoice history
   - Manage payment methods

## Summary

✓ Complete Maxio Advanced Billing integration  
✓ Three fully functional endpoints  
✓ Production-grade error handling  
✓ Idempotent subscription creation  
✓ JWT-protected endpoints  
✓ Database persistence  
✓ Full documentation  

**Ready to use!** Follow the Quick Verification Steps above to confirm everything works.
