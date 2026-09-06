# Test script for subscription endpoints
param(
    [int]$TimeoutSeconds = 30
)

$env:DOTNET_ROLL_FORWARD = "Major"
$env:UseOnlyInMemoryDatabase = "true"

# Disable SSL verification for localhost testing
if (-not ("System.Net.ServicePointManager" -as [type])) { Add-Type -AssemblyName System.Net.Http }
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }

Write-Host "Starting PublicApi application..."
$process = Start-Process -NoNewWindow -PassThru -FilePath "dotnet" `
    -ArgumentList @("run", "--project", "src/PublicApi/PublicApi.csproj", "--no-build") `
    -ErrorAction SilentlyContinue

$processId = $process.Id
Write-Host "Process started with PID: $processId"

# Wait for startup
Start-Sleep -Seconds 5

$baseUrl = "https://localhost:25123"

try {
    # Test authentication
    Write-Host "`n=== Testing Authentication ==="
    $authUrl = "$baseUrl/api/authenticate"
    $authBody = @{
        username = "demouser@microsoft.com"
        password = "Pass@word1"
    } | ConvertTo-Json

    $authResponse = Invoke-RestMethod -Uri $authUrl -Method Post -Body $authBody `
        -ContentType "application/json" -SkipCertificateCheck -ErrorAction Stop
    $token = $authResponse.token
    Write-Host "✓ Authentication successful"
    Write-Host "  Token preview: $($token.Substring(0, 30))..."

    # Test get subscription plans
    Write-Host "`n=== Testing Get Subscription Plans ==="
    $plansUrl = "$baseUrl/api/subscription-plans"
    $plansResponse = Invoke-RestMethod -Uri $plansUrl -Method Get `
        -Headers @{ "Authorization" = "Bearer $token" } -SkipCertificateCheck -ErrorAction Stop
    Write-Host "✓ Get plans successful"
    Write-Host "  Plans found: $($plansResponse.plans.Count)"
    if ($plansResponse.plans.Count -gt 0) {
        foreach ($plan in $plansResponse.plans) {
            Write-Host "    - $($plan.name) (Handle: $($plan.handle), Price: $($plan.price))"
        }
    }

    # Test create subscription
    Write-Host "`n=== Testing Create Subscription ==="
    $subUrl = "$baseUrl/api/subscriptions"
    $subBody = @{
        productHandle = "basic-plan"
    } | ConvertTo-Json

    $subResponse = Invoke-RestMethod -Uri $subUrl -Method Post -Body $subBody `
        -ContentType "application/json" -Headers @{ "Authorization" = "Bearer $token" } `
        -SkipCertificateCheck -ErrorAction Stop
    Write-Host "✓ Create subscription successful"
    Write-Host "  Subscription ID: $($subResponse.subscriptionId)"
    Write-Host "  Status: $($subResponse.status)"
    Write-Host "  Product: $($subResponse.productName)"
    if ($subResponse.nextBillingDate) {
        Write-Host "  Next Billing: $($subResponse.nextBillingDate)"
    }

    # Test get user subscriptions
    Write-Host "`n=== Testing Get User Subscriptions ==="
    $mySubsUrl = "$baseUrl/api/my-subscriptions"
    $mySubsResponse = Invoke-RestMethod -Uri $mySubsUrl -Method Get `
        -Headers @{ "Authorization" = "Bearer $token" } -SkipCertificateCheck -ErrorAction Stop
    Write-Host "✓ Get subscriptions successful"
    Write-Host "  Subscriptions found: $($mySubsResponse.subscriptions.Count)"
    foreach ($sub in $mySubsResponse.subscriptions) {
        Write-Host "    - $($sub.productName) (Status: $($sub.status))"
        if ($sub.nextBillingDate) {
            Write-Host "      Next Billing: $($sub.nextBillingDate)"
        }
    }

    Write-Host "`n✓ All tests passed!"
}
catch {
    Write-Host "`n✗ Test failed: $($_.Exception.Message)"
    Write-Host $_.Exception.InnerException
    exit 1
}
finally {
    Write-Host "`nStopping application (PID: $processId)..."
    if ($process -and -not $process.HasExited) {
        $process | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 500
    }
    Write-Host "Done"
}
