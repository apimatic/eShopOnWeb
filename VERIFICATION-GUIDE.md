# Maxio Subscription Billing - Verification Guide

This guide walks through verifying that the Maxio subscription billing integration is working end-to-end.

## Prerequisites

- Clone and open the eShopOnWeb repository
- Have Maxio sandbox credentials available (API key, subdomain)
- .NET SDK 8.0+ installed with rollForward enabled (global.json already configured)

## Step 1: Configure Maxio Credentials

```bash
cd src/PublicApi
dotnet user-secrets init
dotnet user-secrets set "Maxio:ApiKey" "your-maxio-api-key"
dotnet user-secrets set "Maxio:Subdomain" "cp-exp-3"  # or your sandbox subdomain
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
```

## Step 2: Run the Build Verification

```bash
# From repository root
dotnet build eShopOnWeb.sln

# Expected: Build succeeded with 0 errors
```

## Step 3: Start the PublicApi Service

```bash
cd src/PublicApi
dotnet run

# Expected output:
# info: PublicApi.Program[0]
#       PublicApi App created...
# info: PublicApi.Program[0]
#       Seeding Database...
# info: PublicApi.Program[0]
#       LAUNCHING PublicApi
# 
# Application started. Press Ctrl+C to shut down.
# Hosting environment: Development
# Content root path: C:\...\repo\src\PublicApi
# Now listening on: https://localhost:24783
# Now listening on: http://localhost:24784
```

The Swagger UI is available at: `https://localhost:24783/swagger`

## Step 4: Verify New Endpoints in Swagger UI

Open `https://localhost:24783/swagger` and verify these new endpoints appear:

1. **GET /api/subscription-plans** - "Get available subscription plans"
2. **POST /api/subscriptions** - "Create a subscription"
3. **GET /api/my-subscriptions** - "Get current user's subscriptions"

All three should be under the "Subscriptions" tag.

## Step 5: Test the Hero Flow

### 5a. Authenticate

1. In Swagger, find the POST /api/authenticate endpoint
2. Click "Try it out"
3. Use these test credentials:
   - Username: `demouser@microsoft.com`
   - Password: `DemoPassword123!` (or check the seed data for correct password)
4. Click "Execute"
5. Copy the `token` value from the response

### 5b. List Available Plans

1. Click the "Authorize" button (top right)
2. Paste: `Bearer <your-token-here>`
3. Click "Authorize"
4. Find GET /api/subscription-plans
5. Click "Try it out" → "Execute"
6. Expected response (200 OK):
   ```json
   {
     "plans": [
       {
         "id": 7126957,
         "name": "Pro Plan",
         "handle": "eshop-pro",
         "description": null,
         "price": 299,
         "billingCycle": "1 month"
       },
       {
         "id": 7126958,
         "name": "Basic Plan",
         "handle": "basic-plan",
         "description": null,
         "price": 29,
         "billingCycle": "1 month"
       }
     ]
   }
   ```

### 5c. Create a Subscription

1. Find POST /api/subscriptions
2. Click "Try it out"
3. Enter request body:
   ```json
   {
     "planHandle": "eshop-pro"
   }
   ```
4. Click "Execute"
5. Expected response (200 OK):
   ```json
   {
     "subscriptionId": 12345678,
     "state": "active",
     "nextBillingDate": "2024-10-06T12:00:00Z",
     "message": "Subscription created successfully"
   }
   ```

### 5d. Get User's Subscriptions

1. Find GET /api/my-subscriptions
2. Click "Try it out" → "Execute"
3. Expected response (200 OK) - should contain the subscription just created:
   ```json
   {
     "subscriptions": [
       {
         "id": 12345678,
         "state": "active",
         "productName": "Pro Plan",
         "price": 299,
         "currentPeriodEndsAt": "2024-10-06T12:00:00Z",
         "nextAssessmentAt": "2024-10-06T12:00:00Z",
         "activatedAt": "2024-09-06T12:00:00Z"
       }
     ]
   }
   ```

## Step 6: Verify Idempotency

### 6a. Create Another Subscription for the Same User

1. Use the same token from Step 5a
2. Call POST /api/subscriptions again with `planHandle: "eshop-pro"`
3. Expected behavior:
   - **First call**: Creates Maxio customer and subscription → 200 OK with subscription ID
   - **Second call**: Should fail because user already has subscription to this plan
   - Maxio validates and prevents duplicate subscriptions

### 6b. Create Subscription to Different Plan

1. Call POST /api/subscriptions with `planHandle: "basic-plan"`
2. Expected response (200 OK):
   ```json
   {
     "subscriptionId": 87654321,
     "state": "active",
     "nextBillingDate": "2024-10-06T12:00:00Z",
     "message": "Subscription created successfully"
   }
   ```

### 6c. Verify Multiple Subscriptions

1. Call GET /api/my-subscriptions
2. Expected response (200 OK) - should now contain BOTH subscriptions:
   ```json
   {
     "subscriptions": [
       {
         "id": 12345678,
         "state": "active",
         "productName": "Pro Plan",
         "price": 299.00,
         ...
       },
       {
         "id": 87654321,
         "state": "active",
         "productName": "Basic Plan",
         "price": 29.00,
         ...
       }
     ]
   }
   ```

## Step 7: Verify Data Persistence

1. Stop the PublicApi service (Ctrl+C)
2. Restart the PublicApi service
3. Authenticate again with the same user
4. Call GET /api/my-subscriptions
5. **Note**: Subscriptions will NOT appear because we're using in-memory database (data lost on restart)
   - This is expected behavior for development
   - In production with SQL Server, data would persist

## Step 8: Test Error Scenarios

### 8a. Missing Authorization Header

1. Don't set the Bearer token
2. Call GET /api/subscription-plans
3. Expected response (401 Unauthorized)

### 8b. Invalid Token

1. Set Bearer token to: `Bearer invalid-token`
2. Call GET /api/subscription-plans
3. Expected response (401 Unauthorized)

### 8c. Missing Required Field

1. With valid token, call POST /api/subscriptions
2. Send empty body: `{}`
3. Expected response (400 Bad Request):
   ```json
   {
     "error": "Plan handle is required"
   }
   ```

### 8d. Invalid Plan Handle

1. With valid token, call POST /api/subscriptions
2. Send: `{"planHandle": "nonexistent-plan"}`
3. Expected response (500 Internal Server Error) with Maxio error details

## Architecture & Implementation Details

### New Files Created

1. **Configuration**
   - `src/PublicApi/MaxioSettings.cs` - Settings class for Maxio configuration

2. **Service Layer**
   - `src/PublicApi/Maxio/MaxioService.cs` - HTTP client for Maxio API
   - `src/PublicApi/Maxio/MaxioProduct.cs` - DTO classes for products
   - `src/PublicApi/Maxio/MaxioCustomer.cs` - DTO classes for customers
   - `src/PublicApi/Maxio/MaxioSubscription.cs` - DTO classes for subscriptions

3. **API Endpoints**
   - `src/PublicApi/SubscriptionEndpoints/ListSubscriptionPlansEndpoint.cs` - GET /api/subscription-plans
   - `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs` - POST /api/subscriptions
   - `src/PublicApi/SubscriptionEndpoints/GetMySubscriptionsEndpoint.cs` - GET /api/my-subscriptions
   - `src/PublicApi/SubscriptionEndpoints/SubscriptionDto.cs` - Response DTO
   - `src/PublicApi/SubscriptionEndpoints/SubscriptionPlanDto.cs` - Response DTO

4. **Data Model**
   - `src/ApplicationCore/Entities/MaxioSubscriptionMapping.cs` - Entity to track user → Maxio customer mapping
   - Updated `src/Infrastructure/Identity/AppIdentityDbContext.cs` - Added DbSet for mappings

5. **Configuration Updates**
   - `src/PublicApi/Program.cs` - Added Maxio service registration and configuration
   - `src/PublicApi/appsettings.json` - Added Maxio configuration section

### How It Works

1. **User Authentication**
   - User authenticates with eShopOnWeb credentials
   - API returns JWT token with user claim

2. **Browse Plans**
   - GET /api/subscription-plans calls Maxio API to fetch products from configured family
   - Results are mapped to simplified DTOs and returned

3. **Create Subscription**
   - POST /api/subscriptions extracts user identity from JWT token
   - Service checks if Maxio customer exists for this user (via local mapping table)
   - If not, creates Maxio customer (using user ID as reference for idempotency)
   - Creates subscription in Maxio with remittance payment method (no card required)
   - Stores mapping between eShopOnWeb user and Maxio customer ID
   - Returns subscription details

4. **Get Subscriptions**
   - GET /api/my-subscriptions extracts user identity from JWT token
   - Looks up Maxio customer ID from local mapping
   - Queries Maxio for all subscriptions of that customer
   - Returns subscription details including state and next billing date

### Security

- All endpoints require JWT authentication via Bearer token
- Maxio credentials stored in user-secrets (never in repository)
- API key sent via Basic authentication to Maxio (base64 encoded)
- User can only access their own subscriptions

### Production Considerations

- Use SQL Server for persistent storage of user-subscription mappings
- Implement webhook handling for Maxio events (subscription renewal, cancellation, etc.)
- Add retry logic for transient Maxio API failures
- Implement caching of product list with TTL
- Add monitoring and logging for subscription events
- Consider implementing refund/cancellation flows
- Handle subscription state transitions properly

## Troubleshooting

| Issue | Solution |
|-------|----------|
| "Maxio service is not configured" | Verify `Maxio:Subdomain` in user secrets |
| "HTTP 401 Unauthorized" from Maxio | Verify `Maxio:ApiKey` is correct and set in user secrets |
| "No plans returned" | Verify `Maxio:ProductFamilyHandle` matches family handle in Maxio |
| "Subscription failed with 422" | Check request format matches Maxio API schema |
| Cannot access /swagger after restart | Trust HTTPS cert: `dotnet dev-certs https --trust` |
| "Port already in use" | Kill process on port 24783 or change in launchSettings.json |

## Next Steps

- Implement subscription cancellation endpoint
- Add webhook handler for Maxio events
- Implement metered billing for API calls (api-call component)
- Add UI for subscription management on the Web project
- Implement pause/resume functionality
- Add subscription pause handling in billing logic
