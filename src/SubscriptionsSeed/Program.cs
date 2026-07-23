using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.eShopWeb.SubscriptionsSeed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

// UC0 operator tooling (plan.md §4.1): verifies — and, with --seed, provisions — the product family, the
// two recurring plans and the metered component the integration is configured to use. It is deliberately
// not wired into either host; a customer never runs this.
//
//   dotnet run --project src/SubscriptionsSeed             verify only (default)
//   dotnet run --project src/SubscriptionsSeed -- --seed   create anything that is missing, then verify

var verifyOnly = !args.Contains("--seed", StringComparer.OrdinalIgnoreCase);

var configuration = new ConfigurationBuilder()
    .AddUserSecrets<Microsoft.eShopWeb.SubscriptionsSeed.SeedMarker>(optional: true)
    .AddEnvironmentVariables()
    .Build();

var settings = configuration.GetSection(MaxioSettings.SectionName).Get<MaxioSettings>() ?? new MaxioSettings();

try
{
    settings.Validate();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Configuration error: {ex.Message}");
    return 2;
}

Console.WriteLine($"Maxio target : {settings.ResolveBaseUrl()} " +
                  $"({(settings.HasExplicitBaseUrl ? "explicit Maxio:BaseUrl" : "derived from Maxio:Subdomain")})");
Console.WriteLine($"Family handle: {settings.ProductFamilyHandle}");
Console.WriteLine($"Mode         : {(verifyOnly ? "verify" : "seed + verify")}");
Console.WriteLine();

using var httpClient = new HttpClient();
var options = Options.Create(settings);
var cache = new MaxioCatalogCache();

if (!verifyOnly)
{
    var provisioner = new CatalogProvisioner(httpClient, options, new ConsoleAppLogger<CatalogProvisioner>());

    try
    {
        await provisioner.SeedAsync(CancellationToken.None);
        cache.Clear();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Seeding failed: {ex.Message}");
        return 1;
    }

    Console.WriteLine();
}

var billingClient = new MaxioBillingClient(httpClient, options, cache, new ConsoleAppLogger<MaxioBillingClient>());
var failures = new List<string>();

IReadOnlyList<BillingPlan> plans;
try
{
    plans = await billingClient.ListPlansAsync(CancellationToken.None);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Could not read the product family: {ex.Message}");
    return 1;
}

Console.WriteLine($"Plans in family '{settings.ProductFamilyHandle}': {plans.Count}");
foreach (var plan in plans)
{
    var paymentMethod = plan.RequiresPaymentMethod ? "REQUIRES payment method" : "no payment method required";
    Console.WriteLine($"  - {plan.Handle,-16} id={plan.Id,-9} {plan.PriceDisplay,-22} {paymentMethod}");
}

CheckPlan(settings.DefaultProductHandle, "default");
CheckPlan(settings.AlternateProductHandle, "alternate");

var component = await billingClient.FindComponentByHandleAsync(settings.MeteredComponentHandle, CancellationToken.None);
if (component is null)
{
    failures.Add($"Metered component '{settings.MeteredComponentHandle}' is missing from the family.");
}
else
{
    Console.WriteLine();
    Console.WriteLine($"Component '{component.Handle}': id={component.Id} kind={component.Kind} " +
                      $"scheme={component.PricingScheme} unitPrice=" +
                      $"{(component.UnitPrice.HasValue ? BillingMoney.ToDisplay(component.UnitPrice.Value) : "unpublished")}");

    if (!component.IsMetered)
    {
        failures.Add($"Component '{component.Handle}' is kind '{component.Kind}', not metered. " +
                     "It cannot be converted in place — archive it and recreate it as metered.");
    }
}

Console.WriteLine();

if (failures.Count == 0)
{
    Console.WriteLine("UC0 verified: the configured catalog exists and matches the integration's expectations.");
    return 0;
}

foreach (var failure in failures)
{
    Console.Error.WriteLine($"FAIL: {failure}");
}

Console.Error.WriteLine();
Console.Error.WriteLine("Re-run with --seed to provision the missing entities.");
return 1;

void CheckPlan(string handle, string role)
{
    if (string.IsNullOrWhiteSpace(handle))
    {
        return;
    }

    var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, handle, StringComparison.OrdinalIgnoreCase));

    if (plan is null)
    {
        failures.Add($"The {role} plan handle '{handle}' does not exist in the family.");
        return;
    }

    if (plan.RequiresPaymentMethod)
    {
        failures.Add($"Plan '{handle}' requires a payment method; the demo subscribe flow captures no card. " +
                     "Turn the requirement off on the product.");
    }
}

namespace Microsoft.eShopWeb.SubscriptionsSeed
{
    /// <summary>Anchors the user-secrets id for this tool; top-level statements have no type to point at.</summary>
    internal sealed class SeedMarker
    {
    }
}
