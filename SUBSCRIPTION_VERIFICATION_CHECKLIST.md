# Subscription Integration Verification Checklist

This checklist verifies that the Maxio subscription billing integration is correctly implemented and working.

## Pre-Setup Verification

### Configuration
- [ ] Maxio credentials obtained from Maxio sandbox console
- [ ] Created .NET user-secrets with Maxio configuration:
  ```bash
  cd src/PublicApi
  dotnet user-secrets set "Maxio:ApiKey" "your-key"
  dotnet user-secrets set "Maxio:Subdomain" "your-subdomain"
  dotnet user-secrets set "Maxio:ProductFamilyHandle" "your-product-family"
  ```
- [ ] User-secrets verified with: `dotnet user-secrets list`
- [ ] Environment variables NOT committed to repository
- [ ] `appsettings.json` contains only empty Maxio configuration keys

### Build & Compilation
- [ ] Project builds without errors: `dotnet build`
- [ ] PublicApi builds successfully: `dotnet build src/PublicApi/PublicApi.csproj`
- [ ] No compilation warnings related to Maxio integration
- [ ] All dependencies are properly resolved

### Runtime Verification
- [ ] HTTPS development certificate is trusted
- [ ] PublicApi starts without errors
- [ ] Swagger UI is accessible at `https://localhost:25043/swagger`
- [ ] No errors in application logs during startup
- [ ] Database seeding completes successfully

## Endpoint Functionality Verification

### Test Setup
- [ ] Start PublicApi application
- [ ] Database is seeded with test data
- [ ] Note a test user's credentials (e.g., `user@example.com`)

### Endpoint 1: List Subscription Plans (GET /api/subscription-plans)

#### Basic Functionality
- [ ] Endpoint is accessible without authentication
- [ ] Returns HTTP 200 OK
- [ ] Response includes `plans` array
- [ ] Each plan includes: `handle`, `name`, `priceInCents`, `description`
- [ ] At least one plan is returned
- [ ] Response includes valid `correlationId`

#### Plan Data Validation
- [ ] Product handles match Maxio product handles (e.g., "eshop-pro", "basic-plan")
- [ ] Product names are readable and descriptive
- [ ] Prices are in cents (e.g., 29900 for $299.00)
- [ ] Descriptions include both family name and plan name

#### Error Handling
- [ ] Returns reasonable error when Maxio API is unreachable
- [ ] Error messages don't expose sensitive information
- [ ] Logs contain details about failures (check application logs)

### Endpoint 2: Create Subscription (POST /api/subscriptions)

#### Authentication & Authorization
- [ ] Endpoint returns 401 Unauthorized without Bearer token
- [ ] Endpoint returns 401 Unauthorized with invalid token
- [ ] Endpoint returns 401 Unauthorized with expired token
- [ ] Endpoint accepts valid JWT Bearer token

#### Basic Functionality
- [ ] POST request with valid token creates subscription
- [ ] Returns HTTP 201 Created
- [ ] Response includes `subscriptionId` (valid integer)
- [ ] Response includes `productHandle` matching request
- [ ] Response includes subscription details:
  - [ ] `productName` (readable name)
  - [ ] `state` (should be "active" or "pending")
  - [ ] `priceInCents` (price of the plan)
  - [ ] `nextBillingAt` (future datetime)
  - [ ] `activatedAt` (current or recent datetime)
- [ ] Response includes valid `correlationId`
- [ ] HTTP header `Location` contains created subscription URL

#### Customer Creation
- [ ] On first subscription, a new Maxio customer is created
- [ ] Customer creation is logged
- [ ] Customer reference is set to user ID
- [ ] Customer email is set correctly
- [ ] Customer name fields are populated
- [ ] Verify in Maxio dashboard: customer exists with correct reference
- [ ] Verify in Maxio dashboard: subscription is active

#### Idempotent Behavior
- [ ] Subscribe same user twice returns successful response both times
- [ ] No duplicate customers created in Maxio
- [ ] Both subscriptions show in user's subscription list (if different products)
- [ ] Same product subscription doesn't create duplicate subscription

#### Request Validation
- [ ] Missing `productHandle` returns 400 Bad Request
- [ ] Invalid `productHandle` returns appropriate error
- [ ] Optional `productPricePointHandle` is accepted
- [ ] Request with unknown handle returns error from Maxio

#### Error Handling
- [ ] Maxio API errors are properly propagated
- [ ] Error messages are logged with context
- [ ] Failed subscription creation doesn't create half-mapped state
- [ ] Logs contain details about subscription creation attempts

### Endpoint 3: Get User Subscriptions (GET /api/my-subscriptions)

#### Authentication & Authorization
- [ ] Endpoint returns 401 Unauthorized without Bearer token
- [ ] Endpoint returns 401 Unauthorized with invalid token
- [ ] Endpoint returns 401 Unauthorized with expired token
- [ ] Endpoint accepts valid JWT Bearer token

#### Basic Functionality
- [ ] GET request with valid token returns subscriptions
- [ ] Returns HTTP 200 OK
- [ ] Response includes `subscriptions` array
- [ ] Response includes valid `correlationId`
- [ ] Each subscription includes required fields

#### Subscription Data
- [ ] Returns only subscriptions for authenticated user
- [ ] Does not return other users' subscriptions
- [ ] Each subscription includes:
  - [ ] `subscriptionId` (matches Maxio subscription ID)
  - [ ] `productHandle`
  - [ ] `productName`
  - [ ] `state` (active, pending, canceled, etc.)
  - [ ] `priceInCents`
  - [ ] `nextBillingAt`
  - [ ] `activatedAt`

#### Subscription Lifecycle
- [ ] After creating subscription, appears in this endpoint
- [ ] Multiple subscriptions are all returned
- [ ] Subscription count matches Maxio dashboard

#### Empty Subscriptions
- [ ] New user with no subscriptions returns empty array
- [ ] Returns HTTP 200 OK with empty subscriptions list
- [ ] Response is valid JSON structure

#### Error Handling
- [ ] Maxio API errors are properly handled
- [ ] Returns graceful error when customer not found (HTTP 200 with empty array)
- [ ] Logs contain details about subscription retrieval

## Maxio Integration Verification

### Maxio Dashboard Checks

After creating subscriptions, verify in Maxio dashboard:

- [ ] **Customers**: New customer exists with:
  - [ ] Correct email
  - [ ] Correct first/last name
  - [ ] Reference matching eShopOnWeb user ID
- [ ] **Subscriptions**: Subscription exists with:
  - [ ] Correct product/plan
  - [ ] Active state
  - [ ] Correct next billing date
  - [ ] Customer linked correctly
- [ ] **Products**: Product family and products are configured:
  - [ ] Product family exists with correct handle
  - [ ] Products exist with correct handles
  - [ ] Pricing is correct (no trial, no setup fee, taxable=false)
- [ ] **Webhooks**: (If configured) Webhooks are being received for subscription events

### API Communication Verification

- [ ] Maxio API authentication works (correct Basic Auth header)
- [ ] API key has proper permissions (create customers, create subscriptions)
- [ ] API responses are valid JSON
- [ ] Correct content-type headers are sent/received
- [ ] Request/response bodies match Maxio API specification

## Security Verification

### Credentials & Secrets
- [ ] No Maxio API key found in appsettings.json
- [ ] No Maxio credentials in environment variable config (only keys, not values)
- [ ] No secrets in git history: `git log -p | grep -i "maxio.*key"`
- [ ] User-secrets are properly isolated to local machine
- [ ] Dev certificate is properly trusted

### Authentication & Authorization
- [ ] Unauthenticated requests to protected endpoints are rejected
- [ ] Authenticated users can only see their own subscriptions
- [ ] JWT tokens properly expire
- [ ] Token validation properly checks signature and expiration
- [ ] No sensitive information in error responses

### API Security
- [ ] HTTPS is enforced for all endpoints
- [ ] HTTP requests redirect to HTTPS
- [ ] Maxio API uses secure communication (HTTPS)
- [ ] Basic Auth credentials are only sent over HTTPS
- [ ] No credentials logged in application logs

## Performance & Reliability

### Response Times
- [ ] List plans endpoint responds in < 500ms
- [ ] Create subscription endpoint responds in < 2000ms
- [ ] Get subscriptions endpoint responds in < 1000ms
- [ ] API remains responsive under normal load

### Logging & Diagnostics
- [ ] Application logs contain appropriate diagnostic messages
- [ ] Logs include correlation IDs for request tracing
- [ ] Error logs contain sufficient detail for troubleshooting
- [ ] No excessive logging that impacts performance
- [ ] Logs don't contain sensitive information

### Error Recovery
- [ ] Transient Maxio API failures don't crash application
- [ ] Application handles HTTP errors from Maxio gracefully
- [ ] Timeout handling prevents hanging requests
- [ ] Partial failures (e.g., customer created but subscription failed) are handled

## Code Quality Verification

### Code Structure
- [ ] Maxio integration is properly separated into dedicated classes
- [ ] Endpoints follow established patterns from existing endpoints
- [ ] Configuration is properly abstracted
- [ ] Business logic is in service layer, not endpoints
- [ ] HTTP client usage is consistent and proper

### Testing & Documentation
- [ ] Code is well-commented where logic is complex
- [ ] Public methods have clear signatures
- [ ] Integration documentation is comprehensive
- [ ] Example requests/responses are provided
- [ ] Troubleshooting guide covers common issues

### Dependencies
- [ ] No unnecessary dependencies added
- [ ] Used existing libraries where possible (no new NuGet packages)
- [ ] HttpClient is properly configured and reused
- [ ] No memory leaks from HttpClient usage

## Integration Test Plan

### User Journey Test

1. [ ] **Unauthenticated User**
   - Can list plans
   - Cannot create subscription (401)
   - Cannot view subscriptions (401)

2. [ ] **New User**
   - Authenticates successfully
   - Views available plans
   - Selects and subscribes to a plan
   - Subscription appears in their subscription list
   - User ID becomes Maxio customer reference

3. [ ] **Returning User**
   - Authenticates
   - Existing subscriptions appear in list
   - Can subscribe to additional plans
   - No duplicate customer created in Maxio

4. [ ] **Multiple Users**
   - Each user sees only their subscriptions
   - Separate Maxio customers created for each
   - No cross-user data leakage

### Failure Scenarios

1. [ ] **Maxio API Down**
   - Get reasonable error response
   - Application doesn't crash
   - Error is logged

2. [ ] **Invalid Product Handle**
   - Returns 400 or appropriate error
   - No partial state left behind
   - Error message is helpful

3. [ ] **Duplicate Subscription**
   - Idempotent behavior verified
   - No duplicate state created
   - User gets success response

4. [ ] **Large Response Handling**
   - User with many subscriptions gets all subscriptions
   - Response is properly formatted
   - No truncation issues

## Documentation Verification

- [ ] README includes setup instructions
- [ ] Configuration keys are documented
- [ ] Example requests/responses are included
- [ ] Authentication requirements are clear
- [ ] Troubleshooting section covers common issues
- [ ] Maxio dashboard verification steps are provided
- [ ] Environment gotchas are documented

## Final Verification

- [ ] All items in this checklist are verified ✓
- [ ] Integration works end-to-end
- [ ] Documentation is complete and accurate
- [ ] Code is clean and follows established patterns
- [ ] Ready for production use

## Notes

Use this section to document any issues found and resolutions:

```
Issue: [Description]
Resolution: [How it was resolved]
```
