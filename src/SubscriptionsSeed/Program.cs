using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using MaxioEnums = MaxioAdvancedBilling.Models.Enums;
using MaxioModels = MaxioAdvancedBilling.Models;

// UC0 — one-time operator seed/verify for the Maxio sandbox (plan.md UC0). Operator tooling only:
// not wired into the Web or PublicApi hosts. Idempotent — safe to run repeatedly; only creates an
// entity when the configured handle doesn't already resolve to one of the right shape.
//
// Usage: dotnet run --project src/SubscriptionsSeed

const string WebUserSecretsId = "aspnet-Web2-1FA3F72E-E7E3-4360-9E49-1CCCD7FE85F7";

var configuration = new ConfigurationBuilder()
    .AddUserSecrets(WebUserSecretsId)
    .AddEnvironmentVariables()
    .Build();

var maxio = configuration.GetSection("Maxio");
var apiKey = maxio["ApiKey"];
var subdomain = maxio["Subdomain"];
var region = maxio["Environment"] ?? "US";
var baseUrl = maxio["BaseUrl"];

if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("Maxio:ApiKey is not configured (checked user-secrets for the Web host's UserSecretsId and environment variables). Aborting.");
    return 1;
}

const string familyName = "eShopSubscribe";
var familyHandle = maxio["ProductFamilyHandle"] ?? "eshop-subscribe";
var configuredFamilyId = ParseIntOrNull(maxio["ProductFamilyId"]);

var proHandle = maxio["DefaultProductHandle"] ?? "eshop-pro";
var basicHandle = maxio["AlternateProductHandle"] ?? "basic-plan";
var componentHandle = maxio["MeteredComponentHandle"] ?? "api-call";

var isEu = string.Equals(region, "EU", StringComparison.OrdinalIgnoreCase);
var options = new MaxioAdvancedBillingClientOptions
{
    Environment = isEu ? ServerEnvironment.Eu : ServerEnvironment.Us,
    BasicAuth = new BasicAuthCredentials { Username = apiKey, Password = "x" }
};
if (!string.IsNullOrWhiteSpace(baseUrl))
{
    if (isEu) options.Server.Production.Eu.BaseUrl = baseUrl;
    else options.Server.Production.Us.BaseUrl = baseUrl;
}
else
{
    if (isEu) options.Server.Production.Eu.Site = subdomain;
    else options.Server.Production.Us.Site = subdomain;
}

using var httpClient = new HttpClient();
var client = new MaxioAdvancedBillingClient(httpClient, options);

Console.WriteLine($"Seeding/verifying Maxio sandbox '{subdomain}' (region {region})...");

try
{
    var familyId = await ResolveOrCreateProductFamilyAsync(configuredFamilyId, familyHandle, familyName);
    var familyIdString = familyId.ToString(CultureInfo.InvariantCulture);

    await EnsureProductAsync(familyIdString, proHandle, "Pro Plan", priceInCents: 29900, intervalUnit: MaxioEnums.IntervalUnit.Month);
    await EnsureProductAsync(familyIdString, basicHandle, "Basic Plan", priceInCents: 2900, intervalUnit: MaxioEnums.IntervalUnit.Month);
    await EnsureMeteredComponentAsync(familyId, componentHandle, "API Calls", unitPriceDollars: 0.01m);

    await VerifySeedAsync(familyId, familyIdString);

    Console.WriteLine();
    Console.WriteLine("Seed verified. If any IDs were newly created above, update Maxio:ProductFamilyId / Maxio:DefaultProductId / Maxio:AlternateProductId / Maxio:MeteredComponentId in user-secrets to match (plan.md UC0 step 5).");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Seed failed: {ex.Message}");
    return 1;
}

async System.Threading.Tasks.Task<int> ResolveOrCreateProductFamilyAsync(int? existingId, string handle, string name)
{
    if (existingId is int id)
    {
        try
        {
            var existing = await client.ProductFamilies.ReadProductFamily(id);
            if (existing.ProductFamily is not null)
            {
                Console.WriteLine($"Product family '{existing.ProductFamily.Handle}' (id {id}) already exists — matches configuration.");
                return id;
            }
        }
        catch (SdkException<RawError> ex) when (ex.Error?.StatusCode == HttpStatusCode.NotFound)
        {
            Console.WriteLine($"Configured Maxio:ProductFamilyId {id} was not found; creating a new product family.");
        }
    }

    var created = await client.ProductFamilies.CreateProductFamily(new MaxioModels.CreateProductFamilyRequest
    {
        ProductFamily = new MaxioModels.CreateProductFamily
        {
            Name = name,
            Handle = handle
        }
    });

    var newFamily = created.ProductFamily ?? throw new InvalidOperationException("Empty create-product-family response.");
    Console.WriteLine($"Created product family '{newFamily.Handle}' with id {newFamily.Id}.");
    return newFamily.Id ?? throw new InvalidOperationException("Created product family has no id.");
}

async System.Threading.Tasks.Task EnsureProductAsync(string familyIdString, string handle, string name, long priceInCents, MaxioEnums.IntervalUnit intervalUnit)
{
    var products = await client.ProductFamilies.ListProductsForProductFamily(
        productFamilyId: familyIdString,
        dateField: null,
        filter: null,
        startDate: null,
        endDate: null,
        startDatetime: null,
        endDatetime: null,
        includeArchived: false,
        include: null,
        page: 1,
        perPage: 50);

    var existing = products.Select(p => p.Product).FirstOrDefault(p => p?.Handle == handle);
    if (existing is not null)
    {
        Console.WriteLine($"Product '{handle}' already exists (id {existing.Id}, {existing.PriceInCents} cents / {existing.Interval} {existing.IntervalUnit?.Value}, requireCreditCard={existing.RequireCreditCard}).");
        if (existing.PriceInCents != priceInCents || existing.RequireCreditCard == true)
        {
            Console.WriteLine($"  WARNING: existing product '{handle}' does not match the expected spec (price {priceInCents} cents, requireCreditCard=false). Review manually — this tool does not silently mutate an existing product (plan.md UC0 failure scenarios).");
        }
        return;
    }

    var created = await client.Products.CreateProduct(familyIdString, new MaxioModels.CreateOrUpdateProductRequest
    {
        Product = new MaxioModels.CreateOrUpdateProduct
        {
            Name = name,
            Handle = handle,
            Description = $"{name} — eShopOnWeb Subscribe demo plan",
            PriceInCents = priceInCents,
            Interval = 1,
            IntervalUnit = intervalUnit,
            RequireCreditCard = false,
            ExpirationInterval = null,
            ExpirationIntervalUnit = null
        }
    });

    var newProduct = created.Product ?? throw new InvalidOperationException($"Empty create-product response for '{handle}'.");
    Console.WriteLine($"Created product '{handle}' with id {newProduct.Id}.");
}

async System.Threading.Tasks.Task EnsureMeteredComponentAsync(int familyId, string handle, string name, decimal unitPriceDollars)
{
    var components = await client.Components.ListComponentsForProductFamily(
        productFamilyId: familyId,
        includeArchived: false,
        filter: null,
        dateField: null,
        endDate: null,
        endDatetime: null,
        startDate: null,
        startDatetime: null,
        page: 1,
        perPage: 50);

    var existing = components.Select(c => c.Component).FirstOrDefault(c => c?.Handle == handle);
    if (existing is not null)
    {
        if (existing.Kind == MaxioEnums.ComponentKind.MeteredComponent)
        {
            Console.WriteLine($"Metered component '{handle}' already exists (id {existing.Id}) and is the correct kind.");
            return;
        }

        Console.WriteLine($"Component '{handle}' exists but is kind {existing.Kind?.Value}, not Metered — archiving and recreating (plan.md UC0 failure scenario: a component's kind cannot be changed in place).");
        await client.Components.ArchiveComponent(familyId, existing.Id?.ToString(CultureInfo.InvariantCulture) ?? handle);
    }

    var created = await client.Components.CreateMeteredComponent(familyId.ToString(CultureInfo.InvariantCulture), new MaxioModels.CreateMeteredComponent
    {
        MeteredComponent = new MaxioModels.MeteredComponent
        {
            Name = name,
            UnitName = "call",
            Handle = handle,
            PricingScheme = MaxioEnums.PricingScheme.PerUnit,
            UnitPrice = MaxioModels.AnyOf.UnitPrice1.Double((double)unitPriceDollars)
        }
    });

    var newComponent = created.Component ?? throw new InvalidOperationException($"Empty create-component response for '{handle}'.");
    Console.WriteLine($"Created metered component '{handle}' with id {newComponent.Id}.");
}

async System.Threading.Tasks.Task VerifySeedAsync(int familyId, string familyIdString)
{
    Console.WriteLine();
    Console.WriteLine("Verifying seed by reading back the family, products, and component...");

    var family = (await client.ProductFamilies.ReadProductFamily(familyId)).ProductFamily
        ?? throw new InvalidOperationException("Product family not found on verification read-back.");
    Console.WriteLine($"  Family: {family.Name} ({family.Handle}, id {family.Id})");

    var products = await client.ProductFamilies.ListProductsForProductFamily(
        productFamilyId: familyIdString,
        dateField: null, filter: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null,
        includeArchived: false, include: null, page: 1, perPage: 50);
    foreach (var product in products.Select(p => p.Product).Where(p => p is not null))
    {
        Console.WriteLine($"  Product: {product!.Name} ({product.Handle}, id {product.Id}, {product.PriceInCents} cents / {product.Interval} {product.IntervalUnit?.Value})");
    }

    var components = await client.Components.ListComponentsForProductFamily(
        productFamilyId: familyId, includeArchived: false, filter: null, dateField: null,
        endDate: null, endDatetime: null, startDate: null, startDatetime: null, page: 1, perPage: 50);
    foreach (var component in components.Select(c => c.Component).Where(c => c is not null))
    {
        Console.WriteLine($"  Component: {component!.Name} ({component.Handle}, id {component.Id}, kind {component.Kind?.Value})");
    }
}

static int? ParseIntOrNull(string? value) => int.TryParse(value, out var parsed) ? parsed : null;
