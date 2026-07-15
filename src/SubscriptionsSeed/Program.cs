// UC0 (plan.md) — operator tooling that provisions/verifies the Maxio Advanced Billing
// sandbox entities eShopOnWeb Subscribe depends on. Run once per sandbox (or after a
// reseed). NOT wired into the Web or PublicApi hosts — this is a standalone console tool,
// invoked manually: `dotnet run --project src/SubscriptionsSeed`.
//
// Credentials are read from environment variables only (never hardcoded, never written to
// a file) — see plan.md §0 bottleneck #4 and §5.

using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;

const string FamilyName = "eShopSubscribe";
const string ProPlanName = "Pro Plan";
const string BasicPlanName = "Basic Plan";
const string MeteredComponentName = "API Calls";

var apiKey = Environment.GetEnvironmentVariable("MAXIO_API_KEY");
var subdomain = Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN");
var region = Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT") ?? "US";
var familyHandle = Environment.GetEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY") ?? "eshop-subscribe";
const string proPlanHandle = "eshop-pro";
const string basicPlanHandle = "basic-plan";
const string meteredComponentHandle = "api-call";

if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(subdomain))
{
    Console.Error.WriteLine("MAXIO_API_KEY and MAXIO_SITE_SUBDOMAIN must be set in the environment. Aborting.");
    return 1;
}

var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials { Username = apiKey, Password = "x" },
    Environment = string.Equals(region, "EU", StringComparison.OrdinalIgnoreCase) ? ServerEnvironment.Eu : ServerEnvironment.Us
};
if (options.Environment == ServerEnvironment.Eu)
{
    options.Server.Production.Eu.Site = subdomain;
}
else
{
    options.Server.Production.Us.Site = subdomain;
}

using var httpClient = new HttpClient();
var client = new MaxioAdvancedBillingClient(httpClient, options);

Console.WriteLine($"Verifying Maxio sandbox '{subdomain}' ({options.Environment.Value})...");
Console.WriteLine();

// --- Site-level payment collection default ---
// UC1's demo path assumes signup needs no card (plan.md's "requires payment method off").
// That toggle covers the *product*; whether a subscription is actually invoiced or charged
// immediately is the *site's* payment collection method (or an explicit override per
// subscription — see MaxioBillingClient.CreateSubscriptionAsync). Surface it so an operator
// knows why a signup might demand a card despite the product-level toggle being off.
try
{
    var site = await client.Sites.ReadSite();
    Console.WriteLine($"Site: relationshipInvoicingEnabled={site.Site?.RelationshipInvoicingEnabled} defaultPaymentCollectionMethod={site.Site?.DefaultPaymentCollectionMethod}");
    Console.WriteLine();
}
catch (SdkException<RawError> ex)
{
    Console.Error.WriteLine($"Unable to read site info: {ex.Error.ReadAsString()}");
}

// --- Product family ---
var familyId = await FindProductFamilyIdAsync(client, familyHandle);
if (familyId == null)
{
    Console.WriteLine($"Product family '{familyHandle}' not found — creating it.");
    var created = await client.ProductFamilies.CreateProductFamily(new CreateProductFamilyRequest
    {
        ProductFamily = new CreateProductFamily { Name = FamilyName, Handle = familyHandle }
    });
    familyId = (int?)created.ProductFamily?.Id;
}

if (familyId == null)
{
    Console.Error.WriteLine($"Could not resolve or create product family '{familyHandle}'.");
    return 1;
}

Console.WriteLine($"Product family: handle='{familyHandle}' id={familyId}");

// --- Plans ---
await EnsureProductAsync(client, familyId.Value, proPlanHandle, ProPlanName, priceInCents: 29900);
await EnsureProductAsync(client, familyId.Value, basicPlanHandle, BasicPlanName, priceInCents: 2900);

// --- Metered component ---
await EnsureMeteredComponentAsync(client, familyId.Value, meteredComponentHandle, MeteredComponentName);

Console.WriteLine();
Console.WriteLine("UC0 verification complete.");
return 0;

static async Task<int?> FindProductFamilyIdAsync(MaxioAdvancedBillingClient client, string handle)
{
    IReadOnlyList<ProductFamilyResponse> families;
    try
    {
        families = await client.ProductFamilies.ListProductFamilies(null, null, null, null, null);
    }
    catch (SdkException<RawError> ex)
    {
        Console.Error.WriteLine($"Unable to list product families: {ex.Error.ReadAsString()}");
        return null;
    }

    var match = families.FirstOrDefault(f => string.Equals(f.ProductFamily?.Handle, handle, StringComparison.OrdinalIgnoreCase));
    return (int?)match?.ProductFamily?.Id;
}

static async Task EnsureProductAsync(MaxioAdvancedBillingClient client, int familyId, string handle, string name, long priceInCents)
{
    Product? product = null;
    try
    {
        var response = await client.Products.ReadProductByHandle(handle);
        product = response.Product;
    }
    catch (SdkException<RawError>)
    {
        // Not found — created below.
    }

    if (product == null)
    {
        Console.WriteLine($"Product '{handle}' not found — creating it (${priceInCents / 100m:N2}/month, no trial, no setup fee, requires payment method off).");
        var created = await client.Products.CreateProduct(familyId.ToString(), new CreateOrUpdateProductRequest
        {
            Product = new CreateOrUpdateProduct
            {
                Name = name,
                Handle = handle,
                Description = name,
                RequireCreditCard = false,
                PriceInCents = priceInCents,
                Interval = 1,
                IntervalUnit = IntervalUnit.Month
            }
        });
        product = created.Product;
    }

    if (product == null)
    {
        Console.Error.WriteLine($"Could not resolve or create product '{handle}'.");
        return;
    }

    Console.WriteLine($"Product: handle='{handle}' id={product.Id} price={(product.PriceInCents ?? 0) / 100m:N2} requireCreditCard={product.RequireCreditCard} taxable={product.Taxable}");

    if (product.RequireCreditCard == true)
    {
        Console.WriteLine($"  WARNING: '{handle}' requires a payment method — UC1's demo path expects this OFF (see UC0 failure scenarios in plan.md).");
    }

    if ((product.TrialInterval ?? 0) > 0 || (product.InitialChargeInCents ?? 0) > 0)
    {
        Console.WriteLine($"  WARNING: '{handle}' has a trial period or setup fee — UC1's first-invoice expectations assume neither (see UC0 failure scenarios in plan.md).");
    }
}

static async Task EnsureMeteredComponentAsync(MaxioAdvancedBillingClient client, int familyId, string handle, string name)
{
    Component? component = null;
    try
    {
        var response = await client.Components.FindComponent(handle);
        component = response.Component;
    }
    catch (SdkException<RawError>)
    {
        // Not found — created below.
    }

    if (component == null)
    {
        Console.WriteLine($"Metered component '{handle}' not found — creating it (per-unit, $0.01/unit).");
        var created = await client.Components.CreateMeteredComponent(familyId.ToString(), new CreateMeteredComponent
        {
            MeteredComponent = new MeteredComponent
            {
                Name = name,
                UnitName = "call",
                Handle = handle,
                PricingScheme = PricingScheme.PerUnit,
                UnitPrice = MaxioAdvancedBilling.Models.AnyOf.UnitPrice1.Double(0.01)
            }
        });
        component = created.Component;
    }

    if (component == null)
    {
        Console.Error.WriteLine($"Could not resolve or create metered component '{handle}'.");
        return;
    }

    Console.WriteLine($"Metered component: handle='{handle}' id={component.Id} kind={component.Kind} unitPrice={component.UnitPrice}");

    if (component.Kind != ComponentKind.MeteredComponent)
    {
        Console.WriteLine($"  WARNING: '{handle}' is kind '{component.Kind}', not Metered. A component's kind cannot be changed in place — archive it in the Maxio admin UI and recreate it as Metered (see UC0 failure scenarios in plan.md), then re-run this tool.");
    }

    if (component.ProductFamilyId != familyId)
    {
        Console.WriteLine($"  WARNING: '{handle}' is not on product family id {familyId} (found on family id {component.ProductFamilyId}) — it will not be available to eshop-pro/basic-plan subscriptions.");
    }
}
