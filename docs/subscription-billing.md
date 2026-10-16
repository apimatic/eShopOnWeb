# Subscription billing verification

The PublicApi exposes a parallel subscription capability. Existing basket and checkout endpoints are unchanged. Maxio is the billing system of record; account reads fetch current provider price, state, and billing dates. One shopper can enroll once per offered plan. A repeat POST returns that enrollment, including if it was subsequently canceled. Cancellation, plan changes, usage reporting, and card capture are outside this feature.

This no-card flow uses Maxio's documented `remittance` payment collection method for recurring invoice billing. The normal plan price is assessed immediately and invoiced; a successful active enrollment does **not** mean the invoice has been paid. Both plan listing and enrollment/account responses expose `paymentCollectionMethod`. Automatic collection without a payment profile was rejected by the sandbox despite the product's payment-method-optional setting, so the integration explicitly selects invoice billing. It does not change prices or defer the first billing date. This policy targets Maxio Relationship Invoicing; a legacy Statements site that requires the distinct `invoice` collection method needs a deliberate policy change.

## Build and configure

1. Install the Maxio Advanced Billing plugin. Infrastructure references the SDK project **in the installed plugin**, without vendoring its source. On another machine, remove the SDK ProjectReference and run `dotnet add src/Infrastructure/Infrastructure.csproj reference <installed-plugin>/sdk/dotnet/src/MaxioAdvancedBilling/MaxioAdvancedBilling.csproj` to point at that installation. The SDK requires the .NET 10 compiler. `global.json` allows major SDK roll-forward.
2. In PowerShell at the repository root, import the provided sandbox environment into .NET user-secrets. This sends the values over stdin and writes them outside the repository:

   ```powershell
   foreach ($name in 'MAXIO_API_KEY','MAXIO_SITE_SUBDOMAIN','MAXIO_DEFAULT_PRODUCT_FAMILY') {
       if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) { throw "Missing $name" }
   }
   @{
       'Maxio:ApiKey' = $env:MAXIO_API_KEY
       'Maxio:Subdomain' = $env:MAXIO_SITE_SUBDOMAIN
       'Maxio:ProductFamilyHandle' = $env:MAXIO_DEFAULT_PRODUCT_FAMILY
   } | ConvertTo-Json | dotnet user-secrets set --project src/PublicApi
   $env:DOTNET_ROLL_FORWARD = 'Major'
   $env:UseOnlyInMemoryDatabase = 'true'
   dotnet build eShopOnWeb.sln
   dotnet test eShopOnWeb.sln --no-build
   dotnet dev-certs https --check
   ```

   Bindings use exactly `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and optional `Maxio:BaseUrl`. The host rejects missing/blank required values. Normal .NET configuration providers work in deployment. `Maxio__BaseUrl` can override the complete API base address verbatim, including a proxy path. No override is needed for the supplied sandbox. `MAXIO_ENVIRONMENT=EU` selects the SDK's EU region; otherwise the SDK US region is used. Neither region is a sandbox switch: the supplied site and key must belong to the sandbox. Restart after rotating credentials.

## Run detached, exercise, and stop

3. Run this PowerShell block. It starts only PublicApi, uses the assigned port block, waits for readiness, and records the exact PID for cleanup. Stop any previous instance you started before running it again.

   ```powershell
   $repoRoot = (Get-Location).Path
   $env:ASPNETCORE_ENVIRONMENT = 'Development' # Enables user-secrets.
   $env:DOTNET_ENVIRONMENT = 'Development'
   $env:UseOnlyInMemoryDatabase = 'true'
   $env:DOTNET_ROLL_FORWARD = 'Major'
   $billingPort = [int]$env:APP_PORT_BLOCK_BASE + 3
   if ([int]$env:APP_PORT_BLOCK_SIZE -lt 4) { throw 'Assigned block is too small' }
   $api = "https://localhost:$billingPort"
   $dll = Join-Path $repoRoot 'src/PublicApi/bin/Debug/net8.0/PublicApi.dll'
   $billingProcess = Start-Process dotnet -ArgumentList @(('"' + $dll + '"'), '--urls', $api) `
       -WorkingDirectory (Join-Path $repoRoot 'src/PublicApi') -WindowStyle Hidden -PassThru `
       -RedirectStandardOutput (Join-Path $env:TEMP 'eshop-subscriptions.out.log') `
       -RedirectStandardError (Join-Path $env:TEMP 'eshop-subscriptions.err.log')
   try {
       $ready = $false
       for ($attempt = 0; $attempt -lt 60; $attempt++) {
           if ($billingProcess.HasExited) { throw 'PublicApi exited; inspect the temporary logs' }
           try { Invoke-WebRequest "$api/swagger/v1/swagger.json" -TimeoutSec 2 | Out-Null; $ready = $true; break }
           catch { Start-Sleep -Milliseconds 500 }
       }
       if (!$ready) { throw 'PublicApi did not become ready' }

       # PublicApi uses JWT; the storefront cookie does not authenticate these requests.
       $login = Invoke-RestMethod "$api/api/authenticate" -Method Post -ContentType 'application/json' `
           -Body (@{ username='demouser@microsoft.com'; password='Pass@word1' } | ConvertTo-Json)
       if (!$login.result) { throw 'Authentication failed' }
       $headers = @{ Authorization = "Bearer $($login.token)" }
       $catalog = Invoke-RestMethod "$api/api/subscription-plans" -Headers $headers
       $catalog.plans | Format-Table handle,name,priceInCents,interval,intervalUnit
       if ($catalog.isPartial) { throw 'Catalog is incomplete; enrollment is unavailable' }
       # Demo preference only: choose the offered Pro handle, or the first offered plan on another catalog.
       $plan = $catalog.plans | Where-Object handle -eq 'eshop-pro' | Select-Object -First 1
       if (!$plan) { $plan = $catalog.plans | Select-Object -First 1 }
       if (!$plan) { throw 'No plans are available in the configured product family' }
       $body = @{ productHandle=$plan.handle; firstName='Demo'; lastName='Shopper' } | ConvertTo-Json
       $enrollment = Invoke-WebRequest "$api/api/subscriptions" -Headers $headers -Method Post -ContentType 'application/json' -Body $body
       $enrollment.StatusCode # 201 new, 200 replay/recovered, 202 pending reconciliation.
       $enrollment.Content | ConvertFrom-Json | ConvertTo-Json -Depth 8
       $replay = Invoke-RestMethod "$api/api/subscriptions" -Headers $headers -Method Post -ContentType 'application/json' -Body $body
       $replay | ConvertTo-Json -Depth 8 # Same subscription ID; never a second enrollment.
       Invoke-RestMethod "$api/api/my-subscriptions" -Headers $headers | ConvertTo-Json -Depth 8
   }
   finally {
       if (!$billingProcess.HasExited) { Stop-Process -Id $billingProcess.Id }
   }
   ```

4. Confirm the response contains the plan handle/name, actual `priceInCents`, provider `state`, and `nextBillingDate`. `currency` is provider-supplied; catalog prices are cents without an invented currency. `nextBillingDateSource` identifies `next_assessment_at`, or `current_period_ends_at` when used as fallback. A missing provider billing date remains null. On this seeded sandbox the Pro and Basic default catalog prices are 29900 and 2900 cents/month; the code never embeds those prices or numeric IDs.
5. Repeat with another offered handle (the seeded alternate is `basic-plan`). Authenticate as the seeded `admin@microsoft.com` account with the same sample password and call `/api/my-subscriptions`: it must not reveal the first shopper's enrollments. Without a JWT, all three subscription endpoints return 401. Requests with unrecognized identity/customer fields return 400.

## Persistence and recovery

The SQL Server migration `AddBillingClaims` adds only the billing claim table/index. Apply it through the existing CatalogContext migration workflow for durable deployments. Existing catalog seeding applies migrations on SQL Server. No new infrastructure is required. In-memory development loses local claims and users on restart; deterministic references recover known provider customers/subscriptions for the same normalized username. Retained user-ID mappings preserve the original billing reference after account rename while the database survives.

Claims are primary-key inserts before provider writes, including when EF's in-memory provider is used. POSTs are not SDK-retried. A dropped connection or unusable success response triggers a bounded reference lookup and keeps unresolved work as `Unknown`. The endpoint returns 202; repeat requests and the background sweep only read for that claim. Never delete an unknown claim to force a retry. The worker rotates unresolved claims through bounded sweeps, including claims abandoned by a process crash. If the provider never exposes the result, the claim remains pending for operational investigation rather than risking a duplicate charge. An in-memory restart cannot retain this uncertain local state; use the relational store for durable guarantees.

Foreground requests share a 30-second deadline, with up to five additional seconds for uncertain-write recovery. GETs allow one SDK retry; writes allow none. Catalog traversal caps at 50 pages of 200 and reports `isPartial`/`nextPage`; enrollment requires a complete catalog and checks family membership before any write. Logs contain operation timing, claim state, and safe IDs; request bodies and authentication headers are disabled. Public errors never echo SDK messages, URLs, or provider error bodies. Automated billing tests use HTTP stubs exclusively.

Live sandbox verification on October 16, 2026 confirmed plan discovery, JWT authentication, enrollment, replay, concurrency, and account isolation. Six concurrent fresh Basic requests produced one new subscription and five pending responses; replay returned the same subscription and the shopper account contained exactly the two enrolled plans. A second account initially had no subscriptions and independently enrolled in Pro. The provider returned active subscriptions at 2900/29900 USD cents, with November 16, 2026 next billing dates and remittance collection. Provider request logs showed one customer creation for the second account and one subscription creation per fresh shopper/plan enrollment. These are payable invoice subscriptions; verification did not record or simulate a payment.
