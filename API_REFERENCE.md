# Subscription Billing API Reference

## Base URL

```
https://localhost:28283/api
```

## Authentication

All endpoints require JWT Bearer token authentication.

**Header:**
```
Authorization: Bearer <JWT_TOKEN>
```

**Obtaining a Token:**

```http
POST /api/authenticate
Content-Type: application/json

{
  "username": "user@example.com",
  "password": "password"
}
```

**Response:**
```json
{
  "result": true,
  "username": "user@example.com",
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
}
```

---

## Endpoints

### 1. List Subscription Plans

Lists all available subscription plans from the configured product family.

**Request:**
```http
GET /subscription-plans
Authorization: Bearer <token>
```

**Response (200 OK):**
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "description": "Professional tier with advanced features",
      "priceInDollars": 299.00,
      "intervalDays": 1,
      "intervalUnit": "month"
    },
    {
      "id": 7126958,
      "name": "Basic Plan",
      "handle": "basic-plan",
      "description": "Basic tier for getting started",
      "priceInDollars": 29.00,
      "intervalDays": 1,
      "intervalUnit": "month"
    }
  ]
}
```

**Error Responses:**

- `401 Unauthorized` - Missing or invalid JWT token
- `400 Bad Request` - Failed to fetch plans from Maxio

**Notes:**
- Plans are filtered by product family handle (configured as `Maxio:ProductFamilyHandle`)
- Prices are returned in dollars (Maxio stores as cents, divided by 100)

---

### 2. Create Subscription

Creates a new subscription for the authenticated user to a specified plan.

**Request:**
```http
POST /subscriptions
Authorization: Bearer <token>
Content-Type: application/json

{
  "productHandle": "eshop-pro"
}
```

**Response (201 Created):**
```json
{
  "subscription": {
    "id": 15236915,
    "state": "active",
    "productName": "Pro Plan",
    "productHandle": "eshop-pro",
    "productPriceInDollars": 299.00,
    "currentPeriodEndsAt": "2024-10-15T14:48:10Z",
    "nextAssessmentAt": "2024-10-15T14:48:10Z",
    "activatedAt": "2024-09-15T14:48:10Z",
    "createdAt": "2024-09-15T14:48:10Z",
    "updatedAt": "2024-09-15T14:48:10Z"
  }
}
```

**Error Responses:**

- `401 Unauthorized` - Missing or invalid JWT token
- `404 Not Found` - User not found
- `400 Bad Request` - Failed to create customer or subscription

**Behavior:**

1. **Idempotent Customer Creation**: 
   - Uses the authenticated user's ID as the customer reference in Maxio
   - If a customer with that reference already exists, reuses it
   - Prevents duplicate customers for the same eShopOnWeb user

2. **Subscription State**:
   - New subscriptions are created in "active" state
   - Next billing date is set based on the plan's billing interval
   - Payment collection method is "remittance" (no payment method required)

**Request Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| productHandle | string | Yes | Handle of the plan to subscribe to (e.g., "eshop-pro") |

---

### 3. List User Subscriptions

Lists all subscriptions for the authenticated user.

**Request:**
```http
GET /my-subscriptions
Authorization: Bearer <token>
```

**Response (200 OK):**
```json
{
  "subscriptions": [
    {
      "id": 15236915,
      "state": "active",
      "productName": "Pro Plan",
      "productHandle": "eshop-pro",
      "productPriceInDollars": 299.00,
      "currentPeriodEndsAt": "2024-10-15T14:48:10Z",
      "nextAssessmentAt": "2024-10-15T14:48:10Z",
      "activatedAt": "2024-09-15T14:48:10Z",
      "createdAt": "2024-09-15T14:48:10Z",
      "updatedAt": "2024-09-15T14:48:10Z"
    },
    {
      "id": 15236916,
      "state": "canceled",
      "productName": "Basic Plan",
      "productHandle": "basic-plan",
      "productPriceInDollars": 29.00,
      "currentPeriodEndsAt": "2024-09-20T14:48:10Z",
      "nextAssessmentAt": null,
      "activatedAt": "2024-09-01T14:48:10Z",
      "createdAt": "2024-09-01T14:48:10Z",
      "updatedAt": "2024-09-15T14:48:10Z"
    }
  ]
}
```

**Response (200 OK - No Subscriptions):**
```json
{
  "subscriptions": []
}
```

**Error Responses:**

- `401 Unauthorized` - Missing or invalid JWT token
- `404 Not Found` - User not found

**Notes:**

- Returns all subscriptions (active and inactive) for the user
- Uses the user's ID to look up their Maxio customer
- If no Maxio customer exists for the user, returns empty list
- Results are not paginated (assumes reasonable subscription count per user)

---

## Data Models

### SubscriptionPlanDto

Represents an available subscription plan.

```csharp
public class SubscriptionPlanDto
{
    public int Id { get; set; }                          // Maxio product ID
    public string Name { get; set; }                     // Display name
    public string Handle { get; set; }                   // Unique handle for API
    public string Description { get; set; }              // Plan description
    public decimal PriceInDollars { get; set; }          // Monthly price
    public int IntervalDays { get; set; }                // Billing interval (days)
    public string IntervalUnit { get; set; }             // Interval unit ("month", "year", etc.)
}
```

### SubscriptionDto

Represents an active subscription.

```csharp
public class SubscriptionDto
{
    public int Id { get; set; }                          // Maxio subscription ID
    public string State { get; set; }                    // "active", "canceled", "past_due", etc.
    public string ProductName { get; set; }              // Plan name
    public string ProductHandle { get; set; }            // Plan handle
    public decimal ProductPriceInDollars { get; set; }   // Current price
    public DateTime CurrentPeriodEndsAt { get; set; }    // Next billing date
    public DateTime NextAssessmentAt { get; set; }       // Next charge date
    public DateTime ActivatedAt { get; set; }            // When subscription started
    public DateTime CreatedAt { get; set; }              // When created in Maxio
    public DateTime UpdatedAt { get; set; }              // When last updated
}
```

---

## Integration with Maxio

All endpoints communicate with the Maxio Advanced Billing API using the officially published OpenAPI specification.

### Maxio Configuration

The integration requires the following configuration (from environment or user-secrets):

| Key | Source | Required | Description |
|-----|--------|----------|-------------|
| Maxio:ApiKey | Environment or User-Secrets | Yes | API authentication key |
| Maxio:Subdomain | Environment or User-Secrets | Yes | Maxio site subdomain |
| Maxio:ProductFamilyHandle | appsettings.json | Yes | Product family to filter plans |
| Maxio:BaseUrl | appsettings.json (optional) | No | Override for custom API base URL |

### Authentication with Maxio

- Uses HTTP Basic Authentication with API key as username and "x" as password
- All requests are made to `https://{subdomain}.chargify.com`
- Responses are parsed as JSON

### Customer Reference Mapping

- Each eShopOnWeb user maps to a Maxio customer via the user's ID as reference
- This ensures idempotent operations - the same user always maps to the same customer
- Customer lookup is performed before subscription creation to support existing customers

---

## Error Handling

### Error Response Format

```json
{
  "error": "Description of the error"
}
```

### Common Errors

| Status | Error | Cause | Solution |
|--------|-------|-------|----------|
| 401 | Unauthorized | Invalid or missing JWT token | Obtain a new token via `/api/authenticate` |
| 404 | User not found | User ID doesn't exist in system | Verify user is logged in with valid account |
| 400 | Failed to create customer | Maxio API error | Check API key and network connectivity |
| 400 | Failed to create subscription | Plan handle invalid or other Maxio error | Verify plan handle matches available plans |

---

## Webhook Support (Future)

Maxio sends webhooks for subscription events. Future implementation should handle:

- `subscription_state_change` - When subscription state changes
- `subscription_product_change` - When plan is changed
- `invoice_created` - When invoice is generated
- `payment_success` / `payment_failure` - Payment updates

These should be added to a `/api/webhooks/maxio` endpoint to keep local database in sync.

---

## Rate Limiting

Currently no rate limiting is implemented. For production:

- Consider implementing rate limiting per user
- Monitor Maxio API quota usage
- Implement exponential backoff for failed requests

---

## Examples

### PowerShell Example

```powershell
# Get token
$auth = @{
    username = "user@example.com"
    password = "password"
} | ConvertTo-Json

$token = (Invoke-WebRequest -Uri "https://localhost:28283/api/authenticate" `
    -Method POST -ContentType "application/json" -Body $auth `
    -SkipCertificateCheck).Content | ConvertFrom-Json | Select-Object -ExpandProperty token

$headers = @{ Authorization = "Bearer $token" }

# List plans
Invoke-WebRequest -Uri "https://localhost:28283/api/subscription-plans" `
    -Headers $headers -SkipCertificateCheck | ConvertFrom-Json

# Create subscription
$sub = @{ productHandle = "eshop-pro" } | ConvertTo-Json
Invoke-WebRequest -Uri "https://localhost:28283/api/subscriptions" `
    -Method POST -Headers $headers -Body $sub `
    -ContentType "application/json" -SkipCertificateCheck | ConvertFrom-Json

# List subscriptions
Invoke-WebRequest -Uri "https://localhost:28283/api/my-subscriptions" `
    -Headers $headers -SkipCertificateCheck | ConvertFrom-Json
```

### cURL Example

```bash
# Get token
TOKEN=$(curl -s -X POST https://localhost:28283/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"user@example.com","password":"password"}' \
  -k | jq -r '.token')

# List plans
curl -H "Authorization: Bearer $TOKEN" \
  https://localhost:28283/api/subscription-plans -k

# Create subscription
curl -X POST https://localhost:28283/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}' \
  -k

# List user subscriptions
curl -H "Authorization: Bearer $TOKEN" \
  https://localhost:28283/api/my-subscriptions -k
```
