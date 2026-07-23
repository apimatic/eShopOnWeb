using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.AnyOf;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.Infrastructure.Services;

namespace Microsoft.eShopWeb.SubscriptionsSeed;

/// <summary>
/// Provisions and verifies the billing entities UC1–UC4 depend on: the product family, the two
/// recurring plans, and the metered component.
/// </summary>
/// <remarks>
/// The tool is idempotent — an entity that already exists and matches the expected shape is left
/// alone. An entity that exists but does <b>not</b> match is reported rather than silently mutated,
/// because reusing a mismatched entity is exactly the failure UC0 warns about.
/// </remarks>
public class SeedRunner
{
    private const string HandlePrefix = "handle:";
    private const string MeteredKind = "metered_component";

    private readonly SeedConfiguration _configuration;
    private readonly SeedOptions _options;
    private readonly MaxioAdvancedBillingClient _client;
    private readonly HttpClient _httpClient;
    private readonly List<string> _problems = new();

    public SeedRunner(SeedConfiguration configuration, SeedOptions options)
    {
        _configuration = configuration;
        _options = options;
        _httpClient = new HttpClient();
        _client = new MaxioAdvancedBillingClient(_httpClient, MaxioClientOptionsFactory.Create(configuration.Provider));
    }

    /// <summary>The plans UC1 and UC3 operate on, with the prices plan.md specifies, in cents.</summary>
    private IReadOnlyList<PlanSpec> PlanSpecs => new[]
    {
        new PlanSpec(_configuration.Catalog.DefaultProductHandle, "Pro Plan",
            "eShopOnWeb Pro subscription — full storefront access.", 29_900L),
        new PlanSpec(_configuration.Catalog.AlternateProductHandle, "Basic Plan",
            "eShopOnWeb Basic subscription — entry level storefront access.", 2_900L)
    };

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"Target server : {_configuration.Provider.ResolveBaseUrl()}");
        Console.WriteLine($"Region        : {_configuration.Provider.Environment}");
        Console.WriteLine($"Mode          : {(_options.VerifyOnly ? "verify only" : "provision + verify")}");
        Console.WriteLine();

        try
        {
            var family = await EnsureProductFamilyAsync(cancellationToken);
            if (family is null)
            {
                return Report();
            }

            foreach (var spec in PlanSpecs)
            {
                await EnsurePlanAsync(family, spec, cancellationToken);
            }

            await EnsureMeteredComponentAsync(family, cancellationToken);
            await VerifyAsync(family, cancellationToken);
        }
        finally
        {
            _httpClient.Dispose();
        }

        return Report();
    }

    private async Task<ProductFamily?> EnsureProductFamilyAsync(CancellationToken cancellationToken)
    {
        var handle = _configuration.Catalog.ProductFamilyHandle;
        var existing = await FindProductFamilyAsync(handle, cancellationToken);

        if (existing is not null)
        {
            Console.WriteLine($"[ok]      product family '{handle}' exists (id {existing.Id}).");
            return existing;
        }

        if (_options.VerifyOnly)
        {
            _problems.Add($"Product family '{handle}' does not exist.");
            return null;
        }

        var response = await _client.ProductFamilies.CreateProductFamily(new CreateProductFamilyRequest
        {
            ProductFamily = new CreateProductFamily
            {
                Name = "eShopSubscribe",
                Handle = handle,
                Description = "Recurring plans and metered usage for the eShopOnWeb storefront."
            }
        }, ct: cancellationToken);

        var created = response.ProductFamily;
        if (created is null)
        {
            _problems.Add($"Product family '{handle}' was created but the provider returned no body.");
            return null;
        }

        Console.WriteLine($"[created] product family '{handle}' (id {created.Id}).");
        return created;
    }

    private async Task<ProductFamily?> FindProductFamilyAsync(string handle, CancellationToken cancellationToken)
    {
        // The read-by-id operation takes an int, so a handle is resolved by listing and matching.
        var families = await _client.ProductFamilies.ListProductFamilies(
            dateField: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null,
            ct: cancellationToken);

        return families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f is not null && string.Equals(f.Handle, handle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task EnsurePlanAsync(ProductFamily family, PlanSpec spec, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(spec.Handle))
        {
            _problems.Add($"No handle is configured for the '{spec.Name}' plan.");
            return;
        }

        var existing = await FindProductAsync(spec.Handle, cancellationToken);

        if (existing is not null)
        {
            Console.WriteLine($"[ok]      plan '{spec.Handle}' exists (id {existing.Id}, {Money(existing.PriceInCents)}, " +
                              $"require_credit_card={Flag(existing.RequireCreditCard)}, " +
                              $"request_credit_card={Flag(existing.RequestCreditCard)}, " +
                              $"taxable={Flag(existing.Taxable)}).");
            InspectExistingPlan(family, spec, existing);
            return;
        }

        if (_options.VerifyOnly)
        {
            _problems.Add($"Plan '{spec.Handle}' does not exist.");
            return;
        }

        var response = await _client.Products.CreateProduct(
            productFamilyId: HandlePrefix + family.Handle,
            body: new CreateOrUpdateProductRequest
            {
                Product = new CreateOrUpdateProduct
                {
                    Name = spec.Name,
                    Handle = spec.Handle,
                    Description = spec.Description,
                    PriceInCents = spec.PriceInCents,
                    Interval = 1,
                    IntervalUnit = IntervalUnit.Month,
                    // No card capture in the demo path, so enrollment never triggers 3-DS.
                    RequireCreditCard = false
                }
            },
            ct: cancellationToken);

        Console.WriteLine($"[created] plan '{spec.Handle}' (id {response.Product.Id}, {Money(response.Product.PriceInCents)}).");
    }

    /// <summary>
    /// Reports — without changing anything — the ways an existing plan can differ from what the
    /// integration expects. Silently reusing a mismatched entity is the failure UC0 calls out.
    /// </summary>
    private void InspectExistingPlan(ProductFamily family, PlanSpec spec, Product existing)
    {
        if (existing.PriceInCents != spec.PriceInCents)
        {
            _problems.Add($"Plan '{spec.Handle}' is priced {Money(existing.PriceInCents)}, " +
                          $"expected {Money(spec.PriceInCents)}.");
        }

        if (existing.ProductFamily?.Id != family.Id)
        {
            _problems.Add($"Plan '{spec.Handle}' belongs to product family " +
                          $"'{existing.ProductFamily?.Handle ?? "unknown"}', expected '{family.Handle}'.");
        }

        if (existing.RequireCreditCard == true)
        {
            _problems.Add($"Plan '{spec.Handle}' has 'require credit card' switched on, so subscribing will " +
                          "demand card capture. Switch it off in the Maxio UI for the demo path.");
        }

        if (existing.ArchivedAt.HasValue)
        {
            _problems.Add($"Plan '{spec.Handle}' is archived and cannot be subscribed to.");
        }

        if (existing.TrialIntervalUnit is not null || (existing.TrialPriceInCents ?? 0L) != 0L)
        {
            _problems.Add($"Plan '{spec.Handle}' has a trial configured; first-invoice expectations will not match.");
        }

        if ((existing.InitialChargeInCents ?? 0L) != 0L)
        {
            _problems.Add($"Plan '{spec.Handle}' has a setup fee of {Money(existing.InitialChargeInCents)}; " +
                          "first-invoice expectations will not match.");
        }
    }

    private async Task<Product?> FindProductAsync(string handle, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Products.ReadProductByHandle(handle, ct: cancellationToken);
            return response.Product;
        }
        catch (SdkException<RawError> ex) when (IsAbsent(ex.Error.StatusCode))
        {
            return null;
        }
    }

    private async Task EnsureMeteredComponentAsync(ProductFamily family, CancellationToken cancellationToken)
    {
        var handle = _configuration.Catalog.MeteredComponentHandle;
        if (string.IsNullOrWhiteSpace(handle))
        {
            _problems.Add("No metered component handle is configured.");
            return;
        }

        var existing = await FindComponentAsync(handle, cancellationToken);

        if (existing is not null)
        {
            var kind = existing.Kind?.Value ?? "unknown";

            if (string.Equals(kind, MeteredKind, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[ok]      component '{handle}' exists (id {existing.Id}, {kind}, " +
                                  $"{Money(UnitPriceInCents(existing))}/unit).");
                InspectExistingComponent(family, existing);
                return;
            }

            // A component's kind cannot be converted in place — it must be archived and recreated.
            if (_options.VerifyOnly || !_options.RecreateComponent)
            {
                _problems.Add($"Component '{handle}' is of kind '{kind}', not '{MeteredKind}'. " +
                              "A kind cannot be changed in place — rerun with --recreate-component to archive " +
                              "it and create a metered replacement.");
                return;
            }

            await _client.Components.ArchiveComponent(family.Id ?? 0, HandlePrefix + handle, ct: cancellationToken);
            Console.WriteLine($"[archived] component '{handle}' (was '{kind}').");
        }
        else if (_options.VerifyOnly)
        {
            _problems.Add($"Metered component '{handle}' does not exist.");
            return;
        }

        var response = await _client.Components.CreateMeteredComponent(
            productFamilyId: HandlePrefix + family.Handle,
            body: new CreateMeteredComponent
            {
                MeteredComponent = new MeteredComponent
                {
                    Name = "API Calls",
                    Handle = handle,
                    UnitName = "call",
                    Description = "Pay-as-you-go API calls billed on the next renewal invoice.",
                    PricingScheme = PricingScheme.PerUnit,
                    UnitPrice = UnitPrice1.Double(0.01d)
                }
            },
            ct: cancellationToken);

        Console.WriteLine($"[created] component '{handle}' (id {response.Component.Id}, " +
                          $"{response.Component.Kind?.Value}, {Money(UnitPriceInCents(response.Component))}/unit).");
    }

    private void InspectExistingComponent(ProductFamily family, Component existing)
    {
        if (existing.ProductFamilyId != family.Id)
        {
            _problems.Add($"Component '{existing.Handle}' is on product family " +
                          $"'{existing.ProductFamilyHandle ?? "unknown"}', expected '{family.Handle}'. " +
                          "It will not be available to this integration's subscriptions.");
        }

        if (UnitPriceInCents(existing) != 1L)
        {
            _problems.Add($"Component '{existing.Handle}' is priced {Money(UnitPriceInCents(existing))}/unit, " +
                          "expected $0.01/unit.");
        }

        if (existing.Archived == true)
        {
            _problems.Add($"Component '{existing.Handle}' is archived and cannot accrue usage.");
        }
    }

    private async Task<Component?> FindComponentAsync(string handle, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Components.FindComponent(handle, ct: cancellationToken);
            return response.Component;
        }
        catch (SdkException<RawError> ex) when (IsAbsent(ex.Error.StatusCode))
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the seed back the way the integration will: the family's products must include both
    /// plans, and the component must resolve as metered at the expected unit price.
    /// </summary>
    private async Task VerifyAsync(ProductFamily family, CancellationToken cancellationToken)
    {
        Console.WriteLine();
        Console.WriteLine("Verifying the seed by reading it back...");

        var products = await _client.ProductFamilies.ListProductsForProductFamily(
            productFamilyId: HandlePrefix + family.Handle,
            dateField: null,
            filter: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null,
            includeArchived: false,
            include: null,
            page: 1,
            perPage: 200,
            ct: cancellationToken);

        var handles = products
            .Select(p => p.Product?.Handle)
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .ToList();

        foreach (var spec in PlanSpecs)
        {
            if (handles.Contains(spec.Handle, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[verified] plan '{spec.Handle}' is listed under '{family.Handle}'.");
            }
            else
            {
                _problems.Add($"Plan '{spec.Handle}' is not listed under product family '{family.Handle}'.");
            }
        }

        var component = await FindComponentAsync(_configuration.Catalog.MeteredComponentHandle, cancellationToken);
        if (component is null)
        {
            _problems.Add($"Component '{_configuration.Catalog.MeteredComponentHandle}' could not be read back.");
            return;
        }

        var kind = component.Kind?.Value ?? "unknown";
        if (!string.Equals(kind, MeteredKind, StringComparison.OrdinalIgnoreCase))
        {
            _problems.Add($"Component '{component.Handle}' read back as kind '{kind}', expected '{MeteredKind}'.");
            return;
        }

        // The write side takes a decimal unit price while the read side reports cents, so the
        // magnitude is only trustworthy once it has been read back.
        var unitPrice = UnitPriceInCents(component);
        if (unitPrice != 1L)
        {
            _problems.Add($"Component '{component.Handle}' read back at {Money(unitPrice)}/unit, " +
                          "expected $0.01/unit.");
            return;
        }

        Console.WriteLine($"[verified] component '{component.Handle}' is metered at $0.01/unit " +
                          $"(id {component.Id}).");
    }

    private int Report()
    {
        Console.WriteLine();

        if (_problems.Count == 0)
        {
            Console.WriteLine("Seed is complete and matches the integration's expectations.");
            Console.WriteLine();
            Console.WriteLine("Note: the provider's product API does not expose a 'taxable' flag on create, so that " +
                              "setting follows the Maxio site default and must be adjusted in the Maxio UI if needed.");
            return 0;
        }

        Console.Error.WriteLine($"Seed has {_problems.Count} problem(s):");
        foreach (var problem in _problems)
        {
            Console.Error.WriteLine($"  - {problem}");
        }

        return 1;
    }

    private static bool IsAbsent(HttpStatusCode statusCode)
    {
        if (statusCode == HttpStatusCode.Unauthorized || statusCode == HttpStatusCode.Forbidden)
        {
            return false;
        }

        var code = (int)statusCode;
        return code >= 400 && code < 500;
    }

    /// <summary>
    /// Reads the component's unit price through the same translation the running application uses,
    /// so the tool can never report a price the integration would not actually see.
    /// </summary>
    private static long? UnitPriceInCents(Component component)
    {
        return MaxioModelMapper.ToMeteredComponent(component).PricePerUnitInCents;
    }

    private static string Flag(bool? value) => value?.ToString().ToLowerInvariant() ?? "unset";

    private static string Money(long? cents)
    {
        return cents.HasValue
            ? (cents.Value / 100m).ToString("C", System.Globalization.CultureInfo.GetCultureInfo("en-US"))
            : "unknown";
    }

    /// <summary>One recurring plan the seed must guarantee.</summary>
    private sealed record PlanSpec(string Handle, string Name, string Description, long PriceInCents);
}
