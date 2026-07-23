using System.Globalization;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.AnyOf;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.SubscriptionsSeed;

/// <summary>
/// Provisions the UC0 catalog: the product family, the two recurring plans and the metered component
/// (plan.md UC0). Every step is idempotent — an entity that already exists under the configured handle is
/// reused, never duplicated and never mutated in place.
/// </summary>
public class CatalogProvisioner
{
    private const long ProPlanPriceInCents = 29_900L;    // $299.00 / month
    private const long BasicPlanPriceInCents = 2_900L;   // $29.00 / month
    private const string MeteredUnitPrice = "0.01";      // decimal dollars per unit, not cents
    private const string MeteredUnitName = "call";

    private readonly MaxioSettings _settings;
    private readonly IAppLogger<CatalogProvisioner> _logger;
    private readonly MaxioAdvancedBillingClient _maxio;

    public CatalogProvisioner(HttpClient httpClient, IOptions<MaxioSettings> settings,
        IAppLogger<CatalogProvisioner> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        _maxio = new MaxioAdvancedBillingClient(httpClient, MaxioBillingClient.BuildOptions(_settings));
    }

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var familyId = await EnsureProductFamilyAsync(cancellationToken).ConfigureAwait(false);

        await EnsureProductAsync(familyId, _settings.DefaultProductHandle, "Pro Plan",
            "eShopOnWeb Subscribe — Pro tier.", ProPlanPriceInCents, cancellationToken).ConfigureAwait(false);

        await EnsureProductAsync(familyId, _settings.AlternateProductHandle, "Basic Plan",
            "eShopOnWeb Subscribe — Basic tier.", BasicPlanPriceInCents, cancellationToken).ConfigureAwait(false);

        await EnsureMeteredComponentAsync(familyId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> EnsureProductFamilyAsync(CancellationToken cancellationToken)
    {
        var handle = _settings.ProductFamilyHandle.Trim();

        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await _maxio.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: cancellationToken).ConfigureAwait(false);
        }
        catch (SdkException<RawError> ex)
        {
            throw Describe("list product families", ex.Error, ex);
        }

        var existing = families
            .Select(response => response.ProductFamily)
            .FirstOrDefault(family => family is not null &&
                string.Equals(family.Handle, handle, StringComparison.OrdinalIgnoreCase));

        if (existing?.Id is > 0)
        {
            _logger.LogInformation("Product family '{0}' already exists (id {1}).", handle, existing.Id.Value);
            return existing.Id.Value;
        }

        try
        {
            var created = await _maxio.ProductFamilies.CreateProductFamily(
                new CreateProductFamilyRequest
                {
                    ProductFamily = new CreateProductFamily
                    {
                        Name = "eShopSubscribe",
                        Handle = handle,
                        Description = "Recurring plans and metered usage for the eShopOnWeb storefront."
                    }
                },
                ct: cancellationToken).ConfigureAwait(false);

            var id = created.ProductFamily?.Id
                ?? throw new InvalidOperationException("Maxio created the product family but returned no id.");

            _logger.LogInformation("Created product family '{0}' (id {1}).", handle, id);
            return id;
        }
        catch (SdkException<CreateProductFamilyError> ex)
        {
            throw Describe("create product family", ex.Error.TryGetErrorListResponse1(out var errors) ? errors : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null, ex);
        }
    }

    private async Task EnsureProductAsync(int familyId, string handle, string name, string description,
        long priceInCents, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            return;
        }

        handle = handle.Trim();

        try
        {
            var existing = await _maxio.Products.ReadProductByHandle(handle, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Product '{0}' already exists (id {1}, {2} cents). Left untouched.",
                handle, Text(existing.Product.Id), Text(existing.Product.PriceInCents));
            return;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Not there yet — fall through and create it.
        }

        try
        {
            var created = await _maxio.Products.CreateProduct(
                familyId.ToString(CultureInfo.InvariantCulture),
                new CreateOrUpdateProductRequest
                {
                    Product = new CreateOrUpdateProduct
                    {
                        Name = name,
                        Handle = handle,
                        Description = description,
                        PriceInCents = priceInCents,
                        Interval = 1,
                        IntervalUnit = IntervalUnit.Month,
                        // The demo subscribes without capturing a card, so the product must not demand one.
                        RequireCreditCard = false,
                        ExpirationIntervalUnit = ExpirationIntervalUnit.Never
                    }
                },
                ct: cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Created product '{0}' (id {1}) at {2} cents / month.",
                handle, Text(created.Product.Id), priceInCents);
        }
        catch (SdkException<CreateProductError> ex)
        {
            throw Describe($"create product '{handle}'", ex.Error.TryGetErrorListResponse1(out var errors) ? errors : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null, ex);
        }
    }

    private async Task EnsureMeteredComponentAsync(int familyId, CancellationToken cancellationToken)
    {
        var handle = _settings.MeteredComponentHandle.Trim();

        IReadOnlyList<ComponentResponse> components;
        try
        {
            components = await _maxio.Components.ListComponentsForProductFamily(
                productFamilyId: familyId,
                includeArchived: false,
                filter: null,
                dateField: null,
                endDate: null,
                endDatetime: null,
                startDate: null,
                startDatetime: null,
                page: 1,
                perPage: 200,
                ct: cancellationToken).ConfigureAwait(false);
        }
        catch (SdkException<RawError> ex)
        {
            throw Describe("list components", ex.Error, ex);
        }

        var existing = components
            .Select(response => response.Component)
            .FirstOrDefault(component =>
                string.Equals(component.Handle, handle, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            if (existing.Kind == ComponentKind.MeteredComponent)
            {
                _logger.LogInformation("Metered component '{0}' already exists (id {1}). Left untouched.",
                    handle, Text(existing.Id));
                return;
            }

            // A component's kind cannot be changed in place (plan.md UC0 failure scenarios). Refuse loudly
            // rather than silently reusing an entity of the wrong kind.
            throw new InvalidOperationException(
                $"Component '{handle}' already exists on family '{_settings.ProductFamilyHandle}' but is of kind " +
                $"'{existing.Kind?.Value}', not metered. Archive it in the Maxio UI and re-run this tool.");
        }

        try
        {
            var created = await _maxio.Components.CreateMeteredComponent(
                familyId.ToString(CultureInfo.InvariantCulture),
                new MaxioAdvancedBilling.Models.CreateMeteredComponent
                {
                    MeteredComponent = new MaxioAdvancedBilling.Models.MeteredComponent
                    {
                        Name = "API Calls",
                        UnitName = MeteredUnitName,
                        Handle = handle,
                        Description = "Pay-as-you-go API calls billed on the next renewal invoice.",
                        PricingScheme = PricingScheme.PerUnit,
                        // Decimal dollars per unit — the union's string variant avoids float rounding.
                        UnitPrice = UnitPrice1.String(MeteredUnitPrice),
                        Taxable = false
                    }
                },
                ct: cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Created metered component '{0}' (id {1}) at ${2} per {3}.",
                handle, Text(created.Component.Id), MeteredUnitPrice, MeteredUnitName);
        }
        catch (SdkException<CreateMeteredComponentError> ex)
        {
            throw Describe($"create metered component '{handle}'",
                ex.Error.TryGetErrorListResponse1(out var errors) ? errors : null,
                ex.Error.TryGetRawError(out var raw) ? raw : null, ex);
        }
    }

    private static InvalidOperationException Describe(string operation, RawError error, Exception inner) =>
        new($"Could not {operation}: HTTP {(int)error.StatusCode} {Safe(error)}", inner);

    private static InvalidOperationException Describe(string operation, ErrorListResponse1? errors, RawError? raw,
        Exception inner)
    {
        if (errors?.Errors is { Count: > 0 })
        {
            return new InvalidOperationException($"Could not {operation}: {string.Join("; ", errors.Errors)}", inner);
        }

        return raw is null
            ? new InvalidOperationException($"Could not {operation}: {inner.Message}", inner)
            : new InvalidOperationException($"Could not {operation}: HTTP {(int)raw.StatusCode} {Safe(raw)}", inner);
    }

    /// <summary>Renders a nullable provider id for a log line without ever passing a null argument.</summary>
    private static string Text(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "unknown";

    private static string Safe(RawError error)
    {
        try
        {
            var body = error.ReadAsString();
            return body.Length <= 400 ? body : body.Substring(0, 400);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
