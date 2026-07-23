using System.Globalization;
using System.Net;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Maxio = MaxioAdvancedBilling.Models;
using MaxioEnums = MaxioAdvancedBilling.Models.Enums;
using MaxioUnions = MaxioAdvancedBilling.Models.AnyOf;

namespace Microsoft.eShopWeb.SubscriptionsSeed;

/// <summary>
/// Provisions the billing-provider entities the subscription integration expects (plan.md UC0).
/// </summary>
/// <remarks>
/// Idempotent by design: every entity is probed by its durable handle first and only created when it is
/// genuinely absent, because none of the provider's create operations is itself idempotent. An entity
/// that exists but does not match the expected shape is reported rather than silently reused.
/// </remarks>
internal sealed class CatalogSeeder
{
    /// <summary>The plans the integration expects, in the shape UC0 specifies.</summary>
    private static readonly PlanSpecification[] Plans =
    {
        new("Pro Plan", 29_900),
        new("Basic Plan", 2_900)
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;

    public CatalogSeeder(MaxioAdvancedBillingClient client, MaxioSettings settings)
    {
        _client = client;
        _settings = settings;
    }

    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        var familyId = await EnsureProductFamilyAsync(cancellationToken);

        var planHandles = new[] { _settings.DefaultProductHandle, _settings.AlternateProductHandle };
        for (var index = 0; index < planHandles.Length; index++)
        {
            await EnsurePlanAsync(familyId, planHandles[index], Plans[index], cancellationToken);
        }

        await EnsureMeteredComponentAsync(familyId, cancellationToken);

        return await VerifyAsync(familyId, cancellationToken);
    }

    private async Task<int> EnsureProductFamilyAsync(CancellationToken cancellationToken)
    {
        var handle = _settings.ProductFamilyHandle;

        var families = await _client.ProductFamilies.ListProductFamilies(
            dateField: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null,
            ct: cancellationToken);

        var existing = families
            .Select(response => response.ProductFamily)
            .FirstOrDefault(family => string.Equals(family?.Handle, handle, StringComparison.OrdinalIgnoreCase));

        if (existing?.Id is { } id)
        {
            Report.Unchanged($"product family '{handle}' already exists (id {id})");
            return id;
        }

        var created = await _client.ProductFamilies.CreateProductFamily(
            new Maxio.CreateProductFamilyRequest
            {
                ProductFamily = new Maxio.CreateProductFamily
                {
                    Name = "eShopSubscribe",
                    Handle = handle,
                    Description = "Recurring subscription plans for the eShopOnWeb storefront."
                }
            },
            ct: cancellationToken);

        var createdId = created.ProductFamily?.Id
            ?? throw new SeedException($"The provider created product family '{handle}' but returned no id.");

        Report.Created($"product family '{handle}' (id {createdId})");
        return createdId;
    }

    private async Task EnsurePlanAsync(
        int familyId,
        string handle,
        PlanSpecification specification,
        CancellationToken cancellationToken)
    {
        var existing = await TryReadProductAsync(handle, cancellationToken);
        if (existing is not null)
        {
            VerifyPlanShape(handle, existing, specification);
            Report.Unchanged($"plan '{handle}' already exists (id {existing.Id})");
            return;
        }

        var created = await _client.Products.CreateProduct(
            familyId.ToString(CultureInfo.InvariantCulture),
            new Maxio.CreateOrUpdateProductRequest
            {
                Product = new Maxio.CreateOrUpdateProduct
                {
                    Name = specification.Name,
                    Handle = handle,
                    Description = $"{specification.Name} for eShopOnWeb Subscribe.",
                    PriceInCents = specification.PriceInCents,
                    Interval = 1,
                    IntervalUnit = MaxioEnums.IntervalUnit.Month,

                    // The demo enrols without card capture, so a payment method must not be required.
                    RequireCreditCard = false

                    // No trial and no expiry: those fields are deliberately left unset rather than zeroed.
                }
            },
            ct: cancellationToken);

        Report.Created($"plan '{handle}' (id {created.Product.Id}) at " +
                       $"${specification.PriceInCents / 100m:N2} / month");
    }

    private async Task EnsureMeteredComponentAsync(int familyId, CancellationToken cancellationToken)
    {
        var handle = _settings.MeteredComponentHandle;

        var existing = await TryFindComponentAsync(handle, cancellationToken);
        if (existing is not null)
        {
            // A component's kind cannot be changed in place, so a mismatch is reported for the operator
            // to archive and recreate rather than being papered over.
            if (!string.Equals(existing.Kind?.Value, MaxioEnums.ComponentKind.MeteredComponent.Value,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new SeedException(
                    $"Component '{handle}' exists but its kind is '{existing.Kind?.Value ?? "unknown"}', " +
                    "not metered. A component cannot be converted in place — archive it in the Maxio UI " +
                    "and re-run this seeder to recreate it as metered.");
            }

            if (existing.ProductFamilyId is { } ownerId && ownerId != familyId)
            {
                throw new SeedException(
                    $"Component '{handle}' exists on product family {ownerId}, not {familyId}, so it is " +
                    "not available to the seeded plans. Archive it and re-run this seeder.");
            }

            Report.Unchanged($"metered component '{handle}' already exists (id {existing.Id})");
            return;
        }

        var created = await _client.Components.CreateMeteredComponent(
            familyId.ToString(CultureInfo.InvariantCulture),
            new Maxio.CreateMeteredComponent
            {
                MeteredComponent = new Maxio.MeteredComponent
                {
                    Name = "API Calls",
                    Handle = handle,
                    UnitName = "call",
                    PricingScheme = MaxioEnums.PricingScheme.PerUnit,

                    // Dollars, as a string, so the price is not distorted by binary floating point.
                    UnitPrice = MaxioUnions.UnitPrice1.String("0.01"),
                    Taxable = false
                }
            },
            ct: cancellationToken);

        Report.Created($"metered component '{handle}' (id {created.Component.Id}) at $0.01 / call");
    }

    /// <summary>Reads the seeded catalogue back and confirms it matches what the integration expects.</summary>
    private async Task<bool> VerifyAsync(int familyId, CancellationToken cancellationToken)
    {
        Console.WriteLine();
        Console.WriteLine($"Verifying product family '{_settings.ProductFamilyHandle}' (id {familyId})...");

        var healthy = true;

        var products = await _client.ProductFamilies.ListProductsForProductFamily(
            familyId.ToString(CultureInfo.InvariantCulture),
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

        foreach (var handle in new[] { _settings.DefaultProductHandle, _settings.AlternateProductHandle })
        {
            var product = products
                .Select(response => response.Product)
                .FirstOrDefault(p => string.Equals(p.Handle, handle, StringComparison.OrdinalIgnoreCase));

            if (product is null)
            {
                Report.Problem($"plan '{handle}' is not listed under the product family");
                healthy = false;
                continue;
            }

            Console.WriteLine(
                $"  plan  {handle,-16} id={product.Id,-10} " +
                $"${(product.PriceInCents ?? 0) / 100m,8:N2} / {product.Interval} {product.IntervalUnit?.Value} " +
                $"requiresPaymentMethod={product.RequireCreditCard}");

            if (product.RequireCreditCard == true)
            {
                Report.Problem($"plan '{handle}' requires a payment method; subscribing will demand card capture");
                healthy = false;
            }
        }

        var component = await TryFindComponentAsync(_settings.MeteredComponentHandle, cancellationToken);
        if (component is null)
        {
            Report.Problem($"metered component '{_settings.MeteredComponentHandle}' does not resolve");
            healthy = false;
        }
        else
        {
            Console.WriteLine(
                $"  usage {component.Handle,-16} id={component.Id,-10} kind={component.Kind?.Value} " +
                $"scheme={component.PricingScheme?.Value} unitPrice={component.UnitPrice ?? "n/a"}");

            if (!string.Equals(component.Kind?.Value, MaxioEnums.ComponentKind.MeteredComponent.Value,
                    StringComparison.OrdinalIgnoreCase))
            {
                Report.Problem($"component '{component.Handle}' is not of metered kind, so usage cannot be recorded");
                healthy = false;
            }
        }

        return healthy;
    }

    private static void VerifyPlanShape(string handle, Maxio.Product product, PlanSpecification specification)
    {
        if (product.PriceInCents != specification.PriceInCents)
        {
            Report.Warning(
                $"plan '{handle}' is priced at ${(product.PriceInCents ?? 0) / 100m:N2}, " +
                $"not the expected ${specification.PriceInCents / 100m:N2}");
        }

        if (product.RequireCreditCard == true)
        {
            Report.Warning($"plan '{handle}' requires a payment method; UC1 will demand card capture");
        }
    }

    private async Task<Maxio.Product?> TryReadProductAsync(string handle, CancellationToken cancellationToken)
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

    private async Task<Maxio.Component?> TryFindComponentAsync(string handle, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Components.FindComponent(handle, ct: cancellationToken);
            return response.Component;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private sealed record PlanSpecification(string Name, long PriceInCents);
}

/// <summary>A seeding step could not be completed and needs an operator decision.</summary>
internal sealed class SeedException : Exception
{
    public SeedException(string message) : base(message)
    {
    }
}

internal static class Report
{
    public static void Created(string what) => Write(ConsoleColor.Green, "created ", what);

    public static void Unchanged(string what) => Write(ConsoleColor.DarkGray, "ok      ", what);

    public static void Warning(string what) => Write(ConsoleColor.Yellow, "warning ", what);

    public static void Problem(string what) => Write(ConsoleColor.Red, "problem ", what);

    private static void Write(ConsoleColor color, string prefix, string what)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine($"  {prefix}{what}");
        Console.ForegroundColor = previous;
    }
}
