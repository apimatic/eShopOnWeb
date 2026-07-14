// UC0 operator tooling (plan.md §4.1, §6 Phase 0): a one-shot script that provisions the product
// family, plans, and metered component the integration expects on the Maxio sandbox. Not wired
// into the Web or PublicApi hosts — run manually (or scripted) once per sandbox, or on reseed.
//
// Reads credentials from MAXIO_API_KEY / MAXIO_SITE_SUBDOMAIN / MAXIO_ENVIRONMENT /
// MAXIO_DEFAULT_PRODUCT_FAMILY environment variables — never written to a file.

using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Servers;
using MaxioModels = MaxioAdvancedBilling.Models;
using MaxioEnums = MaxioAdvancedBilling.Models.Enums;
using MaxioUnions = MaxioAdvancedBilling.Models.AnyOf;

var apiKey = RequireEnv("MAXIO_API_KEY");
var subdomain = RequireEnv("MAXIO_SITE_SUBDOMAIN");
var environmentName = Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT") ?? "US";
var familyHandle = Environment.GetEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY") ?? "eshop-subscribe";

using var httpClient = new HttpClient();
var isEu = string.Equals(environmentName, "EU", StringComparison.OrdinalIgnoreCase);
var productionOptions = new ProductionOptions();
if (isEu)
{
    productionOptions.Eu.Site = subdomain;
}
else
{
    productionOptions.Us.Site = subdomain;
}

var client = new MaxioAdvancedBillingClient(httpClient, new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials { Username = apiKey, Password = "x" },
    Environment = isEu ? ServerEnvironment.Eu : ServerEnvironment.Us,
    Server = new ServerOptions { Production = productionOptions },
});

Console.WriteLine($"Seeding Maxio sandbox '{subdomain}' ({environmentName})...");

var family = await FindOrCreateProductFamilyAsync(client, familyHandle, "eShopSubscribe");
Console.WriteLine($"Product family: id={family.Id}, handle={family.Handle}, name={family.Name}");

var proProduct = await FindOrCreateProductAsync(client, family.Id!.Value, "eshop-pro", "Pro Plan", "Pro Plan - full access", 29900);
Console.WriteLine($"Product: id={proProduct.Id}, handle={proProduct.Handle}, name={proProduct.Name}, priceInCents={proProduct.PriceInCents}");

var basicProduct = await FindOrCreateProductAsync(client, family.Id!.Value, "basic-plan", "Basic Plan", "Basic Plan - limited access", 2900);
Console.WriteLine($"Product: id={basicProduct.Id}, handle={basicProduct.Handle}, name={basicProduct.Name}, priceInCents={basicProduct.PriceInCents}");

var component = await FindOrCreateMeteredComponentAsync(client, family.Id!.Value, "api-call", "API Calls");
Console.WriteLine($"Metered component: id={component.Id}, handle={component.Handle}, name={component.Name}");

Console.WriteLine();
Console.WriteLine("Seed complete. Update Maxio:* user-secrets for both Web and PublicApi with:");
Console.WriteLine($"  Maxio:ProductFamilyId        = {family.Id}");
Console.WriteLine($"  Maxio:ProductFamilyHandle    = {family.Handle}");
Console.WriteLine($"  Maxio:DefaultProductId       = {proProduct.Id}");
Console.WriteLine($"  Maxio:DefaultProductHandle   = {proProduct.Handle}");
Console.WriteLine($"  Maxio:AlternateProductId     = {basicProduct.Id}");
Console.WriteLine($"  Maxio:AlternateProductHandle = {basicProduct.Handle}");
Console.WriteLine($"  Maxio:MeteredComponentId     = {component.Id}");
Console.WriteLine($"  Maxio:MeteredComponentHandle = {component.Handle}");

static string RequireEnv(string name) =>
    Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"Required environment variable '{name}' is not set.");

static async Task<MaxioModels.ProductFamily> FindOrCreateProductFamilyAsync(MaxioAdvancedBillingClient client, string handle, string name)
{
    var families = await client.ProductFamilies.ListProductFamilies(
        dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, ct: default);
    var existing = families
        .Select(f => f.ProductFamily)
        .FirstOrDefault(f => f is not null && string.Equals(f.Handle, handle, StringComparison.OrdinalIgnoreCase));
    if (existing is not null)
    {
        Console.WriteLine($"Product family '{handle}' already exists (id={existing.Id}); reusing.");
        return existing;
    }

    var response = await client.ProductFamilies.CreateProductFamily(new MaxioModels.CreateProductFamilyRequest
    {
        ProductFamily = new MaxioModels.CreateProductFamily { Name = name, Handle = handle },
    });
    return response.ProductFamily ?? throw new InvalidOperationException("Product family was not returned after creation.");
}

static async Task<MaxioModels.Product> FindOrCreateProductAsync(
    MaxioAdvancedBillingClient client, int productFamilyId, string handle, string name, string description, long priceInCents)
{
    try
    {
        var existingResponse = await client.Products.ReadProductByHandle(apiHandle: handle, ct: default);
        if (existingResponse.Product is not null)
        {
            Console.WriteLine($"Product '{handle}' already exists (id={existingResponse.Product.Id}); reusing.");
            return existingResponse.Product;
        }
    }
    catch (SdkException<RawError>)
    {
        // Not found on this site — fall through to create.
    }

    var response = await client.Products.CreateProduct(productFamilyId.ToString(), new MaxioModels.CreateOrUpdateProductRequest
    {
        Product = new MaxioModels.CreateOrUpdateProduct
        {
            Name = name,
            Handle = handle,
            Description = description,
            RequireCreditCard = false,
            PriceInCents = priceInCents,
            Interval = 1,
            IntervalUnit = MaxioEnums.IntervalUnit.Month,
        },
    });

    return response.Product ?? throw new InvalidOperationException($"Product '{handle}' was not returned after creation.");
}

static async Task<MaxioModels.Component> FindOrCreateMeteredComponentAsync(MaxioAdvancedBillingClient client, int productFamilyId, string handle, string name)
{
    try
    {
        var existingResponse = await client.Components.FindComponent(handle: handle, ct: default);
        if (existingResponse.Component is not null)
        {
            Console.WriteLine($"Component '{handle}' already exists (id={existingResponse.Component.Id}); reusing.");
            return existingResponse.Component;
        }
    }
    catch (SdkException<RawError>)
    {
        // Not found on this site — fall through to create.
    }

    var response = await client.Components.CreateMeteredComponent(productFamilyId.ToString(), new MaxioModels.CreateMeteredComponent
    {
        MeteredComponent = new MaxioModels.MeteredComponent
        {
            Name = name,
            Handle = handle,
            UnitName = "call",
            Taxable = false,
            PricingScheme = MaxioEnums.PricingScheme.PerUnit,
            UnitPrice = MaxioUnions.UnitPrice1.Double(0.01),
        },
    });

    return response.Component ?? throw new InvalidOperationException($"Component '{handle}' was not returned after creation.");
}
