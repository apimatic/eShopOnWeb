using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.SubscriptionsSeed;
using MaxioServers = MaxioAdvancedBilling.Servers;

// UC0 — provisions the billing-provider entities every other use case depends on (plan.md UC0).
//
// This is operator tooling and is deliberately NOT wired into the Web or PublicApi hosts. Run it once
// per sandbox, or after the sandbox has been wiped:
//
//     dotnet run --project src/SubscriptionsSeed
//
// It reads the same configuration the application does — .NET user-secrets or environment variables —
// so no credential is ever written into the repository.

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    var settings = SeedConfiguration.Load();

    Console.WriteLine("eShopOnWeb Subscribe — billing sandbox seed (UC0)");
    Console.WriteLine($"  target      {settings.ResolveBaseUrl()}");
    Console.WriteLine($"  region      {settings.Environment}");
    Console.WriteLine($"  family      {settings.ProductFamilyHandle}");
    Console.WriteLine($"  plans       {settings.DefaultProductHandle}, {settings.AlternateProductHandle}");
    Console.WriteLine($"  usage       {settings.MeteredComponentHandle}");
    Console.WriteLine();

    using var httpClient = new HttpClient();
    var client = new MaxioAdvancedBillingClient(httpClient, BuildOptions(settings));

    var seeder = new CatalogSeeder(client, settings);
    var healthy = await seeder.RunAsync(cancellation.Token);

    Console.WriteLine();
    if (healthy)
    {
        Console.WriteLine("Seed verified. The integration's configured handles all resolve.");
        return 0;
    }

    Console.Error.WriteLine("Seed incomplete — see the problems reported above.");
    return 1;
}
catch (BillingConfigurationException ex)
{
    Console.Error.WriteLine($"Configuration error: {ex.Message}");
    return 2;
}
catch (SeedException ex)
{
    Console.Error.WriteLine($"Seed error: {ex.Message}");
    return 3;
}
catch (SdkException<RawError> ex)
{
    Console.Error.WriteLine(
        $"The billing provider rejected a seeding request " +
        $"(HTTP {(int)ex.Error.StatusCode}): {ex.Error.ReadAsString()}");
    return 4;
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine(
        $"The billing provider could not be reached: {ex.Message} " +
        "Check Maxio:BaseUrl / Maxio:Subdomain and your network access.");
    return 5;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");
    return 130;
}

static MaxioAdvancedBillingClientOptions BuildOptions(MaxioSettings settings)
{
    var options = new MaxioAdvancedBillingClientOptions
    {
        BasicAuth = new BasicAuthCredentials
        {
            // Maxio authenticates the site API key as the Basic username; the password is a fixed literal.
            Username = settings.ApiKey,
            Password = "x"
        },
        Environment = settings.IsEuropeanRegion
            ? MaxioServers.ServerEnvironment.Eu
            : MaxioServers.ServerEnvironment.Us
    };

    var subdomain = settings.Subdomain?.Trim() ?? string.Empty;
    options.Server.Production.Us.Site = subdomain;
    options.Server.Production.Eu.Site = subdomain;

    // Honors an explicit Maxio:BaseUrl exactly as the running integration does, so the seeder can be
    // pointed at the same target the application is (plan.md §2.3).
    var baseUrl = settings.ResolveBaseUrl();
    options.Server.Production.Us.BaseUrl = baseUrl;
    options.Server.Production.Eu.BaseUrl = baseUrl;

    return options;
}
