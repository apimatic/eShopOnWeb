using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.AnyOf;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.SubscriptionsSeed;
using Microsoft.Extensions.Configuration;

// UC0 operator tooling. This is deliberately not wired into either host: it is run by hand, once per
// sandbox, to provision or verify the billing catalog the subscription features expect.
//
//   dotnet run --project src/SubscriptionsSeed -- verify   (default) reports what exists
//   dotnet run --project src/SubscriptionsSeed -- seed     creates whatever is missing, then verifies
//
// Credentials come from user-secrets or environment variables; nothing sensitive is read from a file
// in this repository.

var command = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "verify";
if (command is not ("verify" or "seed"))
{
    Console.Error.WriteLine($"Unknown command '{command}'. Use 'verify' or 'seed'.");
    return 2;
}

var configuration = new ConfigurationBuilder()
    .AddUserSecrets(typeof(SeedCatalog).Assembly, optional: true)
    .AddEnvironmentVariables()
    .Build();

var settings = configuration.GetSection(MaxioSettings.ConfigurationSection).Get<MaxioSettings>() ?? new MaxioSettings();

try
{
    settings.Validate();
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine("Set them with: dotnet user-secrets set \"Maxio:ApiKey\" \"<key>\" --project src/SubscriptionsSeed");
    return 2;
}

using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
var client = new MaxioAdvancedBillingClient(httpClient, MaxioClientOptionsFactory.Create(settings));
var cancellationToken = CancellationToken.None;

Console.WriteLine($"Target: {settings.ResolveBaseUrl()}  (region {settings.Environment})");
Console.WriteLine($"Command: {command}");
Console.WriteLine();

try
{
    var familyId = await ResolveOrCreateFamilyAsync();
    await EnsurePlansAsync(familyId);
    await EnsureMeteredComponentAsync(familyId);

    Console.WriteLine();
    var ok = await VerifyAsync(familyId);

    Console.WriteLine();
    Console.WriteLine(ok
        ? "UC0 satisfied: the billing catalog matches what the integration expects."
        : "UC0 NOT satisfied: see the mismatches above.");

    return ok ? 0 : 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"Seed failed: {Describe(ex)}");
    return 1;
}

async Task<int> ResolveOrCreateFamilyAsync()
{
    var families = await client.ProductFamilies.ListProductFamilies(
        dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, ct: cancellationToken);

    var existing = families
        .Select(f => f.ProductFamily)
        .FirstOrDefault(f => f is not null && f.Handle == settings.ProductFamilyHandle);

    if (existing?.Id is not null)
    {
        Console.WriteLine($"[=] product family '{settings.ProductFamilyHandle}' exists (id {existing.Id}).");
        return existing.Id.Value;
    }

    if (command != "seed")
    {
        throw new InvalidOperationException(
            $"product family '{settings.ProductFamilyHandle}' does not exist. Re-run with 'seed' to create it.");
    }

    var created = await client.ProductFamilies.CreateProductFamily(
        new CreateProductFamilyRequest
        {
            ProductFamily = new CreateProductFamily
            {
                Name = SeedCatalog.ProductFamilyName,
                Handle = settings.ProductFamilyHandle,
                Description = "Recurring plans and metered usage for the eShopOnWeb storefront."
            }
        },
        ct: cancellationToken);

    var id = created.ProductFamily?.Id
        ?? throw new InvalidOperationException("the provider created a product family but returned no id.");

    Console.WriteLine($"[+] created product family '{settings.ProductFamilyHandle}' (id {id}).");
    return id;
}

async Task EnsurePlansAsync(int familyId)
{
    foreach (var plan in SeedCatalog.Plans)
    {
        var existing = await FindProductAsync(plan.Handle);
        if (existing is not null)
        {
            Console.WriteLine($"[=] plan '{plan.Handle}' exists (id {existing.Id}, {FormatCents(existing.PriceInCents)}).");
            continue;
        }

        if (command != "seed")
        {
            Console.WriteLine($"[!] plan '{plan.Handle}' is missing. Re-run with 'seed' to create it.");
            continue;
        }

        var created = await client.Products.CreateProduct(
            familyId.ToString(CultureInfo.InvariantCulture),
            new CreateOrUpdateProductRequest
            {
                Product = new CreateOrUpdateProduct
                {
                    Name = plan.Name,
                    Description = plan.Description,
                    Handle = plan.Handle,
                    PriceInCents = plan.PriceInCents,
                    Interval = 1,
                    IntervalUnit = IntervalUnit.Month,
                    // The demo subscribes without card capture, so a payment method must not be required.
                    RequireCreditCard = false
                }
            },
            ct: cancellationToken);

        Console.WriteLine($"[+] created plan '{plan.Handle}' (id {created.Product.Id}, {FormatCents(created.Product.PriceInCents)}).");
    }
}

async Task EnsureMeteredComponentAsync(int familyId)
{
    var existing = await FindComponentAsync(settings.MeteredComponentHandle);
    if (existing is not null)
    {
        Console.WriteLine($"[=] component '{settings.MeteredComponentHandle}' exists (id {existing.Id}, kind {existing.Kind?.Value ?? "unknown"}).");
        return;
    }

    if (command != "seed")
    {
        Console.WriteLine($"[!] component '{settings.MeteredComponentHandle}' is missing. Re-run with 'seed' to create it.");
        return;
    }

    var created = await client.Components.CreateMeteredComponent(
        familyId.ToString(CultureInfo.InvariantCulture),
        new CreateMeteredComponent
        {
            MeteredComponent = new MeteredComponent
            {
                Name = SeedCatalog.MeteredComponentName,
                UnitName = SeedCatalog.MeteredComponentUnitName,
                PricingScheme = PricingScheme.PerUnit,
                Handle = settings.MeteredComponentHandle,
                // Dollars, not cents: this field is the one place a price is not in minor units.
                UnitPrice = UnitPrice1.String(SeedCatalog.MeteredUnitPrice),
                Taxable = false
            }
        },
        ct: cancellationToken);

    Console.WriteLine($"[+] created metered component '{settings.MeteredComponentHandle}' (id {created.Component.Id}).");
}

async Task<bool> VerifyAsync(int familyId)
{
    Console.WriteLine("Verification (reading the catalog back):");
    var ok = true;

    var products = await client.ProductFamilies.ListProductsForProductFamily(
        familyId.ToString(CultureInfo.InvariantCulture),
        dateField: null, filter: null, startDate: null, endDate: null,
        startDatetime: null, endDatetime: null, includeArchived: false, include: null,
        page: 1, perPage: 100, ct: cancellationToken);

    foreach (var plan in SeedCatalog.Plans)
    {
        var product = products.Select(p => p.Product).FirstOrDefault(p => p.Handle == plan.Handle);

        if (product is null)
        {
            Console.WriteLine($"  FAIL  plan '{plan.Handle}' is not listed under family '{settings.ProductFamilyHandle}'.");
            ok = false;
            continue;
        }

        var issues = new List<string>();
        if (product.PriceInCents != plan.PriceInCents)
        {
            issues.Add($"price is {FormatCents(product.PriceInCents)}, expected {FormatCents(plan.PriceInCents)}");
        }

        if (product.Interval != 1 || product.IntervalUnit != IntervalUnit.Month)
        {
            issues.Add($"billing period is {product.Interval} {product.IntervalUnit?.Value}, expected 1 month");
        }

        if (product.RequireCreditCard == true)
        {
            issues.Add("requires a payment method, so the card-free subscribe flow will be refused");
        }

        if (product.ArchivedAt is not null)
        {
            issues.Add("is archived");
        }

        if (issues.Count == 0)
        {
            Console.WriteLine($"  OK    plan '{plan.Handle}' (id {product.Id}) {FormatCents(product.PriceInCents)}/month, no payment method required.");
        }
        else
        {
            Console.WriteLine($"  FAIL  plan '{plan.Handle}': {string.Join("; ", issues)}.");
            ok = false;
        }
    }

    var component = await FindComponentAsync(settings.MeteredComponentHandle);
    if (component is null)
    {
        Console.WriteLine($"  FAIL  component '{settings.MeteredComponentHandle}' does not resolve.");
        return false;
    }

    var componentIssues = new List<string>();
    if (component.Kind != ComponentKind.MeteredComponent)
    {
        componentIssues.Add($"kind is '{component.Kind?.Value ?? "unknown"}', expected metered — a component's kind cannot be converted in place, so archive it and recreate it");
    }

    if (component.ProductFamilyHandle != settings.ProductFamilyHandle)
    {
        componentIssues.Add($"lives on family '{component.ProductFamilyHandle}', expected '{settings.ProductFamilyHandle}'");
    }

    if (component.UnitPrice != SeedCatalog.MeteredUnitPrice)
    {
        componentIssues.Add($"unit price is '{component.UnitPrice}', expected '{SeedCatalog.MeteredUnitPrice}' dollars");
    }

    if (component.Archived == true || component.ArchivedAt is not null)
    {
        componentIssues.Add("is archived");
    }

    if (componentIssues.Count == 0)
    {
        Console.WriteLine($"  OK    component '{component.Handle}' (id {component.Id}) metered, ${component.UnitPrice} per {component.UnitName}.");
    }
    else
    {
        Console.WriteLine($"  FAIL  component '{settings.MeteredComponentHandle}': {string.Join("; ", componentIssues)}.");
        ok = false;
    }

    return ok;
}

async Task<Product?> FindProductAsync(string handle)
{
    try
    {
        var response = await client.Products.ReadProductByHandle(handle, ct: cancellationToken);
        return response.Product;
    }
    catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
    {
        return null;
    }
}

async Task<Component?> FindComponentAsync(string handle)
{
    try
    {
        var response = await client.Components.FindComponent(handle, ct: cancellationToken);
        return response.Component;
    }
    catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
    {
        return null;
    }
}

static string FormatCents(long? cents) => ((cents ?? 0L) / 100m).ToString("C2", CultureInfo.GetCultureInfo("en-US"));

static string Describe(Exception exception) => exception switch
{
    SdkException<RawError> raw => $"HTTP {(int)raw.Error.StatusCode}: {SafeBody(raw.Error)}",
    SdkException<CreateProductFamilyError> e => DescribeTyped(e.Error.TryGetErrorListResponse1(out var list) ? list : null,
        e.Error.TryGetRawError(out var raw) ? raw : null),
    SdkException<CreateProductError> e => DescribeTyped(e.Error.TryGetErrorListResponse1(out var list) ? list : null,
        e.Error.TryGetRawError(out var raw) ? raw : null),
    SdkException<CreateMeteredComponentError> e => DescribeTyped(e.Error.TryGetErrorListResponse1(out var list) ? list : null,
        e.Error.TryGetRawError(out var raw) ? raw : null),
    _ => exception.Message
};

static string DescribeTyped(ErrorListResponse1? errors, RawError? raw)
{
    if (errors is not null)
    {
        return string.Join("; ", errors.Errors);
    }

    return raw is null ? "the provider returned an error with no readable detail" : $"HTTP {(int)raw.StatusCode}: {SafeBody(raw)}";
}

static string SafeBody(RawError error)
{
    try
    {
        var body = error.ReadAsString();
        return string.IsNullOrWhiteSpace(body) ? "(no body)" : body.Trim();
    }
    catch (Exception)
    {
        return "(unreadable body)";
    }
}
