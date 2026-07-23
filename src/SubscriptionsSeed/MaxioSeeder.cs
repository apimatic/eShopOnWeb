using System.Globalization;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.AnyOf;
using MaxioAdvancedBilling.Models.Enums;

namespace Microsoft.eShopWeb.SubscriptionsSeed;

/// <summary>
/// Provisions and verifies the UC0 entities on a Maxio site. Idempotent: existing entities that
/// already match the specification are left untouched, and mismatches are reported rather than
/// silently mutated.
/// </summary>
internal sealed class MaxioSeeder
{
    private const int PageSize = 200;
    private const int MaxPages = 25;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly SeedSpecification _specification;
    private readonly bool _verifyOnly;
    private readonly List<string> _problems = new();

    public MaxioSeeder(MaxioAdvancedBillingClient client, SeedSpecification specification, bool verifyOnly)
    {
        _client = client;
        _specification = specification;
        _verifyOnly = verifyOnly;
    }

    /// <summary>Runs the seed/verify pass. Returns true when the site matches the specification.</summary>
    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        var family = await EnsureFamilyAsync(cancellationToken);
        if (family is null)
        {
            Report();
            return false;
        }

        var familyId = family.Id!.Value;
        Console.WriteLine($"  product family  {_specification.FamilyHandle,-18} id {familyId}");

        var products = await ListProductsAsync(familyId, cancellationToken);

        await EnsurePlanAsync(familyId, products, _specification.DefaultPlan, cancellationToken);
        await EnsurePlanAsync(familyId, products, _specification.AlternatePlan, cancellationToken);
        await EnsureMeteredComponentAsync(familyId, cancellationToken);

        Report();

        return _problems.Count == 0;
    }

    private async Task<ProductFamily?> EnsureFamilyAsync(CancellationToken cancellationToken)
    {
        var families = await _client.ProductFamilies.ListProductFamilies(
            dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, ct: cancellationToken);

        var existing = families
            .Select(r => r.ProductFamily)
            .FirstOrDefault(f => f is not null && Matches(f.Handle, _specification.FamilyHandle));

        if (existing is not null)
        {
            if (existing.Id is null)
            {
                _problems.Add($"Product family '{_specification.FamilyHandle}' exists but Maxio returned no id.");
                return null;
            }

            return existing;
        }

        if (_verifyOnly)
        {
            _problems.Add($"Product family '{_specification.FamilyHandle}' does not exist.");
            return null;
        }

        Console.WriteLine($"  creating product family '{_specification.FamilyHandle}'...");

        try
        {
            var created = await _client.ProductFamilies.CreateProductFamily(
                new CreateProductFamilyRequest
                {
                    ProductFamily = new CreateProductFamily
                    {
                        Name = _specification.FamilyName,
                        Handle = _specification.FamilyHandle,
                        Description = "eShopOnWeb Subscribe plans and pay-as-you-go usage."
                    }
                },
                ct: cancellationToken);

            if (created.ProductFamily?.Id is null)
            {
                _problems.Add($"Maxio created product family '{_specification.FamilyHandle}' but returned no id.");
                return null;
            }

            return created.ProductFamily;
        }
        catch (SdkException<CreateProductFamilyError> ex)
        {
            _problems.Add($"Creating product family '{_specification.FamilyHandle}' failed: {Describe(ex.Error, ex.Error.TryGetErrorListResponse1(out var e) ? e.Errors : null)}");
            return null;
        }
    }

    private async Task<List<Product>> ListProductsAsync(int familyId, CancellationToken cancellationToken)
    {
        var products = new List<Product>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var batch = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: familyId.ToString(CultureInfo.InvariantCulture),
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: false,
                include: null,
                page: page,
                perPage: PageSize,
                ct: cancellationToken);

            products.AddRange(batch.Select(r => r.Product));

            if (batch.Count < PageSize)
            {
                break;
            }
        }

        return products;
    }

    private async Task EnsurePlanAsync(int familyId, List<Product> products, PlanSpecification plan, CancellationToken cancellationToken)
    {
        var existing = products.FirstOrDefault(p => Matches(p.Handle, plan.Handle));

        if (existing is not null)
        {
            Console.WriteLine($"  product        {plan.Handle,-18} id {existing.Id}  {FormatMoney(existing.PriceInCents)}/{existing.Interval} {existing.IntervalUnit?.Value}");
            VerifyPlan(existing, plan);
            return;
        }

        if (_verifyOnly)
        {
            _problems.Add($"Product '{plan.Handle}' does not exist in family '{_specification.FamilyHandle}'.");
            return;
        }

        Console.WriteLine($"  creating product '{plan.Handle}'...");

        try
        {
            var created = await _client.Products.CreateProduct(
                productFamilyId: familyId.ToString(CultureInfo.InvariantCulture),
                body: new CreateOrUpdateProductRequest
                {
                    Product = new CreateOrUpdateProduct
                    {
                        Name = plan.Name,
                        Handle = plan.Handle,
                        Description = plan.Description,
                        PriceInCents = plan.PriceInCents,
                        Interval = plan.Interval,
                        IntervalUnit = IntervalUnit.FromValue(plan.IntervalUnit),

                        // No card capture and no 3-DS in the demo subscribe path.
                        RequireCreditCard = false
                    }
                },
                ct: cancellationToken);

            Console.WriteLine($"  product        {plan.Handle,-18} id {created.Product.Id}  {FormatMoney(created.Product.PriceInCents)}");
        }
        catch (SdkException<CreateProductError> ex)
        {
            _problems.Add($"Creating product '{plan.Handle}' failed: {Describe(ex.Error, ex.Error.TryGetErrorListResponse1(out var e) ? e.Errors : null)}");
        }
    }

    private void VerifyPlan(Product product, PlanSpecification plan)
    {
        if (product.PriceInCents != plan.PriceInCents)
        {
            _problems.Add($"Product '{plan.Handle}' is priced at {FormatMoney(product.PriceInCents)} but UC0 specifies {FormatMoney(plan.PriceInCents)}.");
        }

        if (product.Interval != plan.Interval || !Matches(product.IntervalUnit?.Value, plan.IntervalUnit))
        {
            _problems.Add($"Product '{plan.Handle}' bills every {product.Interval} {product.IntervalUnit?.Value} but UC0 specifies every {plan.Interval} {plan.IntervalUnit}.");
        }

        if (product.RequireCreditCard == true)
        {
            _problems.Add($"Product '{plan.Handle}' requires a payment method; UC1's demo subscribe path expects that toggle to be off.");
        }

        if (product.TrialInterval is > 0)
        {
            _problems.Add($"Product '{plan.Handle}' has a trial period; UC0 specifies no trial.");
        }

        if (product.InitialChargeInCents is > 0)
        {
            _problems.Add($"Product '{plan.Handle}' has a setup fee; UC0 specifies none.");
        }
    }

    private async Task EnsureMeteredComponentAsync(int familyId, CancellationToken cancellationToken)
    {
        var specification = _specification.MeteredComponent;
        var components = new List<Component>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var batch = await _client.Components.ListComponentsForProductFamily(
                productFamilyId: familyId,
                includeArchived: false,
                filter: null,
                dateField: null,
                endDate: null,
                endDatetime: null,
                startDate: null,
                startDatetime: null,
                page: page,
                perPage: PageSize,
                ct: cancellationToken);

            components.AddRange(batch.Select(r => r.Component));

            if (batch.Count < PageSize)
            {
                break;
            }
        }

        var existing = components.FirstOrDefault(c => Matches(c.Handle, specification.Handle));

        if (existing is not null)
        {
            Console.WriteLine($"  component      {specification.Handle,-18} id {existing.Id}  kind {existing.Kind?.Value}  {existing.UnitPrice} per {existing.UnitName}");

            if (existing.Kind != ComponentKind.MeteredComponent)
            {
                // A component's kind cannot be changed in place — the operator must archive it and
                // recreate it as metered, so this is reported rather than patched.
                _problems.Add($"Component '{specification.Handle}' is of kind '{existing.Kind?.Value}', not metered. Archive it in the Maxio UI and re-run this tool to recreate it as metered.");
            }

            if (existing.PricingScheme != PricingScheme.PerUnit)
            {
                _problems.Add($"Component '{specification.Handle}' uses pricing scheme '{existing.PricingScheme?.Value}' but UC0 specifies per-unit.");
            }

            return;
        }

        if (_verifyOnly)
        {
            _problems.Add($"Component '{specification.Handle}' does not exist on family '{_specification.FamilyHandle}'.");
            return;
        }

        Console.WriteLine($"  creating metered component '{specification.Handle}'...");

        try
        {
            var created = await _client.Components.CreateMeteredComponent(
                productFamilyId: familyId.ToString(CultureInfo.InvariantCulture),
                body: new CreateMeteredComponent
                {
                    MeteredComponent = new MeteredComponent
                    {
                        Name = specification.Name,
                        Handle = specification.Handle,
                        UnitName = specification.UnitName,
                        PricingScheme = PricingScheme.PerUnit,
                        UnitPrice = UnitPrice1.String(specification.UnitPrice)
                    }
                },
                ct: cancellationToken);

            Console.WriteLine($"  component      {specification.Handle,-18} id {created.Component.Id}  kind {created.Component.Kind?.Value}");
        }
        catch (SdkException<CreateMeteredComponentError> ex)
        {
            _problems.Add($"Creating metered component '{specification.Handle}' failed: {Describe(ex.Error, ex.Error.TryGetErrorListResponse1(out var e) ? e.Errors : null)}");
        }
    }

    private void Report()
    {
        Console.WriteLine();

        if (_problems.Count == 0)
        {
            Console.WriteLine("UC0 verified: the site matches the specification.");
            return;
        }

        Console.WriteLine($"UC0 found {_problems.Count} problem(s):");
        foreach (var problem in _problems)
        {
            Console.WriteLine($"  - {problem}");
        }
    }

    private static bool Matches(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string FormatMoney(long? cents) =>
        cents.HasValue ? (cents.Value / 100m).ToString("C", CultureInfo.GetCultureInfo("en-US")) : "<no price>";

    private static string Describe(ApiError error, IReadOnlyList<string>? messages)
    {
        if (messages is { Count: > 0 })
        {
            return string.Join("; ", messages);
        }

        return error.TryGetRawError(out var raw)
            ? $"HTTP {(int)raw.StatusCode} {raw.ReadAsString()}"
            : "no diagnosable error body";
    }
}
