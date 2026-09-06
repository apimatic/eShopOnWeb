# Maxio Subscription Billing Integration for eShopOnWeb

## Implementation Summary

This integration adds recurring-subscription billing to eShopOnWeb using Maxio Advanced Billing as the system of record. The implementation is **additive and parallel** to the existing one-time commerce flow.

### Architecture

**Configuration:**
- Maxio SDK credentials are loaded from user-secrets (not committed to repo)
- Configuration keys: `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` (optional)
- All credentials come from environment variables on startup

**Service Layer:**
- `MaxioSubscriptionService` wraps all SDK operations in `src/PublicApi/Services/`
- Implements idempotent customer creation/lookup via user ID as customer reference
- Handles subscription lifecycle (create, list, retrieve)

**API Endpoints (JWT-authenticated, on PublicApi):**
1. **GET /api/subscription-plans** — List available subscription plans from the `eshop-subscribe` product family
2. **POST /api/subscriptions** — Subscribe logged-in user to a plan (creates Maxio customer if needed)
3. **GET /api/my-subscriptions** — Retrieve user's active subscriptions

### Implementation Details

**Maxio SDK Usage:**
- Package: `AsadAli.AdvancedBilling.Sdk` v1.0.2
- Client: `MaxioAdvancedBillingClient` with Basic auth (API key as username, "x" as password)
- Operations:
  - `Customers.ReadCustomerByReference()` — Idempotent customer lookup
  - `Customers.CreateCustomer()` — Create customer (if not found)
  - `Products.ListProducts()` — List all products, filtered by family handle in-memory
  - `Subscriptions.CreateSubscription()` — Create subscription without payment method required
  - `Customers.ListCustomerSubscriptions()` — Retrieve user's subscriptions

**Database:**
- Uses in-memory database (`UseOnlyInMemoryDatabase=true`) for this demo
- No local subscription storage needed — Maxio is the system of record
- User ID ↔ subscription mapping maintained in Maxio via `customer_reference` field

**Error Handling:**
- SDK operations throw `SdkException<T>` which are caught and logged
- Typed errors: `CreateCustomerError`, `CreateSubscriptionError`
- Raw errors: `RawError` for operations without typed error models
- 404 on customer lookup triggers idempotent customer creation

---

## Verification Steps

### Prerequisites

1. **Verify environment variables are set:**
   ```bash
   echo $MAXIO_API_KEY
   echo $MAXIO_SITE_SUBDOMAIN
   echo $MAXIO_DEFAULT_PRODUCT_FAMILY  # should be "eshop-subscribe"
   ```

2. **Verify user-secrets are configured in PublicApi:**
   ```bash
   cd src/PublicApi
   dotnet user-secrets list
   ```
   Should show `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`

### Build & Run

3. **Build the solution:**
   ```bash
   dotnet build eShopOnWeb.sln
   ```
   Should complete with 0 errors (4 warnings are pre-existing)

4. **Run PublicApi:**
   ```bash
   cd src/PublicApi
   $env:DOTNET_ROLL_FORWARD = "Major"
   $env:UseOnlyInMemoryDatabase = "true"
   dotnet run
   ```
   Server starts on `https://localhost:27683`

### Test the Integration

5. **Get a JWT token (Authenticate):**
   ```bash
   $token = curl -X POST `
     "https://localhost:27683/api/authenticate" `
     -H "Content-Type: application/json" `
     -d @'{"username":"demouser@microsoft.com","password":"Pass@word1"}'` `
     -SkipCertificateCheck | ConvertFrom-Json
   $bearer = $token.Token
   echo "Bearer token: $bearer"
   ```

6. **List subscription plans:**
   ```bash
   curl -X GET "https://localhost:27683/api/subscription-plans" `
     -H "Authorization: Bearer $bearer" `
     -SkipCertificateCheck | ConvertFrom-Json | ConvertTo-Json -Depth 10
   ```
   **Expected response:** Array of 2 plans (`eshop-pro` $299/mo, `basic-plan` $29/mo)

7. **Subscribe to a plan:**
   ```bash
   $sub = curl -X POST "https://localhost:27683/api/subscriptions" `
     -H "Authorization: Bearer $bearer" `
     -H "Content-Type: application/json" `
     -d @'{"productHandle":"eshop-pro"}'` `
     -SkipCertificateCheck | ConvertFrom-Json
   $sub | ConvertTo-Json -Depth 10
   ```
   **Expected response:** 
   - `Subscription.Id` > 0
   - `Subscription.State` = "active" or "assessing"
   - `Subscription.ActivatedAt` ≠ null
   - `Subscription.CurrentPeriodEndsAt` ≠ null (future date)

8. **Retrieve my subscriptions:**
   ```bash
   curl -X GET "https://localhost:27683/api/my-subscriptions" `
     -H "Authorization: Bearer $bearer" `
     -SkipCertificateCheck | ConvertFrom-Json | ConvertTo-Json -Depth 10
   ```
   **Expected response:** Array containing the subscription created in step 7

### Verify Maxio Side Effects

9. **Verify customer was created in Maxio sandbox** (`cp-exp-1` site):
   - Log in to Maxio dashboard at `https://cp-exp-1.chargify.com`
   - Navigate to **Customers**
   - Look for a customer with reference = `demouser@microsoft.com` (the JWT subject claim / user ID)
   - Confirm customer has 1 active subscription on the Pro Plan

10. **Subscribe to a second plan (same user, different plan):**
    ```bash
    $sub2 = curl -X POST "https://localhost:27683/api/subscriptions" `
      -H "Authorization: Bearer $bearer" `
      -H "Content-Type: application/json" `
      -d @'{"productHandle":"basic-plan"}'` `
      -SkipCertificateCheck | ConvertFrom-Json
    echo "Subscription 2 ID: $($sub2.Subscription.Id)"
    ```

11. **Retrieve my subscriptions again:**
    ```bash
    curl -X GET "https://localhost:27683/api/my-subscriptions" `
      -H "Authorization: Bearer $bearer" `
      -SkipCertificateCheck | ConvertFrom-Json | ConvertTo-Json -Depth 10
    ```
    **Expected response:** Array with 2 subscriptions (Pro + Basic)

---

## Known Limitations & Notes

### Sandbox Mode
- **No payment method required** — This is sandbox behavior. Production would require payment profile creation.
- **Metered usage** (`api-call` component) is seeded but not recorded in this integration. Usage ingestion is out of scope.

### Product Family Filtering
- The `ListProducts` operation does not have a direct product-family-handle filter.
- Current implementation: post-filter by `product.ProductFamily.Handle` in memory.
- Alternative: pre-query `ProductFamilies.ReadProductFamilyByHandle("eshop-subscribe")` to get IDs, then filter in `ListProducts` call.

### Database State
- In-memory database loses all data on app restart.
- User ID ↔ subscription mapping only lives in Maxio; Maxio is the system of record.
- Idempotent customer creation ensures no duplicates even if the subscription request is retried.

### Error Handling
- 404 on customer lookup gracefully triggers idempotent creation.
- All SDK errors are logged and propagated to the caller.
- Connection failures to Maxio will surface as HTTP 5xx errors.

---

## Files Modified/Created

### New Files
- `src/PublicApi/Services/MaxioSubscriptionService.cs` — Maxio SDK wrapper service
- `src/PublicApi/SubscriptionEndpoints/ListSubscriptionPlansEndpoint.cs` — Plan listing endpoint
- `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs` — Subscription creation endpoint
- `src/PublicApi/SubscriptionEndpoints/GetMySubscriptionsEndpoint.cs` — User subscriptions endpoint

### Modified Files
- `Directory.Packages.props` — Added Maxio SDK package reference
- `src/PublicApi/PublicApi.csproj` — Added Maxio SDK package reference
- `src/PublicApi/Program.cs` — Registered Maxio client and service in DI, configured from user-secrets

### Configuration
- User-secrets (not in repo):
  - `Maxio:ApiKey` (from `MAXIO_API_KEY` env var)
  - `Maxio:Subdomain` (from `MAXIO_SITE_SUBDOMAIN` env var)
  - `Maxio:ProductFamilyHandle` (from `MAXIO_DEFAULT_PRODUCT_FAMILY` env var, default `"eshop-subscribe"`)
  - `Maxio:BaseUrl` (optional override, from config)

---

## Troubleshooting

**401 Unauthorized on endpoints**
- Verify the JWT token is valid and non-expired
- Ensure `Authorization: Bearer <token>` header is present and correctly formatted
- Endpoints require valid JWT authentication; the storefront cookie will not work

**404 Product not found**
- Verify `eshop-pro` and `basic-plan` handles exist in the Maxio sandbox catalog
- Confirm `Maxio:ProductFamilyHandle` is set to `"eshop-subscribe"` (the product family handle)
- Check Maxio dashboard to confirm products are in the correct family

**400 Bad Request on subscription creation**
- Ensure `productHandle` field is provided in the request body
- Verify the user is authenticated (valid JWT token)
- Check logs for Maxio API error details

**Connection timeout or SSL errors**
- Ensure HTTPS dev certificate is trusted: `dotnet dev-certs https --check`
- Verify network connectivity to Maxio sandbox at `https://{MAXIO_SITE_SUBDOMAIN}.chargify.com`
- Confirm `Maxio:Subdomain` environment variable is set correctly

**Subscription create succeeds but no subscription in "my subscriptions"**
- Verify the new subscription's state: it may be in `assessing` or `trialing` state, not `active`
- Confirm customer was created with the correct `reference` (should match the user ID from JWT)
- Check Maxio dashboard to confirm subscription exists on the customer

---

## Success Criteria

✅ Solution builds without errors (`dotnet build eShopOnWeb.sln`)
✅ PublicApi runs without Maxio SDK or startup errors
✅ `/api/subscription-plans` returns list of available plans
✅ `/api/subscriptions` POST creates subscription idempotently
✅ `/api/my-subscriptions` returns user's subscriptions
✅ Maxio customer created/reused on subscription creation
✅ Subsequent subscriptions by same user reuse the existing Maxio customer
✅ All Maxio credentials are loaded from user-secrets, never hardcoded or committed
✅ No secrets appear in git history or logs
