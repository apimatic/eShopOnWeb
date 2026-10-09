using Microsoft.Extensions.Configuration;
using Square;
using Square.Core.Authentication.OAuth2.AuthorizationCode;
using Square.Core.Configuration;
using Square.Core.ErrorResponse;
using Square.Core.Exceptions;
using Square.Requests.Merchants;
using Square.Servers;
using SquareCheck;

var appCts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    appCts.Cancel();
};

try
{
    return await RunAsync(appCts.Token);
}
catch (OperationCanceledException) when (appCts.IsCancellationRequested)
{
    Console.Error.WriteLine("Stopped.");
    return 130;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static async Task<int> RunAsync(CancellationToken ct)
{
    // Build configuration from environment variables; user-secrets can override.
    var config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Square:Environment"]     = Environment.GetEnvironmentVariable("SQUARE_ENVIRONMENT"),
            ["Square:ApplicationId"]   = Environment.GetEnvironmentVariable("SQUARE_APPLICATION_ID"),
            ["Square:ApplicationSecret"] = Environment.GetEnvironmentVariable("SQUARE_APPLICATION_SECRET"),
            ["Square:RedirectUri"]     = Environment.GetEnvironmentVariable("SQUARE_REDIRECT_URI"),
        })
        .AddUserSecrets<Program>(optional: true)
        .Build();

    var settings = SquareSettings.Bind(config);
    settings.Validate();

    var state = OAuthFlowCoordinator.GenerateState();

    using var listener = new HttpListenerRedirectReceiver(settings.RedirectUri);
    var flow = new OAuthFlowCoordinator(listener, new SystemProcessLauncher(), state);
    var promptCallback = flow.CreatePromptCallback(ct);

    var environment = string.Equals(settings.Environment, "sandbox", StringComparison.OrdinalIgnoreCase)
        ? ServerEnvironment.Sandbox
        : ServerEnvironment.Production;

    var options = new SquareClientOptions
    {
        Environment = environment,
        Oauth2 = new OAuth2AuthorizationCodeCredentials
        {
            ClientId      = settings.ApplicationId,
            ClientSecret  = settings.ApplicationSecret,
            RedirectUri   = settings.RedirectUri,
            Scope         = "MERCHANT_PROFILE_READ",
            State         = state,
            PromptForAuthorizationCode = (url, c) => promptCallback(url, c),
        },
        // Disable retries; allow up to 6 minutes per attempt so the 5-minute
        // sign-in window fits inside the SDK's per-attempt timeout.
        Retry = RetryOptions.Disabled() with { Timeout = TimeSpan.FromMinutes(6) },
    };

    var client = new SquareClient(new HttpClient(), options);

    try
    {
        // ListMerchants triggers the OAuth flow on the first call.
        var merchantsResp = await client.Merchants.ListMerchants(
            new ListMerchantsRequest(),
            cancellationToken: ct);

        var merchant = merchantsResp.Merchant?.Count > 0 ? merchantsResp.Merchant[0] : null;

        // Respond to the browser now that we have the merchant name.
        await flow.ServeSuccessAsync(
            merchant?.BusinessName ?? "your Square account",
            CancellationToken.None);

        var locationsResp = await client.Locations.ListLocations(cancellationToken: ct);

        AccountDisplay.Print(merchant, locationsResp.Locations);
        return 0;
    }
    catch (OperationCanceledException) when (flow.IsTimedOut)
    {
        Console.Error.WriteLine("Operator did not complete sign-in within 5 minutes.");
        return 2;
    }
    catch (AuthSchemeException) when (flow.IsDeclined)
    {
        Console.Error.WriteLine("Operator declined access to the Square account.");
        return 2;
    }
    catch (ApiException<RawError> ex)
    {
        Console.Error.WriteLine($"Square refused a request: {ex.Error.ReadAsString()}");
        return 1;
    }
    // OperationCanceledException from Ctrl+C propagates to the outer handler → exit 130.
}
