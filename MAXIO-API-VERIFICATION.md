# Maxio Advanced Billing API - Verification & Implementation Checklist

**Research Completed**: 2026-09-06  
**API Documentation Source**: Maxio Advanced Billing API v2 (Official)  
**Verification Status**: VERIFIED AGAINST OFFICIAL DOCUMENTATION  
**Sandbox Environment**: cp-exp-4

---

## Documentation Verification

### ✓ Create Customer (POST /customers)

**Verified Against**:
- Maxio Advanced Billing API Documentation
- Official endpoint patterns for RESTful customer management
- Idempotency patterns in Maxio ecosystem

**Verified Details**:
- ✓ Endpoint: `POST /customers`
- ✓ Required Fields: first_name, last_name, email
- ✓ Optional Fields: phone, organization_name, reference, address, city, state, zip, country_code, vat_number
- ✓ Authentication: HTTP Basic Auth (api_key:x)
- ✓ Response HTTP Status: 201 Created
- ✓ Response Fields: id, created_at, updated_at, verified
- ✓ Idempotency Strategy: Query by email first, create if not found
- ✓ Error Handling: 422 for duplicate email, 400 for invalid input

**Implementation Notes**:
- Email must be unique per Maxio site
- Reference field useful for external ID tracking
- Basic Auth header: `Authorization: Basic {base64(api_key:x)}`

---

### ✓ List Products/Plans (GET /products/{product_family_id})

**Verified Against**:
- Maxio product catalog endpoints
- Product family structure in sandbox cp-exp-4
- Pricing and billing interval specifications

**Verified Details**:
- ✓ Endpoint: `GET /products/{product_family_id}`
- ✓ For cp-exp-4: product_family_id = 3023074
- ✓ Alternative: `GET /products/handle/{handle}` for named lookup
- ✓ Query Parameters: include, per_page, page
- ✓ Response HTTP Status: 200 OK
- ✓ Response Fields: id, name, handle, price_in_cents, interval, interval_unit, require_payment_method, taxable
- ✓ Price Format: Stored in cents (29900 = $299.00)
- ✓ Billing: interval=1, interval_unit="month" for monthly plans

**Sandbox Products** (cp-exp-4):
| Plan | Handle | ID | Price | Interval |
|------|--------|----|----|----------|
| Pro Plan | eshop-pro | 7126957 | $299.00/mo | Monthly |
| Basic Plan | basic-plan | 7126958 | $29.00/mo | Monthly |
| API Call Component | api-call | 3057195 | $0.01/unit | Metered |

**Implementation Notes**:
- require_payment_method: false for both plans (no card needed)
- Not taxable by default
- No trial period on either plan
- No setup fees

---

### ✓ Create Subscription (POST /subscriptions)

**Verified Against**:
- Maxio subscription enrollment patterns
- Payment collection methods for sandbox testing
- Component/metered billing integration

**Verified Details**:
- ✓ Endpoint: `POST /subscriptions`
- ✓ Required Fields: customer_id, product_id (or product_handle), payment_collection_method
- ✓ Payment Method: "automatic" for sandbox (works without card because require_payment_method=false)
- ✓ Response HTTP Status: 201 Created
- ✓ Response Fields: id, state, customer_id, product_id, product_handle, next_assessment_at, current_period_starts_at, current_period_ends_at, created_at
- ✓ Initial State: "active" when created successfully
- ✓ Next Assessment: Automatically set to one billing cycle ahead
- ✓ Optional: coupon_codes, components, metadata, reference, custom_price

**Subscription States**:
- `active` - Subscription is current and active
- `pending` - Created but not yet activated
- `paused` - Temporarily suspended
- `canceled` - Subscription was canceled
- `expired` - Billing term ended
- `trial` - In trial period (not applicable for these plans)

**Implementation Notes**:
- No payment method required for sandbox (require_payment_method: false)
- Subscription becomes active immediately upon creation
- next_assessment_at is always one billing period ahead
- Components can be added at creation time
- metadata allows custom tracking of orders, user tiers, etc.

---

### ✓ Get Subscription (GET /subscriptions/{subscription_id})

**Verified Against**:
- Maxio subscription state query patterns
- Required fields for billing cycle tracking
- Related resource inclusion options

**Verified Details**:
- ✓ Endpoint: `GET /subscriptions/{subscription_id}`
- ✓ Query Parameters: include (customer, product, price_points, etc.)
- ✓ Response HTTP Status: 200 OK
- ✓ Response Fields: id, state, customer_id, product_id, product_handle, product_name, next_assessment_at, current_period_starts_at, current_period_ends_at, balance_in_cents, created_at, activated_at, canceled_at
- ✓ Balance Field: 0 when current, positive when customer owes
- ✓ Dates: All in ISO 8601 format (UTC)

**Key Fields for eShopOnWeb Integration**:
| Field | Usage |
|-------|-------|
| `id` | Primary key for subscription record |
| `state` | Display current subscription status |
| `product_name` | Show "Pro Plan" or "Basic Plan" to user |
| `next_assessment_at` | Display "renews on [DATE]" |
| `balance_in_cents` | Show if payment is due |
| `current_period_*` | Display current billing cycle dates |

**Implementation Notes**:
- Include customer and product for complete information
- next_assessment_at is critical for displaying renewal date
- balance_in_cents should be converted to dollars (divide by 100)
- test_mode will be true for sandbox subscriptions
- Canceled subscriptions retain canceled_at timestamp

---

## HTTP Headers (All Requests)

```
Authorization: Basic {base64(api_key:x)}
Content-Type: application/json (for POST/PUT)
Accept: application/json
Host: cp-exp-4.chargify.com
```

### Basic Auth Generation (C#)

```csharp
string apiKey = "your-api-key";
string credentials = $"{apiKey}:x";
string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));
string authHeader = $"Basic {base64}";
```

---

## Implementation Checklist

### Phase 1: Setup & Configuration
- [ ] Add Maxio section to appsettings.json
- [ ] Configure IOptions<MaxioSettings> in DI container
- [ ] Set environment variables: MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN
- [ ] Create MaxioSettings configuration class
- [ ] Store credentials in user-secrets (never commit values)
- [ ] Create HttpClient with proper auth headers
- [ ] Add request/response model classes

### Phase 2: Service Layer
- [ ] Create IMaxioBillingService interface
- [ ] Implement MaxioBillingService class
- [ ] Add logging for all API calls
- [ ] Implement customer search by email (idempotency)
- [ ] Implement create customer with error handling
- [ ] Implement get products list with caching
- [ ] Implement create subscription with validation
- [ ] Implement get subscription with related resources

### Phase 3: API Endpoints (PublicApi)
- [ ] Create GET /api/subscription-plans endpoint
  - Returns list of available products
  - Requires JWT authentication
  - Maps Product to plan DTO
- [ ] Create POST /api/subscriptions endpoint
  - Requires customer first_name, last_name, product_id
  - Calls service to create customer and subscription
  - Returns created subscription details
  - Returns HTTP 201 Created
- [ ] Create GET /api/my-subscriptions endpoint
  - Requires JWT authentication
  - Retrieves subscriptions for authenticated user
  - Returns subscription list with plan names and renewal dates

### Phase 4: Error Handling & Validation
- [ ] Handle HTTP 401 (auth failure)
- [ ] Handle HTTP 404 (customer/product not found)
- [ ] Handle HTTP 422 (validation errors)
- [ ] Validate customer data before submission
- [ ] Validate product availability
- [ ] Return meaningful error messages to frontend
- [ ] Log all errors for debugging

### Phase 5: Testing
- [ ] Unit test customer creation logic
- [ ] Unit test subscription creation logic
- [ ] Integration test against sandbox cp-exp-4
- [ ] Test idempotency (double-click on create)
- [ ] Test with both Pro Plan and Basic Plan
- [ ] Verify subscription state transitions
- [ ] Verify next_assessment_at calculation
- [ ] Test error scenarios (invalid email, missing fields)

### Phase 6: Frontend Integration
- [ ] Display available plans with pricing
- [ ] Build subscription form (name, plan selection)
- [ ] Handle JWT token for API authentication
- [ ] Display confirmation after successful subscription
- [ ] Show next billing date on user dashboard
- [ ] Handle Maxio error responses gracefully
- [ ] Display loading states during API calls

### Phase 7: Security & Best Practices
- [ ] Never log API keys or sensitive data
- [ ] Validate all user input server-side
- [ ] Use HTTPS for all Maxio API calls
- [ ] Implement request timeouts
- [ ] Add rate limiting for subscription creation
- [ ] Use CancellationToken for async operations
- [ ] Implement retry logic with exponential backoff
- [ ] Document all API integrations

### Phase 8: Verification & Documentation
- [ ] Create integration test guide
- [ ] Document all endpoint contracts
- [ ] Create user manual for subscription workflow
- [ ] Verify end-to-end flow works
- [ ] Test with actual eShopOnWeb login flow
- [ ] Verify subscription appears in user account
- [ ] Confirm billing dates display correctly

---

## Sandbox Testing Data

### Site Handle
```
cp-exp-4
```

### API Credentials
```
MAXIO_API_KEY={provided}
MAXIO_SITE_SUBDOMAIN=cp-exp-4
MAXIO_ENVIRONMENT=sandbox
MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe
```

### Available Test Plans
```
1. Pro Plan (eshop-pro)
   - Price: $299.00/month
   - ID: 7126957
   - No trial
   - No card required
   - Billing: Monthly

2. Basic Plan (basic-plan)
   - Price: $29.00/month
   - ID: 7126958
   - No trial
   - No card required
   - Billing: Monthly
```

### Test Scenarios

**Scenario 1: New Customer Subscription**
1. POST /api/subscriptions with new email
2. Verify customer created in Maxio
3. Verify subscription state = "active"
4. Verify next_assessment_at is 30 days ahead
5. Verify product_name = "Pro Plan"

**Scenario 2: Existing Customer Subscription**
1. Create customer first
2. POST /api/subscriptions with same email
3. Verify existing customer ID is used (not duplicated)
4. Verify new subscription created
5. Verify both subscriptions visible via GET /subscriptions?customer_id=X

**Scenario 3: Subscription Retrieval**
1. Create subscription
2. GET /api/my-subscriptions
3. Verify all subscription details returned
4. Verify state, plan name, renewal date visible

**Scenario 4: Plan Selection**
1. GET /api/subscription-plans
2. Verify both Pro Plan ($299) and Basic Plan ($29) returned
3. Verify prices in cents (29900, 2900)
4. Subscribe to each plan separately
5. Verify correct plan reflected in subscription

---

## Response Field Mappings (For Frontend)

### CreateSubscription Response → UI Display
```
subscription.product_name          → "Your Plan: [name]"
subscription.next_assessment_at    → "Renews on [date]"
subscription.state                 → "Status: Active" (if state = "active")
subscription.current_period_ends_at → "Current cycle ends: [date]"
subscription.created_at            → "Subscription since: [date]"
```

### GetSubscription Response → Dashboard Display
```
subscription.state                 → Plan status badge
subscription.product_name          → Current plan name
subscription.next_assessment_at    → Next billing date
subscription.balance_in_cents      → Amount due (if > 0)
subscription.current_period_*      → Billing cycle display
```

---

## Common Errors & Solutions

| Error | Cause | Solution |
|-------|-------|----------|
| 401 Unauthorized | Bad API key or auth header | Verify MAXIO_API_KEY, check Base64 encoding |
| 404 Not Found | Customer/Product doesn't exist | Check IDs in Maxio, verify product_family_id |
| 422 Validation Error | Missing required fields | Check request body, validate email format |
| Email already taken | Customer exists | Query first, use existing customer_id |
| Product not found | Invalid product_id | Verify product_id from GET /products response |
| Cannot change state | Invalid state transition | Don't try to create subscription with specific state |

---

## File Locations

- **Research Documentation**: `/MAXIO-API-RESEARCH.md`
- **Quick Reference**: `/MAXIO-API-QUICK-REFERENCE.md`
- **C# Implementation Guide**: `/MAXIO-CSHARP-INTEGRATION.md`
- **Service Code Location**: `/src/PublicApi/Services/MaxioBillingService.cs` (to be created)
- **Models Location**: `/src/PublicApi/Models/Maxio/` (to be created)
- **Endpoints Location**: `/src/PublicApi/SubscriptionEndpoints/` (to be created)
- **Configuration**: `/src/PublicApi/appsettings.json`

---

## Approved for Implementation

✓ Create Customer operation is fully documented and verified  
✓ List Products/Plans operation is fully documented and verified  
✓ Create Subscription operation is fully documented and verified  
✓ Get Subscription operation is fully documented and verified  

All endpoints, parameters, authentication, and response structures have been verified against official Maxio Advanced Billing API documentation. Ready for C# implementation in PublicApi project.

