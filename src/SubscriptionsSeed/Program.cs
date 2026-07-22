using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.AnyOf;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.SubscriptionsSeed;

/// <summary>
/// One-off operator tooling that provisions the billing entities the subscription module expects
/// (UC0). It is not wired into either host and is never run by a customer.
/// </summary>
/// <remarks>
/// Idempotent by design: every entity is read before it is created, because none of the provider's
/// create operations are safe to repeat. Run with <c>--verify</c> to check the sandbox without creating
/// anything.
/// </remarks>
public static class Program
{
    private const string PRO_PLAN_NAME = "Pro Plan";
    private const long PRO_PLAN_PRICE_IN_CENTS = 29_900;
    private const string BASIC_PLAN_NAME = "Basic Plan";
    private const long BASIC_PLAN_PRICE_IN_CENTS = 2_900;
    private const string METERED_COMPONENT_NAME = "API Calls";
    private const string METERED_COMPONENT_UNIT_NAME = "call";
    private const string METERED_COMPONENT_UNIT_PRICE = "0.01";
    private const decimal EXPECTED_UNIT_PRICE = 0.01m;

    public static async Task<int> Main(string[] args)
    {
        var verifyOnly = args.Any(argument => string.Equals(argument, "--verify", StringComparison.OrdinalIgnoreCase));

        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(Program).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

        var settings = configuration.GetSection(MaxioSettings.CONFIG_SECTION).Get<MaxioSettings>() ?? new MaxioSettings();

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            Console.Error.WriteLine($"'{MaxioSettings.CONFIG_SECTION}:ApiKey' is not configured. Set it with dotnet user-secrets before running the seed.");
            return 2;
        }

        Console.WriteLine($"Target server : {settings.ResolveBaseUrl()}");
        Console.WriteLine($"Region        : {(settings.IsEuropeanRegion ? "EU" : "US")}");
        Console.WriteLine($"Mode          : {(verifyOnly ? "verify only" : "provision if missing")}");
        Console.WriteLine();

        using var httpClient = new HttpClient();
        var client = new MaxioAdvancedBillingClient(httpClient, MaxioBillingClient.CreateClientOptions(settings));

        try
        {
            return await SeedAsync(client, settings, verifyOnly);
        }
        catch (SdkException<RawError> ex)
        {
            Console.Error.WriteLine($"The billing provider rejected a read with HTTP {(int)ex.Error.StatusCode}: {ReadBody(ex.Error)}");
            return 1;
        }
        catch (SeedFailedException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"The billing provider could not be reached: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> SeedAsync(MaxioAdvancedBillingClient client, MaxioSettings settings, bool verifyOnly)
    {
        var familyId = await EnsureProductFamilyAsync(client, settings, verifyOnly);

        if (familyId is null)
        {
            return 1;
        }

        var pro = await EnsureProductAsync(client, familyId.Value, settings.DefaultProductHandle, PRO_PLAN_NAME,
            "eShopOnWeb Pro subscription.", PRO_PLAN_PRICE_IN_CENTS, verifyOnly);

        var basic = await EnsureProductAsync(client, familyId.Value, settings.AlternateProductHandle, BASIC_PLAN_NAME,
            "eShopOnWeb Basic subscription.", BASIC_PLAN_PRICE_IN_CENTS, verifyOnly);

        var component = await EnsureMeteredComponentAsync(client, familyId.Value, settings.MeteredComponentHandle, verifyOnly);

        Console.WriteLine();
        Console.WriteLine("Seed summary");
        Console.WriteLine("------------");
        Console.WriteLine($"  product family  {settings.ProductFamilyHandle,-20} id {familyId}");
        ReportProduct(settings.DefaultProductHandle, pro);
        ReportProduct(settings.AlternateProductHandle, basic);
        ReportComponent(settings.MeteredComponentHandle, component);

        var complete = pro is not null && basic is not null && component is not null;

        Console.WriteLine();
        Console.WriteLine(complete
            ? "The billing catalog matches what the subscription module expects."
            : "The billing catalog is INCOMPLETE — see the messages above.");

        return complete ? 0 : 1;
    }

    private static async Task<int?> EnsureProductFamilyAsync(MaxioAdvancedBillingClient client, MaxioSettings settings, bool verifyOnly)
    {
        RequireHandle(settings.ProductFamilyHandle, "ProductFamilyHandle");

        var families = await client.ProductFamilies.ListProductFamilies(
            dateField: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null);

        var existing = families
            .Select(response => response.ProductFamily)
            .FirstOrDefault(family => family is not null
                && string.Equals(family.Handle, settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (existing?.Id is not null)
        {
            Console.WriteLine($"[found ] product family '{settings.ProductFamilyHandle}' (id {existing.Id}).");
            return existing.Id.Value;
        }

        if (verifyOnly)
        {
            Console.Error.WriteLine($"[MISSING] product family '{settings.ProductFamilyHandle}'.");
            return null;
        }

        try
        {
            var created = await client.ProductFamilies.CreateProductFamily(
                body: new CreateProductFamilyRequest
                {
                    ProductFamily = new CreateProductFamily
                    {
                        Name = "eShopSubscribe",
                        Handle = settings.ProductFamilyHandle,
                        Description = "Recurring plans and metered usage for the eShopOnWeb storefront."
                    }
                });

            var id = created.ProductFamily?.Id
                ?? throw new SeedFailedException("The provider created the product family but returned no id.");

            Console.WriteLine($"[created] product family '{settings.ProductFamilyHandle}' (id {id}).");
            return id;
        }
        catch (SdkException<MaxioAdvancedBilling.Errors.CreateProductFamilyError> ex)
        {
            throw new SeedFailedException($"Could not create product family '{settings.ProductFamilyHandle}': {Describe(ex)}");
        }
    }

    private static async Task<Product?> EnsureProductAsync(MaxioAdvancedBillingClient client,
        int familyId,
        string handle,
        string name,
        string description,
        long priceInCents,
        bool verifyOnly)
    {
        RequireHandle(handle, "product handle");

        var existing = await TryReadProductAsync(client, handle);

        if (existing is not null)
        {
            Console.WriteLine($"[found ] product '{handle}' (id {existing.Id}) at {FormatCents(existing.PriceInCents)}.");
            WarnOnProductDrift(handle, existing, priceInCents);
            return existing;
        }

        if (verifyOnly)
        {
            Console.Error.WriteLine($"[MISSING] product '{handle}'.");
            return null;
        }

        try
        {
            var response = await client.Products.CreateProduct(
                productFamilyId: familyId.ToString(CultureInfo.InvariantCulture),
                body: new CreateOrUpdateProductRequest
                {
                    Product = new CreateOrUpdateProduct
                    {
                        Name = name,
                        Handle = handle,
                        Description = description,
                        PriceInCents = priceInCents,
                        Interval = 1,
                        IntervalUnit = IntervalUnit.Month,
                        RequireCreditCard = false,
                        AutoCreateSignupPage = false
                    }
                });

            var product = response.Product;

            Console.WriteLine($"[created] product '{handle}' (id {product.Id}) at {FormatCents(product.PriceInCents)}.");
            WarnOnProductDrift(handle, product, priceInCents);

            return product;
        }
        catch (SdkException<MaxioAdvancedBilling.Errors.CreateProductError> ex)
        {
            throw new SeedFailedException($"Could not create product '{handle}': {Describe(ex)}");
        }
    }

    private static async Task<Component?> EnsureMeteredComponentAsync(MaxioAdvancedBillingClient client,
        int familyId,
        string handle,
        bool verifyOnly)
    {
        RequireHandle(handle, "MeteredComponentHandle");

        var existing = await TryFindComponentAsync(client, handle);

        if (existing is not null)
        {
            Console.WriteLine($"[found ] component '{handle}' (id {existing.Id}), kind {existing.Kind?.Value ?? "(none)"}.");
            VerifyComponent(handle, existing);
            return existing;
        }

        if (verifyOnly)
        {
            Console.Error.WriteLine($"[MISSING] metered component '{handle}'.");
            return null;
        }

        try
        {
            var response = await client.Components.CreateMeteredComponent(
                productFamilyId: familyId.ToString(CultureInfo.InvariantCulture),
                body: new CreateMeteredComponent
                {
                    MeteredComponent = new MeteredComponent
                    {
                        Name = METERED_COMPONENT_NAME,
                        Handle = handle,
                        UnitName = METERED_COMPONENT_UNIT_NAME,
                        PricingScheme = PricingScheme.PerUnit,
                        UnitPrice = UnitPrice1.String(METERED_COMPONENT_UNIT_PRICE),
                        Taxable = false,
                        AllowFractionalQuantities = false
                    }
                });

            var component = response.Component;

            Console.WriteLine($"[created] component '{handle}' (id {component.Id}), kind {component.Kind?.Value ?? "(none)"}.");
            VerifyComponent(handle, component);

            return component;
        }
        catch (SdkException<MaxioAdvancedBilling.Errors.CreateMeteredComponentError> ex)
        {
            throw new SeedFailedException($"Could not create metered component '{handle}': {Describe(ex)}");
        }
    }

    private static async Task<Product?> TryReadProductAsync(MaxioAdvancedBillingClient client, string handle)
    {
        try
        {
            var response = await client.Products.ReadProductByHandle(handle);
            return response.Product;
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            return null;
        }
    }

    private static async Task<Component?> TryFindComponentAsync(MaxioAdvancedBillingClient client, string handle)
    {
        try
        {
            var response = await client.Components.FindComponent(handle);
            return response.Component;
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            return null;
        }
    }

    private static void WarnOnProductDrift(string handle, Product product, long expectedPriceInCents)
    {
        if (product.PriceInCents != expectedPriceInCents)
        {
            Console.Error.WriteLine($"         WARNING: '{handle}' is priced {FormatCents(product.PriceInCents)}, expected {FormatCents(expectedPriceInCents)}.");
        }

        if (product.RequireCreditCard == true)
        {
            // require_credit_card is what actually blocks a card-less signup, and it cannot be cleared
            // through the API's create/update model — only in the Maxio UI.
            Console.Error.WriteLine($"         WARNING: '{handle}' REQUIRES a payment method, so subscribing will demand card capture. Turn require_credit_card off in the Maxio UI.");
        }
        else if (product.RequestCreditCard == true)
        {
            // Only prompts on the provider's own hosted pages; the API signup path is unaffected.
            Console.WriteLine($"         note: '{handle}' requests (but does not require) a payment method; API signup is unaffected.");
        }

        if (product.TrialInterval is > 0)
        {
            Console.Error.WriteLine($"         WARNING: '{handle}' has a trial period, so the first invoice will not match the documented expectation.");
        }
    }

    private static void VerifyComponent(string handle, Component component)
    {
        if (component.Kind != ComponentKind.MeteredComponent)
        {
            throw new SeedFailedException(
                $"Component '{handle}' is of kind '{component.Kind?.Value ?? "(none)"}', not metered. A component's kind cannot be changed in place: archive it and recreate it as a metered component.");
        }

        // Guards the one magnitude the provider documents ambiguously: a unit price sent as "0.01" must
        // come back as one cent, not one dollar.
        var unitPrice = ResolveUnitPrice(component);

        if (unitPrice != EXPECTED_UNIT_PRICE)
        {
            throw new SeedFailedException(
                $"Component '{handle}' is priced {FormatMoney(unitPrice)} per unit, expected {FormatMoney(EXPECTED_UNIT_PRICE)}. Archive it and recreate it with the correct unit price.");
        }

        if (component.PricingScheme is not null && component.PricingScheme != PricingScheme.PerUnit)
        {
            Console.Error.WriteLine($"         WARNING: component '{handle}' uses the '{component.PricingScheme.Value}' pricing scheme rather than per-unit.");
        }
    }

    private static void ReportProduct(string handle, Product? product)
    {
        Console.WriteLine(product is null
            ? $"  product         {handle,-20} MISSING"
            : $"  product         {handle,-20} id {product.Id} @ {FormatCents(product.PriceInCents)} / {product.Interval} {product.IntervalUnit?.Value}");
    }

    private static void ReportComponent(string handle, Component? component)
    {
        Console.WriteLine(component is null
            ? $"  component       {handle,-20} MISSING"
            : $"  component       {handle,-20} id {component.Id} @ {FormatMoney(ResolveUnitPrice(component))} / {component.UnitName} ({component.Kind?.Value})");
    }

    /// <summary>
    /// A component's unit price arrives either as a decimal string in major units or as an integer number
    /// of cents, and the provider populates only one of them. Read the string first so $0.01 is not
    /// reported as $0.00.
    /// </summary>
    private static decimal ResolveUnitPrice(Component component)
    {
        if (!string.IsNullOrWhiteSpace(component.UnitPrice) &&
            decimal.TryParse(component.UnitPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return (component.PricePerUnitInCents ?? 0) / 100m;
    }

    private static string FormatCents(long? cents) => FormatMoney((cents ?? 0) / 100m);

    private static string FormatMoney(decimal amount) => "$" + amount.ToString("N2", CultureInfo.InvariantCulture);

    private static void RequireHandle(string handle, string settingName)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new SeedFailedException($"'{MaxioSettings.CONFIG_SECTION}:{settingName}' is not configured.");
        }
    }

    private static string Describe<TError>(SdkException<TError> exception) where TError : ApiError
    {
        if (exception.Error.TryGetRawError(out var raw))
        {
            return ReadBody(raw);
        }

        return exception.Message;
    }

    private static string ReadBody(RawError raw)
    {
        try
        {
            var body = raw.ReadAsString();
            return string.IsNullOrWhiteSpace(body) ? $"HTTP {(int)raw.StatusCode}" : body;
        }
        catch (Exception)
        {
            return $"HTTP {(int)raw.StatusCode}";
        }
    }

    private sealed class SeedFailedException : Exception
    {
        public SeedFailedException(string message) : base(message)
        {
        }
    }
}
