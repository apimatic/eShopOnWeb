# Maxio Advanced Billing API - Research Documentation

**Research Date**: 2026-09-06  
**API Version**: Advanced Billing (v1)  
**Sandbox Site Handle**: `cp-exp-4`  
**Base URL Pattern**: `https://{subdomain}.chargify.com/api/v2/`  
**Authentication**: HTTP Basic Auth or X-Chargify-Token header

---

## Authentication

### Method 1: HTTP Basic Auth (Recommended for integration)
- **Header**: `Authorization: Basic {base64(api_key:x)}`
- **Format**: Encode `{MAXIO_API_KEY}:x` in base64 where `x` is the literal character "x"
- **Example**: 
  ```
  Authorization: Basic cW1xbXRhbDFzZXZ0Ym05MzJrcm5mbXAxMm46eA==
  ```

### Method 2: X-Chargify-Token Header
- **Header**: `X-Chargify-Token: {MAXIO_API_KEY}`

### Configuration
```csharp
// From environment variables:
var apiKey = Environment.GetEnvironmentVariable("MAXIO_API_KEY");
var subdomain = Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN"); // e.g., "cp-exp-4"
var environment = Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT"); // e.g., "sandbox"
var productFamily = Environment.GetEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY");

// Construct base URL
string baseUrl = "https://{subdomain}.chargify.com/api/v2/";
// OR use MAXIO_BASE_URL override if provided
```

---

## 1. Create Customer

**Purpose**: Create a new customer in Maxio (idempotent operation preferred)

### HTTP Method & Endpoint
```
POST /customers
```

### Full URL Example
```
https://cp-exp-4.chargify.com/api/v2/customers
```

### Required Headers
```
Authorization: Basic {base64_encoded_credentials}
Content-Type: application/json
Accept: application/json
```

### Request Body - Required Fields

```json
{
  "customer": {
    "first_name": "John",
    "last_name": "Doe",
    "email": "john.doe@example.com"
  }
}
```

**Required Parameters**:
- `first_name` (string): Customer's first name. Max 30 characters
- `last_name` (string): Customer's last name. Max 30 characters  
- `email` (string): Customer's email address (must be unique per site)

### Request Body - Optional Fields

```json
{
  "customer": {
    "first_name": "John",
    "last_name": "Doe",
    "email": "john.doe@example.com",
    "phone": "+1-555-123-4567",
    "organization_name": "ACME Corp",
    "reference": "customer-123",
    "address": "123 Main St",
    "address_2": "Suite 100",
    "city": "Portland",
    "state": "OR",
    "zip": "97214",
    "country_code": "US",
    "vat_number": "US12345678"
  }
}
```

**Optional Parameters**:
- `phone` (string): Customer's phone number
- `organization_name` (string): Company/organization name
- `reference` (string): Your internal reference ID (external_id, max 50 chars)
- `address` (string): Street address
- `address_2` (string): Apartment/suite number
- `city` (string): City name
- `state` (string): State/province code (2 chars for US/CA)
- `country_code` (string): ISO 3166-1 alpha-2 country code (e.g., "US", "CA")
- `vat_number` (string): VAT/tax ID

### Response - Success (HTTP 201)

```json
{
  "customer": {
    "id": 12345678,
    "first_name": "John",
    "last_name": "Doe",
    "email": "john.doe@example.com",
    "phone": "+1-555-123-4567",
    "organization_name": "ACME Corp",
    "reference": "customer-123",
    "address": "123 Main St",
    "address_2": "Suite 100",
    "city": "Portland",
    "state": "OR",
    "zip": "97214",
    "country_code": "US",
    "created_at": "2026-09-06T10:15:30Z",
    "updated_at": "2026-09-06T10:15:30Z",
    "parent_id": null,
    "vat_number": "US12345678",
    "verified": false
  }
}
```

**Key Response Fields**:
- `id` (integer): Unique customer ID in Maxio (needed for subscriptions)
- `created_at` (ISO 8601): Timestamp when customer was created
- `updated_at` (ISO 8601): Last update timestamp
- `verified` (boolean): Whether customer email has been verified

### Response - Error (HTTP 422 / 400)

```json
{
  "errors": {
    "email": [
      "has already been taken"
    ]
  }
}
```

### Idempotency Notes

**Important**: The POST /customers endpoint is NOT idempotent by Maxio design. To prevent duplicate customers:

1. **Option A - Search before create**: Query GET /customers?email={email} first
   - If found, use existing customer ID
   - If not found, create new customer

2. **Option B - Use reference field**: 
   - Always include a `reference` field with a unique external ID
   - Your app can deduplicate using the reference value before calling Maxio
   
3. **Option C - Prefer GET endpoint first** (Recommended):
   ```
   GET /customers?search={email}
   POST /customers (if not found)
   ```

---

## 2. List Products/Plans

**Purpose**: Retrieve available plans/products from a product family

### HTTP Method & Endpoint

```
GET /products/{product_family_id}
```

OR for paginated product families:

```
GET /product_families/{product_family_id}/products
```

### Full URL Examples

Using Product Family ID (from MAXIO_DEFAULT_PRODUCT_FAMILY):
```
https://cp-exp-4.chargify.com/api/v2/products/3023074
```

For sandbox `cp-exp-4`, this product family contains the plans:
- Pro Plan (`eshop-pro`) - ID: 7126957 - $299.00/month
- Basic Plan (`basic-plan`) - ID: 7126958 - $29.00/month

### Query Parameters

```
GET /products/3023074?
  include=product_family&
  per_page=50&
  page=1
```

**Optional Query Parameters**:
- `include` (string): Comma-separated values. Options: `product_family`, `price_points`
- `per_page` (integer): Results per page (default: 20, max: 200)
- `page` (integer): Page number (default: 1)

### Required Headers
```
Authorization: Basic {base64_encoded_credentials}
Accept: application/json
```

### Response - Success (HTTP 200)

```json
{
  "products": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "description": "Professional subscription plan",
      "accounting_code": null,
      "request_credit_limit_percentage_as_string": null,
      "credit_limit_percentage": null,
      "created_at": "2024-01-15T08:00:00Z",
      "updated_at": "2024-01-15T08:00:00Z",
      "price_in_cents": 29900,
      "interval": 1,
      "interval_unit": "month",
      "initial_charge_in_cents": null,
      "trial_price_in_cents": null,
      "trial_interval": null,
      "trial_interval_unit": null,
      "expiration_interval": null,
      "expiration_interval_unit": null,
      "return_params": null,
      "require_credit_card": false,
      "require_payment_method": false,
      "taxable": false,
      "product_family": {
        "id": 3023074,
        "name": "eShop Subscriptions",
        "handle": "eshop-subscribe",
        "description": "eShop subscription product family",
        "accounting_code": null,
        "created_at": "2024-01-15T07:00:00Z",
        "updated_at": "2024-01-15T07:00:00Z"
      }
    },
    {
      "id": 7126958,
      "name": "Basic Plan",
      "handle": "basic-plan",
      "description": "Basic subscription plan",
      "price_in_cents": 2900,
      "interval": 1,
      "interval_unit": "month",
      "require_credit_card": false,
      "require_payment_method": false,
      "taxable": false
    }
  ]
}
```

**Key Response Fields**:
- `id` (integer): Product/plan ID (needed for subscription creation)
- `name` (string): Display name of the plan
- `handle` (string): Unique slug/identifier (e.g., "eshop-pro")
- `price_in_cents` (integer): Price in cents ($299.00 = 29900)
- `interval` (integer): Billing interval (1 = monthly)
- `interval_unit` (string): Unit of billing interval ("month", "day", "year")
- `require_payment_method` (boolean): Whether payment method is required
- `taxable` (boolean): Whether plan is subject to taxes

### Alternative: Get Specific Product by Handle

```
GET /products/handle/{handle}
```

Example:
```
https://cp-exp-4.chargify.com/api/v2/products/handle/eshop-pro
```

---

## 3. Create Subscription

**Purpose**: Enroll a customer in a plan

### HTTP Method & Endpoint

```
POST /subscriptions
```

### Full URL Example
```
https://cp-exp-4.chargify.com/api/v2/subscriptions
```

### Required Headers
```
Authorization: Basic {base64_encoded_credentials}
Content-Type: application/json
Accept: application/json
```

### Request Body - Minimal Required

```json
{
  "subscription": {
    "customer_id": 12345678,
    "product_id": 7126957,
    "payment_collection_method": "automatic"
  }
}
```

**Absolutely Required Parameters**:
- `customer_id` (integer): The Maxio customer ID (from Create Customer response)
- `product_id` (integer): The product/plan ID (from List Products response)
  - OR use `product_handle` (string): Plan handle like "eshop-pro"
- `payment_collection_method` (string): Set to `"automatic"` or `"remittance"`
  - For sandbox with `require_payment_method: false`, use `"automatic"`

### Request Body - Common Optional Fields

```json
{
  "subscription": {
    "customer_id": 12345678,
    "product_id": 7126957,
    "payment_collection_method": "automatic",
    "coupon_codes": ["SAVE10"],
    "components": [
      {
        "component_id": 3057195,
        "quantity": 100
      }
    ],
    "custom_price": {
      "expiration_interval": null,
      "expiration_interval_unit": null,
      "interval": 1,
      "interval_unit": "month",
      "name": "Custom Price",
      "price": 19999,
      "trial_interval": null,
      "trial_interval_unit": null,
      "trial_price": null
    },
    "metadata": {
      "order_id": "ORD-12345",
      "user_tier": "premium"
    }
  }
}
```

**Optional Parameters**:
- `coupon_codes` (array): Coupon codes to apply (array of strings)
- `components` (array): Metered/quantity components (see structure below)
  - `component_id` (integer): Component ID
  - `quantity` (number): Initial quantity for metered component
- `custom_price` (object): Override the product's standard pricing
- `metadata` (object): Custom key-value pairs for your tracking
- `reference` (string): Your internal subscription reference
- `notes` (string): Internal notes about subscription
- `snap_day` (integer): Specific day of month for billing (1-28)

### Response - Success (HTTP 201)

```json
{
  "subscription": {
    "id": 98765432,
    "state": "active",
    "customer_id": 12345678,
    "product_id": 7126957,
    "product_handle": "eshop-pro",
    "customer": {
      "id": 12345678,
      "first_name": "John",
      "last_name": "Doe",
      "email": "john.doe@example.com",
      "created_at": "2026-09-06T10:15:30Z",
      "updated_at": "2026-09-06T10:15:30Z"
    },
    "payment_collection_method": "automatic",
    "balance_in_cents": 0,
    "total_revenue_in_cents": 0,
    "product_price_point_id": 12345,
    "product_price_point_name": "Default",
    "next_assessment_at": "2026-10-06T00:00:00Z",
    "test_mode": true,
    "activated_at": "2026-09-06T10:15:30Z",
    "created_at": "2026-09-06T10:15:30Z",
    "updated_at": "2026-09-06T10:15:30Z",
    "scheduled_cancellation_at": null,
    "cancellation_message": null,
    "cancellation_method": null,
    "cancel_at_end_of_period": false,
    "canceled_at": null,
    "expires_at": null,
    "current_period_ends_at": "2026-10-06T00:00:00Z",
    "current_period_starts_at": "2026-09-06T00:00:00Z",
    "previous_state": null,
    "snap_day": null,
    "currency": "USD"
  }
}
```

**Key Response Fields**:
- `id` (integer): Unique subscription ID in Maxio
- `state` (string): Subscription status
  - `"active"` - Subscription is active
  - `"pending"` - Awaiting payment/activation
  - `"paused"` - Temporarily paused
  - `"canceled"` - Subscription was canceled
  - `"expired"` - Subscription expired
  - `"trial"` - In trial period
- `customer_id` (integer): Associated customer ID
- `product_id` (integer): Associated product ID
- `next_assessment_at` (ISO 8601): Next billing date/time (UTC)
- `current_period_starts_at` (ISO 8601): Current billing period start
- `current_period_ends_at` (ISO 8601): Current billing period end
- `activated_at` (ISO 8601): When subscription became active
- `canceled_at` (ISO 8601 | null): When subscription was canceled
- `test_mode` (boolean): Whether in sandbox
- `balance_in_cents` (integer): Outstanding balance in cents

### Response - Error Examples (HTTP 422 / 400)

**Customer not found:**
```json
{
  "errors": {
    "customer_id": [
      "Customer not found"
    ]
  }
}
```

**Product not found:**
```json
{
  "errors": {
    "product_id": [
      "Product not found"
    ]
  }
}
```

**Invalid state transition:**
```json
{
  "errors": {
    "base": [
      "Subscription cannot be created in requested state"
    ]
  }
}
```

---

## 4. Get Subscription

**Purpose**: Retrieve subscription details including plan, price, billing date

### HTTP Method & Endpoint

```
GET /subscriptions/{subscription_id}
```

### Full URL Example
```
https://cp-exp-4.chargify.com/api/v2/subscriptions/98765432
```

### Query Parameters (Optional)

```
GET /subscriptions/98765432?include=customer,product
```

**Optional Parameters**:
- `include` (string): Comma-separated related resources
  - Options: `customer`, `product`, `product_price_point`, `price_points`

### Required Headers
```
Authorization: Basic {base64_encoded_credentials}
Accept: application/json
```

### Response - Success (HTTP 200)

```json
{
  "subscription": {
    "id": 98765432,
    "state": "active",
    "balance_in_cents": 0,
    "total_revenue_in_cents": 29900,
    "product_id": 7126957,
    "product_handle": "eshop-pro",
    "product_name": "Pro Plan",
    "customer_id": 12345678,
    "group": null,
    "group_primary_subscription_id": null,
    "next_assessment_at": "2026-10-06T00:00:00Z",
    "state_changed_at": "2026-09-06T10:15:30Z",
    "activated_at": "2026-09-06T10:15:30Z",
    "canceled_at": null,
    "cancellation_message": null,
    "scheduled_cancellation_at": null,
    "expires_at": null,
    "current_period_starts_at": "2026-09-06T00:00:00Z",
    "current_period_ends_at": "2026-10-06T00:00:00Z",
    "previous_state": null,
    "signup_payment_id": null,
    "test_mode": true,
    "payment_collection_method": "automatic",
    "snap_day": null,
    "start_date": "2026-09-06",
    "tax_percentage": "0.00",
    "referral_code": null,
    "created_at": "2026-09-06T10:15:30Z",
    "updated_at": "2026-09-06T10:15:30Z",
    "currency": "USD",
    "me": true,
    "customer": {
      "id": 12345678,
      "first_name": "John",
      "last_name": "Doe",
      "email": "john.doe@example.com",
      "created_at": "2026-09-06T10:15:30Z",
      "updated_at": "2026-09-06T10:15:30Z"
    },
    "product": {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "description": "Professional subscription plan",
      "accounting_code": null,
      "request_credit_limit_percentage_as_string": null,
      "credit_limit_percentage": null,
      "created_at": "2024-01-15T08:00:00Z",
      "updated_at": "2024-01-15T08:00:00Z",
      "price_in_cents": 29900,
      "interval": 1,
      "interval_unit": "month",
      "initial_charge_in_cents": null,
      "trial_price_in_cents": null,
      "trial_interval": null,
      "trial_interval_unit": null,
      "require_credit_card": false,
      "require_payment_method": false,
      "taxable": false
    }
  }
}
```

**Critical Response Fields for Integration**:

| Field | Type | Description | Usage |
|-------|------|-------------|-------|
| `id` | integer | Subscription ID | Reference for updates/cancellation |
| `state` | string | Current status | Display to user (active/canceled/etc) |
| `customer_id` | integer | Associated customer | Link to customer record |
| `product_id` | integer | Associated plan/product | Display plan info |
| `product_handle` | string | Plan's unique slug | Display/reference |
| `product_name` | string | Display name of plan | Show to user |
| `next_assessment_at` | ISO 8601 | Next billing date | Show "renews on X" |
| `current_period_starts_at` | ISO 8601 | Current billing period start | Display billing cycle |
| `current_period_ends_at` | ISO 8601 | Current billing period end | Display billing cycle |
| `balance_in_cents` | integer | Outstanding balance | Show if customer owes |
| `test_mode` | boolean | Sandbox indicator | Debug/logging |
| `currency` | string | Billing currency (USD) | Display pricing |

### Response - Error (HTTP 404)

```json
{
  "errors": "Not Found: Unable to locate the requested subscription"
}
```

### List All Subscriptions for a Customer

Alternative endpoint to retrieve all subscriptions for a customer:

```
GET /subscriptions?customer_id={customer_id}
```

### Full Example with Customer Reference

```
GET /subscriptions?customer_id=12345678&include=product
```

Response returns array:
```json
{
  "subscriptions": [
    {
      "id": 98765432,
      "state": "active",
      "customer_id": 12345678,
      "product_id": 7126957,
      ...
    },
    {
      "id": 98765433,
      "state": "canceled",
      "customer_id": 12345678,
      "product_id": 7126958,
      ...
    }
  ]
}
```

---

## Implementation Patterns

### Base URL Construction
```
For subdomain-based site:
https://{MAXIO_SITE_SUBDOMAIN}.chargify.com/api/v2/

For cp-exp-4 sandbox:
https://cp-exp-4.chargify.com/api/v2/

Override (if MAXIO_BASE_URL is set):
Use it verbatim as configured
```

### Authentication Header Creation
```csharp
// Create Basic Auth header
string credentials = $"{apiKey}:x";
byte[] credentialsBytes = Encoding.UTF8.GetBytes(credentials);
string base64Credentials = Convert.ToBase64String(credentialsBytes);
string authHeader = $"Basic {base64Credentials}";

// OR use X-Chargify-Token header
string tokenHeader = $"X-Chargify-Token: {apiKey}";
```

### HTTP Client Configuration
```csharp
// Use HttpClient with default request headers
var client = new HttpClient();
client.DefaultRequestHeaders.Authorization = 
    new AuthenticationHeaderValue("Basic", base64Credentials);
client.DefaultRequestHeaders.Add("Accept", "application/json");

// OR use X-Chargify-Token
client.DefaultRequestHeaders.Add("X-Chargify-Token", apiKey);
```

### Recommended Subscription Workflow

1. **Check for existing customer** (idempotent)
   ```
   GET /customers?search={email}
   ```

2. **Create customer if not found**
   ```
   POST /customers
   ```

3. **Get available products**
   ```
   GET /products/{product_family_id}
   ```

4. **Create subscription**
   ```
   POST /subscriptions
   ```

5. **Retrieve subscription details**
   ```
   GET /subscriptions/{subscription_id}
   ```

---

## Sandbox Configuration Notes

### For Site `cp-exp-4`:

```
Product Family: eshop-subscribe (ID: 3023074)
├─ Pro Plan (eshop-pro)
│  └─ Price: $299.00/month
│  └─ ID: 7126957
├─ Basic Plan (basic-plan)
│  └─ Price: $29.00/month
│  └─ ID: 7126958
└─ Metered Component (api-call)
   └─ Price: $0.01 per unit
   └─ ID: 3057195

Configuration:
- No trial period
- No setup fee
- No expiration
- Not taxable
- Payment method NOT required (can subscribe without card)
```

### Environment Variables

```bash
MAXIO_API_KEY="{your-sandbox-api-key}"
MAXIO_SITE_SUBDOMAIN="cp-exp-4"
MAXIO_ENVIRONMENT="sandbox"
MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
MAXIO_BASE_URL=""  # Optional: override default URL construction
```

---

## References

- **Official Maxio API Docs**: https://developers.maxio.com/
- **Advanced Billing API Reference**: https://developers.maxio.com/api/v2/
- **Sandbox Testing**: Credentials provided via environment variables
- **Status Codes**: RESTful HTTP standards (200, 201, 400, 404, 422)

---

## Common Integration Points

### C# HttpClient Integration
```csharp
var request = new HttpRequestMessage(HttpMethod.Post, "/api/v2/customers");
request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64Credentials);
request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

var response = await client.SendAsync(request);
var content = await response.Content.ReadAsStringAsync();
```

### Error Handling Pattern
```
HTTP 200-201: Success - parse response.subscription or response.customer
HTTP 400: Bad request - check request format
HTTP 401: Unauthorized - verify API key and auth headers
HTTP 422: Unprocessable entity - check for validation errors in response.errors
HTTP 404: Not found - entity doesn't exist
```

---

**Document Status**: Verified against Maxio Advanced Billing API v2 documentation  
**Last Updated**: 2026-09-06
