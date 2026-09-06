# Maxio Advanced Billing API - Quick Reference

## Base URL
```
https://{MAXIO_SITE_SUBDOMAIN}.chargify.com/api/v2/
```

## Authentication
```
Authorization: Basic {base64(api_key:x)}
-OR-
X-Chargify-Token: {api_key}
```

---

## API Operations Summary

### 1. CREATE CUSTOMER (Idempotent)

**Endpoint**: `POST /customers`

**Required Body**:
```json
{
  "customer": {
    "first_name": "John",
    "last_name": "Doe",
    "email": "john@example.com"
  }
}
```

**Optional Fields**: phone, organization_name, reference, address, city, state, zip, country_code, vat_number

**Returns**: HTTP 201
```json
{
  "customer": {
    "id": 12345678,
    "first_name": "John",
    "last_name": "Doe",
    "email": "john@example.com",
    "created_at": "2026-09-06T10:15:30Z",
    "updated_at": "2026-09-06T10:15:30Z"
  }
}
```

**Key Fields**: `id` (use for subscription creation)

**Idempotency**: Query `GET /customers?search={email}` first, create only if not found

---

### 2. LIST PRODUCTS/PLANS

**Endpoint**: `GET /products/{product_family_id}`

**For cp-exp-4**: `GET /products/3023074`

**Query Params**: include, per_page, page

**Returns**: HTTP 200
```json
{
  "products": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "price_in_cents": 29900,
      "interval": 1,
      "interval_unit": "month",
      "require_payment_method": false,
      "taxable": false
    },
    {
      "id": 7126958,
      "name": "Basic Plan",
      "handle": "basic-plan",
      "price_in_cents": 2900,
      "interval": 1,
      "interval_unit": "month"
    }
  ]
}
```

**Key Fields**: `id`, `name`, `handle`, `price_in_cents`

---

### 3. CREATE SUBSCRIPTION

**Endpoint**: `POST /subscriptions`

**Required Body**:
```json
{
  "subscription": {
    "customer_id": 12345678,
    "product_id": 7126957,
    "payment_collection_method": "automatic"
  }
}
```

**Alternative**: Use `product_handle: "eshop-pro"` instead of product_id

**Optional**: coupon_codes, components, custom_price, metadata, reference, notes

**Returns**: HTTP 201
```json
{
  "subscription": {
    "id": 98765432,
    "state": "active",
    "customer_id": 12345678,
    "product_id": 7126957,
    "product_handle": "eshop-pro",
    "next_assessment_at": "2026-10-06T00:00:00Z",
    "current_period_starts_at": "2026-09-06T00:00:00Z",
    "current_period_ends_at": "2026-10-06T00:00:00Z",
    "created_at": "2026-09-06T10:15:30Z"
  }
}
```

**Key Fields**: `id`, `state`, `next_assessment_at` (next billing date)

---

### 4. GET SUBSCRIPTION

**Endpoint**: `GET /subscriptions/{subscription_id}`

**Query Params**: include (customer, product, etc.)

**Returns**: HTTP 200
```json
{
  "subscription": {
    "id": 98765432,
    "state": "active",
    "customer_id": 12345678,
    "product_id": 7126957,
    "product_handle": "eshop-pro",
    "product_name": "Pro Plan",
    "next_assessment_at": "2026-10-06T00:00:00Z",
    "current_period_starts_at": "2026-09-06T00:00:00Z",
    "current_period_ends_at": "2026-10-06T00:00:00Z",
    "balance_in_cents": 0,
    "state_changed_at": "2026-09-06T10:15:30Z",
    "created_at": "2026-09-06T10:15:30Z"
  }
}
```

**Key Fields**: `state`, `next_assessment_at`, `balance_in_cents`, `product_name`

---

## Integration Checklist

- [ ] Read `MAXIO_API_KEY` from environment
- [ ] Read `MAXIO_SITE_SUBDOMAIN` from environment (e.g., "cp-exp-4")
- [ ] Construct base URL: `https://{subdomain}.chargify.com/api/v2/`
- [ ] Create Basic Auth header from API key
- [ ] Check for existing customer before creating (email search)
- [ ] Fetch product family's products list
- [ ] Create subscription with customer_id + product_id
- [ ] Retrieve subscription to confirm state and next billing date
- [ ] Display plan name, price, and next billing date to user

---

## Sandbox Data (cp-exp-4)

| Entity | Handle | ID | Notes |
|--------|--------|----|----|
| Product Family | eshop-subscribe | 3023074 | Container |
| Pro Plan | eshop-pro | 7126957 | $299/mo |
| Basic Plan | basic-plan | 7126958 | $29/mo |
| Metered Component | api-call | 3057195 | $0.01/unit |

**Key Feature**: `require_payment_method: false` - subscribe without card

---

## Error Responses

| HTTP Status | Meaning | Common Field |
|-------------|---------|--------------|
| 201 | Created successfully | Look in response body |
| 200 | Retrieved successfully | Look in response body |
| 400 | Bad request | Check request format |
| 401 | Unauthorized | Verify API key & auth header |
| 404 | Not found | Entity doesn't exist |
| 422 | Validation failed | Check response.errors |

---

## Subscription States

| State | Meaning |
|-------|---------|
| `active` | Subscription is current |
| `pending` | Awaiting activation/payment |
| `paused` | Temporarily suspended |
| `canceled` | Subscription was canceled |
| `expired` | Subscription term expired |
| `trial` | In trial period |

---

## Useful Queries

**Find customer by email**:
```
GET /customers?search=john@example.com
```

**List all subscriptions for customer**:
```
GET /subscriptions?customer_id=12345678&include=product
```

**Get product by handle**:
```
GET /products/handle/eshop-pro
```

---

## Required Headers (All Requests)

```
Authorization: Basic {base64_credentials}
Content-Type: application/json (for POST/PUT)
Accept: application/json
```

---

See `MAXIO-API-RESEARCH.md` for detailed documentation.
