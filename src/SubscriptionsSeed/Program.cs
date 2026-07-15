using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.AnyOf;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;

// UC0 operator tooling (plan.md §Phase 0 / maxio-plan.md §5): one-time seed of the product family,
// the two recurring plans, and the metered component this integration is configured to use.
// Not wired into the storefront — run manually against the sandbox, then copy the printed ids into
// the app's user-secrets (Maxio:ProductFamilyId / Maxio:DefaultProductId / Maxio:AlternateProductId /
// Maxio:MeteredComponentId) for both Web and PublicApi.

var apiKey = Environment.GetEnvironmentVariable("MAXIO_API_KEY");
var subdomain = Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN");
var environment = Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT") ?? "US";
var familyHandle = Environment.GetEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY") ?? "eshop-subscribe";

if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(subdomain))
{
    Console.Error.WriteLine("MAXIO_API_KEY and MAXIO_SITE_SUBDOMAIN environment variables are required.");
    return 1;
}

var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials { Username = apiKey, Password = "x" },
    Environment = string.Equals(environment, "EU", StringComparison.OrdinalIgnoreCase) ? ServerEnvironment.Eu : ServerEnvironment.Us
};
if (string.Equals(environment, "EU", StringComparison.OrdinalIgnoreCase))
{
    options.Server.Production.Eu.Site = subdomain;
}
else
{
    options.Server.Production.Us.Site = subdomain;
}

using var httpClient = new HttpClient();
var client = new MaxioAdvancedBillingClient(httpClient, options);

try
{
    Console.WriteLine($"Creating product family '{familyHandle}'...");
    var familyResponse = await client.ProductFamilies.CreateProductFamily(new CreateProductFamilyRequest
    {
        ProductFamily = new CreateProductFamily
        {
            Name = "eShopSubscribe",
            Handle = familyHandle,
            Description = "eShopOnWeb Subscribe demo product family."
        }
    });
    var familyId = familyResponse.ProductFamily!.Id!.Value;
    var familyIdString = familyId.ToString();
    Console.WriteLine($"  -> ProductFamilyId = {familyId}");

    Console.WriteLine("Creating Pro Plan (eshop-pro, $299.00/month)...");
    var proPlanResponse = await client.Products.CreateProduct(familyIdString, new CreateOrUpdateProductRequest
    {
        Product = new CreateOrUpdateProduct
        {
            Name = "Pro Plan",
            Handle = "eshop-pro",
            Description = "Pro Plan — eShopOnWeb Subscribe demo.",
            PriceInCents = 29900,
            Interval = 1,
            IntervalUnit = IntervalUnit.Month,
            RequireCreditCard = false
        }
    });
    var proProductId = proPlanResponse.Product!.Id!.Value;
    Console.WriteLine($"  -> DefaultProductId (eshop-pro) = {proProductId}");

    Console.WriteLine("Creating Basic Plan (basic-plan, $29.00/month)...");
    var basicPlanResponse = await client.Products.CreateProduct(familyIdString, new CreateOrUpdateProductRequest
    {
        Product = new CreateOrUpdateProduct
        {
            Name = "Basic Plan",
            Handle = "basic-plan",
            Description = "Basic Plan — eShopOnWeb Subscribe demo.",
            PriceInCents = 2900,
            Interval = 1,
            IntervalUnit = IntervalUnit.Month,
            RequireCreditCard = false
        }
    });
    var basicProductId = basicPlanResponse.Product!.Id!.Value;
    Console.WriteLine($"  -> AlternateProductId (basic-plan) = {basicProductId}");

    Console.WriteLine("Creating metered component (api-call, Per Unit, $0.01/unit)...");
    var componentResponse = await client.Components.CreateMeteredComponent(familyIdString, new MaxioAdvancedBilling.Models.CreateMeteredComponent
    {
        MeteredComponent = new MeteredComponent
        {
            Name = "API Calls",
            UnitName = "call",
            Handle = "api-call",
            PricingScheme = PricingScheme.PerUnit,
            UnitPrice = UnitPrice1.String("0.01"),
            Taxable = false
        }
    });
    var componentId = componentResponse.Component!.Id!.Value;
    Console.WriteLine($"  -> MeteredComponentId (api-call) = {componentId}");

    Console.WriteLine();
    Console.WriteLine("Seed complete. Update user-secrets for src/Web and src/PublicApi with:");
    Console.WriteLine($"  Maxio:ProductFamilyId        = {familyId}");
    Console.WriteLine($"  Maxio:DefaultProductId        = {proProductId}");
    Console.WriteLine($"  Maxio:AlternateProductId      = {basicProductId}");
    Console.WriteLine($"  Maxio:MeteredComponentId      = {componentId}");
    return 0;
}
catch (SdkException<CreateProductFamilyError> ex)
{
    Console.Error.WriteLine($"Failed to create product family: {Describe(ex.Error.TryGetErrorListResponse1, ex.Error.TryGetRawError)}");
    return 1;
}
catch (SdkException<CreateProductError> ex)
{
    Console.Error.WriteLine($"Failed to create product: {Describe(ex.Error.TryGetErrorListResponse1, ex.Error.TryGetRawError)}");
    return 1;
}
catch (SdkException<CreateMeteredComponentError> ex)
{
    Console.Error.WriteLine($"Failed to create metered component: {Describe(ex.Error.TryGetErrorListResponse1, ex.Error.TryGetRawError)}");
    return 1;
}

static string Describe(TryGetErrorList tryGetErrorList, TryGetRaw tryGetRaw)
{
    if (tryGetErrorList(out var errors))
    {
        return string.Join("; ", errors.Errors);
    }

    if (tryGetRaw(out var raw))
    {
        return $"HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}";
    }

    return "unrecognized error shape";
}

delegate bool TryGetErrorList(out ErrorListResponse1 errors);
delegate bool TryGetRaw(out RawError raw);
