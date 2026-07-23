using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.SubscriptionsSeed;
using Microsoft.Extensions.Configuration;

// UC0 operator tooling: provisions and verifies the Maxio entities the subscription module is
// configured against. Not wired into either host — a developer runs it once per sandbox.
//
//   dotnet run --project src/SubscriptionsSeed                 seed anything missing, then verify
//   dotnet run --project src/SubscriptionsSeed -- --verify-only  report only, change nothing

var verifyOnly = args.Any(a => string.Equals(a, "--verify-only", StringComparison.OrdinalIgnoreCase));

var configuration = new ConfigurationBuilder()
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args.Where(a => !a.StartsWith("--verify-only", StringComparison.OrdinalIgnoreCase)).ToArray())
    .Build();

var settings = configuration.GetSection(MaxioSettings.ConfigurationSectionName).Get<MaxioSettings>() ?? new MaxioSettings();

if (string.IsNullOrWhiteSpace(settings.ApiKey))
{
    Console.Error.WriteLine(
        "Maxio:ApiKey is not configured. Set it with:\n" +
        "  dotnet user-secrets set \"Maxio:ApiKey\" \"<key>\" --project src/SubscriptionsSeed");
    return 2;
}

var specification = new SeedSpecification(
    FamilyName: "eShopSubscribe",
    FamilyHandle: Fallback(settings.ProductFamilyHandle, "eshop-subscribe"),
    DefaultPlan: new PlanSpecification(
        Name: "Pro Plan",
        Handle: Fallback(settings.DefaultProductHandle, "eshop-pro"),
        Description: "eShopOnWeb Subscribe — Pro.",
        PriceInCents: 29_900,
        Interval: 1,
        IntervalUnit: "month"),
    AlternatePlan: new PlanSpecification(
        Name: "Basic Plan",
        Handle: Fallback(settings.AlternateProductHandle, "basic-plan"),
        Description: "eShopOnWeb Subscribe — Basic.",
        PriceInCents: 2_900,
        Interval: 1,
        IntervalUnit: "month"),
    MeteredComponent: new ComponentSpecification(
        Name: "API Calls",
        Handle: Fallback(settings.MeteredComponentHandle, "api-call"),
        UnitName: "call",
        UnitPrice: "0.01"));

string baseUrl;
try
{
    baseUrl = settings.ResolveBaseUrl();
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

Console.WriteLine($"Maxio target: {baseUrl} (region {(settings.IsEuRegion ? "EU" : "US")}){(verifyOnly ? " — verify only" : string.Empty)}");
Console.WriteLine();

var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey, Password = "x" },
    Environment = settings.IsEuRegion ? ServerEnvironment.Eu : ServerEnvironment.Us
};

if (settings.IsEuRegion)
{
    options.Server.Production.Eu.BaseUrl = baseUrl;
}
else
{
    options.Server.Production.Us.BaseUrl = baseUrl;
}

using var httpClient = new HttpClient();
var client = new MaxioAdvancedBillingClient(httpClient, options);

using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));

try
{
    var seeder = new MaxioSeeder(client, specification, verifyOnly);
    var succeeded = await seeder.RunAsync(cancellation.Token);

    return succeeded ? 0 : 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"UC0 failed: {ex.Message}");
    return 2;
}

static string Fallback(string? configured, string fallback) =>
    string.IsNullOrWhiteSpace(configured) ? fallback : configured;

/// <summary>Anchor type for <c>AddUserSecrets</c>.</summary>
public partial class Program { }
