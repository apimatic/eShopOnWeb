# Quick Start - Maxio Subscription Billing

## One-Minute Setup

1. **Get Maxio Sandbox Credentials**
   - Log into your Maxio sandbox account
   - Go to Settings → Developer API
   - Copy your API Key and Site Subdomain

2. **Set Credentials**
   ```powershell
   cd src/PublicApi
   dotnet user-secrets set "Maxio:ApiKey" "your-api-key"
   dotnet user-secrets set "Maxio:Subdomain" "your-subdomain"
   dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
   ```

3. **Run the App**
   ```powershell
   $env:DOTNET_ROLL_FORWARD="Major"
   dotnet run
   ```

4. **Test in Browser**
   - Swagger UI: https://localhost:28343/swagger/ui/index.html
   - Authenticate: POST `/api/authenticate` with `{"username":"demouser@microsoft.com","password":"Pass@password1"}`
   - Get Plans: GET `/api/subscription-plans`
   - Subscribe: POST `/api/subscriptions` with `{"planHandle":"eshop-pro"}`

## What's Implemented

✅ **Subscription Plans Endpoint**
- Lists all available plans from Maxio
- No authentication required
- Cached for 1 hour

✅ **Subscribe Endpoint**
- Requires JWT authentication
- Creates Maxio customer if not exists (idempotent)
- Creates subscription to selected plan
- Returns subscription details with dates

✅ **My Subscriptions Endpoint**
- Lists user's subscriptions
- Requires JWT authentication
- Shows plan, state, dates

✅ **Error Handling**
- Proper HTTP status codes
- User-friendly error messages
- Logging with correlation IDs

## Expected Behavior

1. **First Call to Subscribe**
   - New Maxio customer created with user's email
   - New subscription created for selected plan
   - Returns 200 with subscription details

2. **Second Call to Subscribe (Same Plan)**
   - Reuses existing customer (same email reference)
   - Creates second subscription (new reference timestamp)
   - User can have multiple subscriptions

3. **Get Plans**
   - Returns $299/month Pro Plan + $29/month Basic Plan
   - Results cached in memory

4. **Get My Subscriptions**
   - Lists all active subscriptions for logged-in user
   - Shows state (active, canceled, etc.)
   - Shows next billing date

## Troubleshooting

| Problem | Solution |
|---------|----------|
| Cannot connect to Maxio | Check API key and subdomain in secrets |
| 401 on authenticated endpoints | Get new JWT token from `/api/authenticate` |
| 422 on subscribe | Verify plan handle is "eshop-pro" or "basic-plan" |
| Empty product handle in response | Known issue - see IMPLEMENTATION_SUMMARY.md |

## Next: Full Documentation

See `MAXIO_INTEGRATION_GUIDE.md` for detailed testing with curl commands and full troubleshooting guide.

See `IMPLEMENTATION_SUMMARY.md` for architecture, limitations, and what's left to do.
