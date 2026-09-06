# Quick Test Reference - Maxio Subscription Integration

## One-Time Setup

```bash
# Save environment variables to user-secrets
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" "YOUR_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "YOUR_SUBDOMAIN"
dotnet user-secrets set "Maxio:Environment" "US"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
```

## Start the Server

```bash
cd src/PublicApi
$env:UseOnlyInMemoryDatabase = "true"
$env:DOTNET_ROLL_FORWARD = "Major"
dotnet run
```

Server runs on: `https://localhost:27823`

## Test Commands (PowerShell)

### 1. Authenticate
```powershell
$response = Invoke-WebRequest -Uri "https://localhost:27823/api/authenticate" `
  -Method POST `
  -Headers @{"Content-Type"="application/json"} `
  -Body '{"username":"demouser","password":"DemoUser@123"}' `
  -SkipCertificateCheck
$token = ($response.Content | ConvertFrom-Json).token
Write-Host "Token: $token"
```

### 2. List Plans
```powershell
Invoke-WebRequest -Uri "https://localhost:27823/api/subscription-plans" `
  -Headers @{"Authorization"="Bearer $token"} `
  -SkipCertificateCheck | Select-Object -ExpandProperty Content | ConvertFrom-Json
```

### 3. Subscribe (Pro Plan)
```powershell
$body = @{"productHandle"="eshop-pro"} | ConvertTo-Json
Invoke-WebRequest -Uri "https://localhost:27823/api/subscriptions" `
  -Method POST `
  -Headers @{
    "Authorization"="Bearer $token"
    "Content-Type"="application/json"
  } `
  -Body $body `
  -SkipCertificateCheck | Select-Object -ExpandProperty Content | ConvertFrom-Json
```

### 4. Subscribe Again (Test Idempotency)
```powershell
# Same command as #3
# Should return isNewSubscription: false
```

### 5. Subscribe to Different Plan (Basic)
```powershell
$body = @{"productHandle"="basic-plan"} | ConvertTo-Json
Invoke-WebRequest -Uri "https://localhost:27823/api/subscriptions" `
  -Method POST `
  -Headers @{
    "Authorization"="Bearer $token"
    "Content-Type"="application/json"
  } `
  -Body $body `
  -SkipCertificateCheck | Select-Object -ExpandProperty Content | ConvertFrom-Json
```

### 6. List My Subscriptions
```powershell
Invoke-WebRequest -Uri "https://localhost:27823/api/my-subscriptions" `
  -Headers @{"Authorization"="Bearer $token"} `
  -SkipCertificateCheck | Select-Object -ExpandProperty Content | ConvertFrom-Json
```

## Test Commands (curl / bash)

### 1. Authenticate
```bash
TOKEN=$(curl -s -k -X POST https://localhost:27823/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser","password":"DemoUser@123"}' \
  | jq -r '.token')
echo "Token: $TOKEN"
```

### 2. List Plans
```bash
curl -s -k -H "Authorization: Bearer $TOKEN" \
  https://localhost:27823/api/subscription-plans | jq
```

### 3. Subscribe to Pro Plan
```bash
curl -s -k -X POST https://localhost:27823/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}' | jq
```

### 4. Test Idempotency (Subscribe Again)
```bash
curl -s -k -X POST https://localhost:27823/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}' | jq '.isNewSubscription'
# Should output: false
```

### 5. Subscribe to Basic Plan
```bash
curl -s -k -X POST https://localhost:27823/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"basic-plan"}' | jq
```

### 6. List User Subscriptions
```bash
curl -s -k -H "Authorization: Bearer $TOKEN" \
  https://localhost:27823/api/my-subscriptions | jq '.subscriptions | length'
# Should output: 2 (pro and basic)
```

## Expected Response Examples

### List Plans Response
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "description": "...",
      "priceInCents": 29900,
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

### Create Subscription Response (First Time)
```json
{
  "subscription": {
    "id": 123456,
    "customerId": 789,
    "productId": 7126957,
    "productHandle": "eshop-pro",
    "state": "active",
    "balanceInCents": 0,
    "currentPeriodStartsAt": "2026-09-07T00:00:00Z",
    "currentPeriodEndsAt": "2026-10-07T00:00:00Z",
    "nextAssessmentAt": "2026-10-07T00:00:00Z",
    "activatedAt": "2026-09-07T00:00:00Z"
  },
  "isNewSubscription": true
}
```

### Create Subscription Response (Duplicate - Idempotent)
```json
{
  "subscription": {
    "id": 123456,
    ...
  },
  "isNewSubscription": false
}
```

### List My Subscriptions Response
```json
{
  "subscriptions": [
    {
      "id": 123456,
      "customerId": 789,
      "productId": 7126957,
      "productHandle": "eshop-pro",
      "state": "active",
      ...
    },
    {
      "id": 123457,
      "customerId": 789,
      "productId": 7126958,
      "productHandle": "basic-plan",
      "state": "active",
      ...
    }
  ]
}
```

## Error Cases to Test

### Missing Token
```bash
curl -k https://localhost:27823/api/subscription-plans
# Expected: 401 Unauthorized
```

### Invalid Token
```bash
curl -k -H "Authorization: Bearer invalid" \
  https://localhost:27823/api/subscription-plans
# Expected: 401 Unauthorized
```

### Invalid Product Handle
```bash
curl -s -k -X POST https://localhost:27823/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"nonexistent"}' | jq
# Expected: 400 Bad Request or 422 Unprocessable Entity
```

## Swagger UI

Open: `https://localhost:27823/swagger/index.html`
- View all endpoints
- Try endpoints directly from browser
- See request/response schemas
- Copy curl commands from browser dev tools

## Debugging

### Check User Secrets
```bash
cd src/PublicApi
dotnet user-secrets list
```

### Clear User Secrets
```bash
cd src/PublicApi
dotnet user-secrets clear
```

### Enable Debug Logging
Add to Program.cs before `var app = builder.Build();`:
```csharp
builder.Logging.SetMinimumLevel(LogLevel.Debug);
```

### Check Server is Running
```bash
curl -k https://localhost:27823/swagger/index.html
# Should return HTML
```
