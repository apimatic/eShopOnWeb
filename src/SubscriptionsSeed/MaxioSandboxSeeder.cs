using System.Globalization;
using System.Net;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.AnyOf;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.Infrastructure.Configuration;

namespace Microsoft.eShopWeb.SubscriptionsSeed;

/// <summary>
/// Provisions and verifies the billing-provider entities the integration depends on (plan.md UC0).
/// </summary>
/// <remarks>
/// <para>
/// This is operator tooling. It is never reachable from the storefront or the API, and it is the
/// only code in the repository that creates catalog entities — the application itself treats the
/// plan catalog as read-only.
/// </para>
/// <para>
/// Every step is idempotent on the configured handle, so it is safe to re-run: an entity that
/// already matches is reported and left alone. Correcting a mis-created entity archives and
/// recreates it rather than mutating it, and only when explicitly asked, because archiving is
/// irreversible.
/// </para>
/// </remarks>
internal sealed class MaxioSandboxSeeder
{
    private readonly MaxioAdvancedBillingClient _maxio;
    private readonly MaxioSettings _settings;
    private readonly bool _repair;
    private readonly List<string> _problems = new();

    internal MaxioSandboxSeeder(MaxioAdvancedBillingClient maxio, MaxioSettings settings, bool repair)
    {
        _maxio = maxio;
        _settings = settings;
        _repair = repair;
    }

    /// <summary>Runs the seed and verification. Returns true when the sandbox matches the spec.</summary>
    internal async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        var familyHandle = Require(_settings.ProductFamilyHandle, "Maxio:ProductFamilyHandle");
        var primaryHandle = Require(_settings.DefaultProductHandle, "Maxio:DefaultProductHandle");
        var alternateHandle = Require(_settings.AlternateProductHandle, "Maxio:AlternateProductHandle");
        var componentHandle = Require(_settings.MeteredComponentHandle, "Maxio:MeteredComponentHandle");

        Console.WriteLine($"Seeding Maxio site '{_settings.Subdomain}' at {_settings.ResolveBaseUrl()}");
        Console.WriteLine();

        var familyId = await EnsureProductFamilyAsync(familyHandle, cancellationToken);

        await EnsurePlanAsync(familyId, primaryHandle, SeedCatalog.PrimaryPlan, cancellationToken);
        await EnsurePlanAsync(familyId, alternateHandle, SeedCatalog.AlternatePlan, cancellationToken);

        await EnsureMeteredComponentAsync(familyId, componentHandle, cancellationToken);

        Console.WriteLine();
        await VerifyAsync(familyId, familyHandle, componentHandle, cancellationToken);

        return _problems.Count == 0;
    }

    internal IReadOnlyList<string> Problems => _problems;

    private async Task<int> EnsureProductFamilyAsync(string handle, CancellationToken cancellationToken)
    {
        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await _maxio.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw new InvalidOperationException($"Could not list product families: {Describe(ex.Error)}");
        }

        var existing = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f is not null &&
                                 string.Equals(f.Handle, handle, StringComparison.OrdinalIgnoreCase) &&
                                 !f.ArchivedAt.HasValue);

        if (existing?.Id is int existingId && existingId > 0)
        {
            Console.WriteLine($"  [ok]      product family '{handle}' already exists (id {existingId}).");
            return existingId;
        }

        ProductFamilyResponse created;
        try
        {
            created = await _maxio.ProductFamilies.CreateProductFamily(
                body: new CreateProductFamilyRequest
                {
                    ProductFamily = new CreateProductFamily
                    {
                        Name = SeedCatalog.ProductFamilyName,
                        Handle = handle,
                        Description = "Recurring plans and metered add-ons for eShopOnWeb Subscribe."
                    }
                },
                ct: cancellationToken);
        }
        catch (SdkException<CreateProductFamilyError> ex)
        {
            throw new InvalidOperationException($"Could not create product family '{handle}': {Describe(ex.Error)}");
        }

        if (created.ProductFamily?.Id is not int newId || newId <= 0)
        {
            throw new InvalidOperationException(
                $"Maxio accepted the product family '{handle}' but returned no id.");
        }

        Console.WriteLine($"  [created] product family '{handle}' (id {newId}).");
        return newId;
    }

    private async Task EnsurePlanAsync(int familyId,
        string handle,
        SeedCatalog.SeedPlan plan,
        CancellationToken cancellationToken)
    {
        var existing = await FindProductAsync(familyId, handle, cancellationToken);

        if (existing is not null)
        {
            Console.WriteLine($"  [ok]      plan '{handle}' already exists (id {existing.Id}).");
            ReportPlanDrift(handle, plan, existing);
            return;
        }

        ProductResponse created;
        try
        {
            created = await _maxio.Products.CreateProduct(
                productFamilyId: familyId.ToString(CultureInfo.InvariantCulture),
                body: new CreateOrUpdateProductRequest
                {
                    Product = new CreateOrUpdateProduct
                    {
                        Name = plan.Name,
                        Handle = handle,
                        Description = plan.Description,
                        PriceInCents = plan.PriceInCents,
                        Interval = 1,
                        IntervalUnit = IntervalUnit.Month,

                        // The demo enrols without card capture, so no payment method is demanded.
                        RequireCreditCard = false

                        // No trial, no expiry and no tax code: each is expressed by leaving the
                        // corresponding fields unset. A setup fee is not expressible on create.
                    }
                },
                ct: cancellationToken);
        }
        catch (SdkException<CreateProductError> ex)
        {
            _problems.Add($"Could not create plan '{handle}': {Describe(ex.Error)}");
            return;
        }

        Console.WriteLine(
            $"  [created] plan '{handle}' (id {created.Product.Id}) at ${plan.PriceInDollars:N2}/month.");
    }

    private async Task EnsureMeteredComponentAsync(int familyId,
        string handle,
        CancellationToken cancellationToken)
    {
        var existing = await FindComponentAsync(handle, cancellationToken);

        if (existing is not null)
        {
            var isMetered = existing.Kind is not null && existing.Kind == ComponentKind.MeteredComponent;
            var onCorrectFamily = existing.ProductFamilyId is null || existing.ProductFamilyId == familyId;

            if (isMetered && onCorrectFamily && !(existing.Archived ?? false) && !existing.ArchivedAt.HasValue)
            {
                Console.WriteLine($"  [ok]      metered component '{handle}' already exists (id {existing.Id}).");
                return;
            }

            var fault = !isMetered
                ? $"it is of kind '{existing.Kind?.Value ?? "unknown"}' instead of metered"
                : !onCorrectFamily
                    ? $"it lives on product family {existing.ProductFamilyId} instead of {familyId}"
                    : "it is archived";

            if (!_repair)
            {
                _problems.Add(
                    $"Component '{handle}' exists but {fault}. A component's kind and family cannot be " +
                    "changed in place. Re-run with --repair to archive it and recreate it correctly.");
                return;
            }

            await ArchiveComponentAsync(familyId, handle, existing, cancellationToken);
        }

        ComponentResponse created;
        try
        {
            created = await _maxio.Components.CreateMeteredComponent(
                productFamilyId: familyId.ToString(CultureInfo.InvariantCulture),
                body: new MaxioAdvancedBilling.Models.CreateMeteredComponent
                {
                    MeteredComponent = new MeteredComponent
                    {
                        Name = SeedCatalog.MeteredComponent.Name,
                        Handle = handle,
                        UnitName = SeedCatalog.MeteredComponent.UnitName,
                        PricingScheme = PricingScheme.PerUnit,

                        // Dollars, as a decimal string: the string variant avoids binary-float
                        // rounding on a rate as small as a cent.
                        UnitPrice = UnitPrice1.String(SeedCatalog.MeteredComponent.UnitPrice),
                        Taxable = false
                    }
                },
                ct: cancellationToken);
        }
        catch (SdkException<CreateMeteredComponentError> ex)
        {
            _problems.Add($"Could not create metered component '{handle}': {Describe(ex.Error)}");
            return;
        }

        Console.WriteLine(
            $"  [created] metered component '{handle}' (id {created.Component.Id}) at " +
            $"${SeedCatalog.MeteredComponent.UnitPrice}/unit.");
    }

    private async Task ArchiveComponentAsync(int familyId,
        string handle,
        Component existing,
        CancellationToken cancellationToken)
    {
        // Archive by the id Maxio actually assigned; the handle form would resolve to the same
        // component but the id is unambiguous once we already hold the record.
        var componentId = existing.Id?.ToString(CultureInfo.InvariantCulture) ?? $"handle:{handle}";
        var archiveFamilyId = existing.ProductFamilyId ?? familyId;

        try
        {
            await _maxio.Components.ArchiveComponent(
                productFamilyId: archiveFamilyId,
                componentId: componentId,
                ct: cancellationToken);

            Console.WriteLine($"  [archived] component '{handle}' (id {existing.Id}) so it can be recreated.");
        }
        catch (SdkException<ArchiveComponentError> ex)
        {
            _problems.Add($"Could not archive the mismatched component '{handle}': {Describe(ex.Error)}");
        }
    }

    private async Task VerifyAsync(int familyId,
        string familyHandle,
        string componentHandle,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Verifying product family '{familyHandle}' (id {familyId}):");

        IReadOnlyList<ProductResponse> products;
        try
        {
            products = await ListProductsAsync(familyId, cancellationToken);
        }
        catch (Exception ex)
        {
            _problems.Add($"Could not read the product family back: {ex.Message}");
            return;
        }

        foreach (var product in products.Select(p => p.Product).Where(p => !p.ArchivedAt.HasValue))
        {
            var price = (product.PriceInCents ?? 0L) / 100m;
            Console.WriteLine(
                $"  product   {product.Handle,-16} id {product.Id,-10} $ {price,10:N2} / " +
                $"{product.Interval} {product.IntervalUnit?.Value} " +
                $"(require_credit_card: {product.RequireCreditCard ?? false}, " +
                $"request_credit_card: {product.RequestCreditCard ?? false})");
        }

        var component = await FindComponentAsync(componentHandle, cancellationToken);
        if (component is null)
        {
            _problems.Add($"Metered component '{componentHandle}' could not be read back.");
            return;
        }

        Console.WriteLine(
            $"  component {component.Handle,-16} id {component.Id,-10} " +
            $"kind {component.Kind?.Value} scheme {component.PricingScheme?.Value} " +
            $"unit price {component.UnitPrice ?? "(unset)"}");

        if (component.Kind is null || component.Kind != ComponentKind.MeteredComponent)
        {
            _problems.Add(
                $"Component '{componentHandle}' is not metered, so pay-as-you-go usage cannot be recorded.");
        }
    }

    private async Task<Product?> FindProductAsync(int familyId, string handle, CancellationToken cancellationToken)
    {
        var products = await ListProductsAsync(familyId, cancellationToken);

        return products
            .Select(p => p.Product)
            .FirstOrDefault(p => string.Equals(p.Handle, handle, StringComparison.OrdinalIgnoreCase) &&
                                 !p.ArchivedAt.HasValue);
    }

    private async Task<IReadOnlyList<ProductResponse>> ListProductsAsync(int familyId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _maxio.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: familyId.ToString(CultureInfo.InvariantCulture),
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
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            throw new InvalidOperationException($"Could not list the family's products: {Describe(ex.Error)}");
        }
    }

    private async Task<Component?> FindComponentAsync(string handle, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _maxio.Components.FindComponent(handle: handle, ct: cancellationToken);
            return response.Component;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw new InvalidOperationException($"Could not look up component '{handle}': {Describe(ex.Error)}");
        }
    }

    /// <summary>
    /// Reports where an existing plan differs from the specification. The seed never mutates a plan
    /// in place — an operator decides whether the difference is intentional.
    /// </summary>
    private void ReportPlanDrift(string handle, SeedCatalog.SeedPlan expected, Product actual)
    {
        var actualPrice = actual.PriceInCents ?? 0L;
        if (actualPrice != expected.PriceInCents)
        {
            Console.WriteLine(
                $"            note: '{handle}' is ${actualPrice / 100m:N2}, the spec says ${expected.PriceInDollars:N2}.");
        }

        // require_credit_card is the flag that refuses an enrolment without a payment profile.
        // request_credit_card only makes the hosted signup page show a card field, which the
        // storefront never uses, so it is reported but not treated as a problem.
        if (actual.RequireCreditCard ?? false)
        {
            _problems.Add(
                $"Plan '{handle}' requires a payment method, so subscribing will demand card capture. " +
                "Turn the toggle off in Maxio, or expect UC1 to need the card-capture path.");
        }
        else if (actual.RequestCreditCard ?? false)
        {
            Console.WriteLine(
                $"            note: '{handle}' asks for a card on Maxio's hosted signup page; " +
                "the eShopOnWeb storefront does not use that page, so enrolment is unaffected.");
        }

        if (actual.TrialIntervalUnit is not null || (actual.TrialPriceInCents ?? 0L) > 0L)
        {
            _problems.Add($"Plan '{handle}' has a trial period, so first-invoice expectations will not match.");
        }
    }

    private static string Require(string? value, string key) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"'{key}' is not configured.")
            : value;

    private static string Describe(RawError raw)
    {
        try
        {
            var body = raw.ReadAsString();
            return string.IsNullOrWhiteSpace(body) ? raw.StatusCode.ToString() : $"{(int)raw.StatusCode}: {body}";
        }
        catch (Exception)
        {
            return raw.StatusCode.ToString();
        }
    }

    private static string Describe(CreateProductFamilyError error) =>
        error.TryGetErrorListResponse1(out var errors) ? string.Join("; ", errors.Errors)
        : error.TryGetRawError(out var raw) ? Describe(raw)
        : "Maxio returned an unrecognized error.";

    private static string Describe(CreateProductError error) =>
        error.TryGetErrorListResponse1(out var errors) ? string.Join("; ", errors.Errors)
        : error.TryGetRawError(out var raw) ? Describe(raw)
        : "Maxio returned an unrecognized error.";

    private static string Describe(ListProductsForProductFamilyError error) =>
        error.TryGetString(out var message) ? message
        : error.TryGetRawError(out var raw) ? Describe(raw)
        : "Maxio returned an unrecognized error.";

    private static string Describe(CreateMeteredComponentError error) =>
        error.TryGetNoContent(out var notFound) ? Describe(notFound)
        : error.TryGetErrorListResponse1(out var errors) ? string.Join("; ", errors.Errors)
        : error.TryGetRawError(out var raw) ? Describe(raw)
        : "Maxio returned an unrecognized error.";

    private static string Describe(ArchiveComponentError error) =>
        error.TryGetErrorListResponse1(out var errors) ? string.Join("; ", errors.Errors)
        : error.TryGetRawError(out var raw) ? Describe(raw)
        : "Maxio returned an unrecognized error.";
}

