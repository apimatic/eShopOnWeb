using System.Globalization;
using System.Net;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.AnyOf;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.SubscriptionsSeed;

/// <summary>
/// Verifies — and on request provisions — the product family, recurring plans, and metered
/// component the subscription module is configured to use.
/// </summary>
/// <remarks>
/// Every step reads before it writes, so the tool is safe to re-run: creates are never auto-retried
/// by the SDK, and an already-correct sandbox is left untouched.
/// </remarks>
public sealed class SeedRunner
{
    private const string ApiKeyPasswordSentinel = "x";

    private readonly MaxioSettings _settings;
    private readonly MaxioAdvancedBillingClient _client;
    private readonly HttpClient _httpClient;

    public SeedRunner(IConfiguration configuration)
    {
        _settings = configuration.GetSection(MaxioSettings.SectionName).Get<MaxioSettings>()
                    ?? new MaxioSettings();
        _settings.Validate();

        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials
            {
                Username = _settings.ApiKey!,
                Password = ApiKeyPasswordSentinel
            },
            Environment = _settings.IsEuRegion ? ServerEnvironment.Eu : ServerEnvironment.Us
        };

        var baseUrl = _settings.ResolveBaseUrl();
        if (_settings.IsEuRegion)
        {
            options.Server.Production.Eu.BaseUrl = baseUrl;
        }
        else
        {
            options.Server.Production.Us.BaseUrl = baseUrl;
        }

        _httpClient = new HttpClient();
        _client = new MaxioAdvancedBillingClient(_httpClient, options);
    }

    /// <summary>The plans the integration expects to exist, with the shape UC0 specifies.</summary>
    private PlanSpec[] PlanSpecs =>
    [
        new(_settings.DefaultProductHandle ?? "eshop-pro", "Pro Plan", "eShopOnWeb Pro subscription", 29900),
        new(_settings.AlternateProductHandle ?? "basic-plan", "Basic Plan", "eShopOnWeb Basic subscription", 2900)
    ];

    public async Task<int> RunAsync(bool applyChanges, CancellationToken cancellationToken)
    {
        var familyHandle = _settings.ProductFamilyHandle!;
        var componentHandle = _settings.MeteredComponentHandle!;

        Console.WriteLine($"Target server : {_settings.ResolveBaseUrl()}");
        Console.WriteLine($"Mode          : {(applyChanges ? "SEED (will create missing entities)" : "VERIFY ONLY")}");
        Console.WriteLine();

        var problems = new List<string>();

        // --- Product family -----------------------------------------------------------------
        var family = await FindProductFamilyAsync(familyHandle, cancellationToken);
        if (family is null)
        {
            if (!applyChanges)
            {
                problems.Add($"Product family '{familyHandle}' does not exist. Re-run with --seed to create it.");
                return Report(problems);
            }

            family = await CreateProductFamilyAsync(familyHandle, cancellationToken);
            Console.WriteLine($"CREATED  product family '{familyHandle}' (id {family.Id}).");
        }
        else
        {
            Console.WriteLine($"OK       product family '{familyHandle}' (id {family.Id}).");
        }

        var familyId = family.Id ?? throw new InvalidOperationException(
            $"Maxio returned product family '{familyHandle}' without an id.");

        // --- Plans --------------------------------------------------------------------------
        foreach (var spec in PlanSpecs)
        {
            var product = await FindProductAsync(spec.Handle, cancellationToken);

            if (product is null)
            {
                if (!applyChanges)
                {
                    problems.Add($"Plan '{spec.Handle}' does not exist. Re-run with --seed to create it.");
                    continue;
                }

                product = await CreateProductAsync(familyId, spec, cancellationToken);
                Console.WriteLine(
                    $"CREATED  plan '{spec.Handle}' (id {product.Id}) at {FormatMoney(product.PriceInCents)}/{product.IntervalUnit?.Value}.");
                continue;
            }

            Console.WriteLine(
                $"OK       plan '{spec.Handle}' (id {product.Id}) at {FormatMoney(product.PriceInCents)}/{product.IntervalUnit?.Value}.");

            if (product.ProductFamily?.Handle is { } owner &&
                !string.Equals(owner, familyHandle, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add(
                    $"Plan '{spec.Handle}' belongs to product family '{owner}', not '{familyHandle}'. It will not be offered by the storefront.");
            }

            if (product.ArchivedAt is not null)
            {
                problems.Add($"Plan '{spec.Handle}' is archived and cannot be subscribed to.");
            }

            if (product.RequireCreditCard == true)
            {
                problems.Add(
                    $"Plan '{spec.Handle}' requires a payment method, so subscribing will demand card capture. Turn the toggle off in the Maxio UI to exercise the demo path.");
            }
        }

        // --- Metered component ---------------------------------------------------------------
        var component = await FindComponentAsync(componentHandle, cancellationToken);

        if (component is null)
        {
            if (!applyChanges)
            {
                problems.Add($"Metered component '{componentHandle}' does not exist. Re-run with --seed to create it.");
            }
            else
            {
                component = await CreateMeteredComponentAsync(familyId, componentHandle, cancellationToken);
                Console.WriteLine(
                    $"CREATED  metered component '{componentHandle}' (id {component.Id}) at {component.UnitPrice ?? "?"} per unit.");
            }
        }
        else
        {
            Console.WriteLine(
                $"OK       metered component '{componentHandle}' (id {component.Id}), kind '{component.Kind?.Value}', {component.UnitPrice ?? "?"} per unit.");

            if (!string.Equals(component.Kind?.Value, ComponentKind.MeteredComponent.Value, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add(
                    $"Component '{componentHandle}' is of kind '{component.Kind?.Value}', not metered. A component's kind cannot be changed in place — archive it and recreate it as metered under a new handle, then update configuration.");
            }

            if (component.ProductFamilyId is { } ownerId && ownerId != familyId)
            {
                problems.Add(
                    $"Component '{componentHandle}' lives on product family {ownerId}, not {familyId}, so it is not available to these plans.");
            }

            if (component.ArchivedAt is not null || component.Archived == true)
            {
                problems.Add($"Component '{componentHandle}' is archived and cannot accrue usage.");
            }
        }

        Console.WriteLine();
        return Report(problems);
    }

    private static int Report(IReadOnlyCollection<string> problems)
    {
        if (problems.Count == 0)
        {
            Console.WriteLine("Sandbox matches the configuration. UC0 is satisfied.");
            return 0;
        }

        Console.Error.WriteLine("Problems found:");
        foreach (var problem in problems)
        {
            Console.Error.WriteLine($"  - {problem}");
        }

        return 1;
    }

    // -------------------------------------------------------------------------------------------
    // Reads
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Finds a product family by handle. The read-by-id operation only accepts a numeric id, so the
    /// families are listed and matched on handle.
    /// </summary>
    private async Task<ProductFamily?> FindProductFamilyAsync(string handle, CancellationToken cancellationToken)
    {
        var families = await _client.ProductFamilies.ListProductFamilies(
            dateField: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null,
            ct: cancellationToken);

        return families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f is not null &&
                                 string.Equals(f.Handle, handle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<Product?> FindProductAsync(string handle, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Products.ReadProductByHandle(handle, ct: cancellationToken);
            return response.Product;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<Component?> FindComponentAsync(string handle, CancellationToken cancellationToken)
    {
        try
        {
            // Lookup endpoint: the handle is a query parameter and is passed bare.
            var response = await _client.Components.FindComponent(handle, ct: cancellationToken);
            return response.Component;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    // -------------------------------------------------------------------------------------------
    // Writes
    // -------------------------------------------------------------------------------------------

    private async Task<ProductFamily> CreateProductFamilyAsync(string handle, CancellationToken cancellationToken)
    {
        var response = await _client.ProductFamilies.CreateProductFamily(new CreateProductFamilyRequest
        {
            ProductFamily = new CreateProductFamily
            {
                Name = "eShopSubscribe",
                Handle = handle,
                Description = "Recurring plans and metered usage for the eShopOnWeb storefront."
            }
        }, ct: cancellationToken);

        return response.ProductFamily
               ?? throw new InvalidOperationException("Maxio returned no product family on create.");
    }

    /// <summary>
    /// Creates a recurring monthly plan with no trial, no setup fee, no expiry, and no payment
    /// method required — the shape the demo flows expect.
    /// </summary>
    private async Task<Product> CreateProductAsync(int familyId, PlanSpec spec, CancellationToken cancellationToken)
    {
        var response = await _client.Products.CreateProduct(
            familyId.ToString(CultureInfo.InvariantCulture),
            new CreateOrUpdateProductRequest
            {
                Product = new CreateOrUpdateProduct
                {
                    Name = spec.Name,
                    Handle = spec.Handle,
                    Description = spec.Description,
                    PriceInCents = spec.PriceInCents,
                    Interval = 1,
                    IntervalUnit = IntervalUnit.Month,
                    RequireCreditCard = false,
                    ExpirationIntervalUnit = ExpirationIntervalUnit.Never

                    // No trial and no tax code: the trial fields and TaxCode are deliberately left
                    // unset. A setup fee is not settable through this SDK, so there is never one.
                }
            },
            ct: cancellationToken);

        return response.Product;
    }

    /// <summary>
    /// Creates the metered component. For a per-unit pricing scheme the price is expressed as
    /// <c>unit_price</c> in currency units (not cents, not price brackets).
    /// </summary>
    private async Task<Component> CreateMeteredComponentAsync(int familyId, string handle, CancellationToken cancellationToken)
    {
        var response = await _client.Components.CreateMeteredComponent(
            familyId.ToString(CultureInfo.InvariantCulture),
            new MaxioAdvancedBilling.Models.CreateMeteredComponent
            {
                MeteredComponent = new MeteredComponent
                {
                    Name = "API Calls",
                    Handle = handle,
                    UnitName = "call",
                    Description = "Pay-as-you-go API calls billed on the next renewal invoice.",
                    PricingScheme = PricingScheme.PerUnit,
                    UnitPrice = UnitPrice1.String("0.01"),
                    Taxable = false
                }
            },
            ct: cancellationToken);

        return response.Component;
    }

    private static string FormatMoney(long? cents)
        => cents is null ? "?" : (cents.Value / 100m).ToString("C2", CultureInfo.GetCultureInfo("en-US"));

    private sealed record PlanSpec(string Handle, string Name, string Description, long PriceInCents);
}
