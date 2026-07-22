using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.SubscriptionsSeed;

/// <summary>
/// UC0 operator tooling: provisions the product family, recurring plans, and metered component the
/// subscription module is configured against, then reads them back to verify the seed.
/// </summary>
/// <remarks>
/// <para>
/// This is a one-shot setup task run by a developer or a setup script. It is deliberately not
/// wired into either host — no customer-facing flow can reach it.
/// </para>
/// <para>
/// It is idempotent: entities that already exist with the configured handles are left untouched
/// and reported, so re-running it after a partial seed is safe. It never mutates an existing
/// entity that does not match the expected shape; it reports the mismatch and exits non-zero so
/// the operator can archive and recreate it deliberately.
/// </para>
/// <para>
/// Configuration comes from user-secrets and environment variables under the "Maxio" section, the
/// same shape both hosts bind. No credentials are read from, or written to, any file in the repo.
/// </para>
/// </remarks>
public static class Program
{
    private const string ProductFamilyName = "eShopSubscribe";
    private const string DefaultPlanName = "Pro Plan";
    private const long DefaultPlanPriceInCents = 29_900L;
    private const string AlternatePlanName = "Basic Plan";
    private const long AlternatePlanPriceInCents = 2_900L;
    private const string MeteredComponentName = "API Calls";
    private const string MeteredComponentUnitName = "call";
    private const string MeteredComponentUnitPrice = "0.01";
    private const string MeteredComponentKind = "metered_component";

    private const int ExitSuccess = 0;
    private const int ExitFailure = 1;

    public static async Task<int> Main(string[] args)
    {
        var verifyOnly = args.Any(a => string.Equals(a, "--verify-only", StringComparison.OrdinalIgnoreCase));

        try
        {
            var settings = LoadSettings();

            Console.WriteLine($"Maxio target : {settings.ResolveBaseUrl()}");
            Console.WriteLine($"Region       : {(settings.IsEuRegion ? "EU" : "US")}");
            Console.WriteLine($"Mode         : {(verifyOnly ? "verify only (no writes)" : "seed (create what is missing)")}");
            Console.WriteLine();

            using var httpClient = new HttpClient();
            var client = MaxioClientFactory.Create(httpClient, settings);

            var seeder = new SandboxSeeder(client, settings, verifyOnly);
            return await seeder.RunAsync(CancellationToken.None) ? ExitSuccess : ExitFailure;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAILED: {ex.Message}");
            return ExitFailure;
        }
    }

    private static MaxioSettings LoadSettings()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(Program).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

        var settings = configuration.GetSection(MaxioSettings.SectionName).Get<MaxioSettings>() ?? new MaxioSettings();

        // Allow the provisioning environment's own variables to stand in, so an operator can run
        // this without first populating user-secrets.
        settings.ApiKey = FirstNonEmpty(settings.ApiKey, configuration["MAXIO_API_KEY"]);
        settings.Subdomain = FirstNonEmpty(settings.Subdomain, configuration["MAXIO_SITE_SUBDOMAIN"]);
        settings.Environment = FirstNonEmpty(settings.Environment, configuration["MAXIO_ENVIRONMENT"], "US");
        settings.ProductFamilyHandle = FirstNonEmpty(
            settings.ProductFamilyHandle, configuration["MAXIO_DEFAULT_PRODUCT_FAMILY"], "eshop-subscribe");
        settings.DefaultProductHandle = FirstNonEmpty(settings.DefaultProductHandle, "eshop-pro");
        settings.AlternateProductHandle = FirstNonEmpty(settings.AlternateProductHandle, "basic-plan");
        settings.MeteredComponentHandle = FirstNonEmpty(settings.MeteredComponentHandle, "api-call");

        return settings;
    }

    private static string FirstNonEmpty(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? string.Empty;

    /// <summary>
    /// Provisions and verifies the catalog. Kept as a class so each step reads as one named
    /// operation rather than a wall of inline calls.
    /// </summary>
    private sealed class SandboxSeeder
    {
        private readonly MaxioAdvancedBillingClient _client;
        private readonly MaxioSettings _settings;
        private readonly bool _verifyOnly;
        private readonly List<string> _problems = new();

        public SandboxSeeder(MaxioAdvancedBillingClient client, MaxioSettings settings, bool verifyOnly)
        {
            _client = client;
            _settings = settings;
            _verifyOnly = verifyOnly;
        }

        public async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            var family = await EnsureProductFamilyAsync(cancellationToken);
            if (family?.Id is null)
            {
                Report("Product family could not be resolved or created; nothing else can be seeded.");
                return Summarise(family: null, plans: Array.Empty<Product>(), component: null);
            }

            var familyId = family.Id.Value.ToString(CultureInfo.InvariantCulture);

            var defaultPlan = await EnsurePlanAsync(
                familyId, _settings.DefaultProductHandle, DefaultPlanName, DefaultPlanPriceInCents, cancellationToken);

            var alternatePlan = await EnsurePlanAsync(
                familyId, _settings.AlternateProductHandle, AlternatePlanName, AlternatePlanPriceInCents, cancellationToken);

            var component = await EnsureMeteredComponentAsync(familyId, cancellationToken);

            var plans = new[] { defaultPlan, alternatePlan }.Where(p => p is not null).Select(p => p!).ToArray();
            return Summarise(family, plans, component);
        }

        private async Task<ProductFamily?> EnsureProductFamilyAsync(CancellationToken cancellationToken)
        {
            var handle = _settings.ProductFamilyHandle;

            // The read-by-id operation takes an int, so a handle is matched client-side.
            var families = await _client.ProductFamilies.ListProductFamilies(
                dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, ct: cancellationToken);

            var matches = families?
                .Select(r => r.ProductFamily)
                .Where(f => f is not null && string.Equals(f.Handle, handle, StringComparison.OrdinalIgnoreCase))
                .Select(f => f!)
                .ToList() ?? new List<ProductFamily>();

            // A handle is supposed to identify one family. More than one means an earlier seed was
            // repeated against a site that allowed the duplicate, and every handle lookup from here
            // on is ambiguous.
            if (matches.Count > 1)
            {
                Report(
                    $"{matches.Count} product families carry the handle '{handle}' " +
                    $"(ids {string.Join(", ", matches.Select(f => f.Id))}). Handle lookups are ambiguous; " +
                    "archive the duplicates so exactly one remains.");
            }

            var existing = matches.FirstOrDefault();
            if (existing is not null)
            {
                Console.WriteLine($"[ok]      product family '{handle}' exists (id {existing.Id}).");
                return existing;
            }

            if (_verifyOnly)
            {
                Report($"product family '{handle}' does not exist.");
                return null;
            }

            Console.WriteLine($"[create]  product family '{handle}'...");

            var created = await _client.ProductFamilies.CreateProductFamily(
                new CreateProductFamilyRequest
                {
                    ProductFamily = new CreateProductFamily
                    {
                        Name = ProductFamilyName,
                        Handle = handle,
                        Description = "Recurring plans and metered usage for eShopOnWeb Subscribe."
                    }
                },
                cancellationToken);

            if (created.ProductFamily?.Id is null)
            {
                Report($"Maxio accepted the product family '{handle}' but returned no id.");
                return null;
            }

            Console.WriteLine($"[created] product family '{handle}' (id {created.ProductFamily.Id}).");
            return created.ProductFamily;
        }

        private async Task<Product?> EnsurePlanAsync(
            string familyId,
            string handle,
            string name,
            long priceInCents,
            CancellationToken cancellationToken)
        {
            var existing = await ReadProductByHandleOrNullAsync(handle, cancellationToken);

            if (existing is not null)
            {
                VerifyPlan(existing, handle, priceInCents);
                VerifyPlanBelongsToFamily(existing, handle, familyId);
                Console.WriteLine(
                    $"[ok]      plan '{handle}' exists (id {existing.Id}, {FormatCents(existing.PriceInCents)}/" +
                    $"{existing.Interval} {existing.IntervalUnit?.Value}).");
                return existing;
            }

            if (_verifyOnly)
            {
                Report($"plan '{handle}' does not exist.");
                return null;
            }

            Console.WriteLine($"[create]  plan '{handle}'...");

            var created = await _client.Products.CreateProduct(
                familyId,
                new CreateOrUpdateProductRequest
                {
                    Product = new CreateOrUpdateProduct
                    {
                        Name = name,
                        Handle = handle,
                        Description = $"{name} for eShopOnWeb Subscribe.",
                        PriceInCents = priceInCents,
                        Interval = 1,
                        IntervalUnit = IntervalUnit.Month,
                        // The demo subscribes without card capture or 3-DS.
                        RequireCreditCard = false
                    }
                },
                cancellationToken);

            Console.WriteLine($"[created] plan '{handle}' (id {created.Product.Id}).");
            return created.Product;
        }

        private async Task<Component?> EnsureMeteredComponentAsync(string familyId, CancellationToken cancellationToken)
        {
            var handle = _settings.MeteredComponentHandle;
            var existing = await FindComponentOrNullAsync(handle, cancellationToken);

            if (existing is not null)
            {
                VerifyComponent(existing, handle);
                Console.WriteLine(
                    $"[ok]      component '{handle}' exists (id {existing.Id}, kind {existing.Kind?.Value}, " +
                    $"unit price {existing.UnitPrice ?? FormatCents(existing.PricePerUnitInCents)}).");
                return existing;
            }

            if (_verifyOnly)
            {
                Report($"metered component '{handle}' does not exist.");
                return null;
            }

            Console.WriteLine($"[create]  metered component '{handle}'...");

            var created = await _client.Components.CreateMeteredComponent(
                familyId,
                new CreateMeteredComponent
                {
                    MeteredComponent = new MeteredComponent
                    {
                        Name = MeteredComponentName,
                        Handle = handle,
                        UnitName = MeteredComponentUnitName,
                        PricingScheme = PricingScheme.PerUnit,
                        UnitPrice = MeteredComponentUnitPrice
                    }
                },
                cancellationToken);

            Console.WriteLine($"[created] metered component '{handle}' (id {created.Component.Id}).");
            return created.Component;
        }

        private void VerifyPlan(Product plan, string handle, long expectedPriceInCents)
        {
            if (plan.ArchivedAt.HasValue)
            {
                Report($"plan '{handle}' is archived. Archive it out of the way and recreate it.");
            }

            if (plan.PriceInCents != expectedPriceInCents)
            {
                Report(
                    $"plan '{handle}' is priced {FormatCents(plan.PriceInCents)} but the seed expects " +
                    $"{FormatCents(expectedPriceInCents)}.");
            }

            // Only require_credit_card actually blocks signup without a payment method;
            // request_credit_card merely offers the card form, so it is noted, not failed.
            if (plan.RequireCreditCard ?? false)
            {
                Report(
                    $"plan '{handle}' has require_credit_card on, so subscribing will demand card capture. " +
                    "Turn the toggle off, or exercise UC1 through the card-capture path.");
            }
            else if (plan.RequestCreditCard ?? false)
            {
                Console.WriteLine(
                    $"[note]    plan '{handle}' has request_credit_card on (card form offered but not required).");
            }

            if (plan.TrialInterval is > 0)
            {
                Report($"plan '{handle}' has a trial period; UC1's first-invoice expectations will not match.");
            }

            if (plan.InitialChargeInCents is > 0)
            {
                Report($"plan '{handle}' has a setup fee; UC1's first-invoice expectations will not match.");
            }
        }

        /// <summary>
        /// Confirms that the plan a handle lookup resolves to actually sits in the configured
        /// family. Handle lookups are site-wide, so a same-handle plan in another family would be
        /// subscribed to happily — and then UC2 would fail, because the metered component lives on
        /// this family and would not be available to that subscription.
        /// </summary>
        private void VerifyPlanBelongsToFamily(Product plan, string handle, string familyId)
        {
            var actualFamilyId = plan.ProductFamily?.Id?.ToString(CultureInfo.InvariantCulture);

            if (actualFamilyId is not null && actualFamilyId != familyId)
            {
                Report(
                    $"plan '{handle}' resolves to product {plan.Id} in product family {actualFamilyId}, " +
                    $"not the configured family {familyId}. Metered usage (UC2) would not be available to " +
                    "subscriptions on it. Archive the duplicate plan so the handle is unambiguous.");
            }
        }

        private void VerifyComponent(Component component, string handle)
        {
            if (component.ArchivedAt.HasValue || (component.Archived ?? false))
            {
                Report($"component '{handle}' is archived and cannot accept usage.");
            }

            // A component's kind cannot be converted in place; a wrong kind must be archived and
            // recreated, so this is reported rather than patched.
            if (!string.Equals(component.Kind?.Value, MeteredComponentKind, StringComparison.OrdinalIgnoreCase))
            {
                Report(
                    $"component '{handle}' is of kind '{component.Kind?.Value ?? "unknown"}', not metered. " +
                    "Archive it and recreate it as a metered component; UC2 will otherwise fail at runtime.");
            }

            var expectedFamily = _settings.ProductFamilyHandle;
            if (!string.IsNullOrWhiteSpace(component.ProductFamilyHandle)
                && !string.Equals(component.ProductFamilyHandle, expectedFamily, StringComparison.OrdinalIgnoreCase))
            {
                Report(
                    $"component '{handle}' is on product family '{component.ProductFamilyHandle}', not " +
                    $"'{expectedFamily}'. It will not be available to this integration's subscriptions.");
            }
        }

        private async Task<Product?> ReadProductByHandleOrNullAsync(string handle, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(handle))
            {
                return null;
            }

            try
            {
                var response = await _client.Products.ReadProductByHandle(handle, cancellationToken);
                return response.Product;
            }
            catch (SdkException<RawError> ex) when (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        private async Task<Component?> FindComponentOrNullAsync(string handle, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(handle))
            {
                return null;
            }

            try
            {
                var response = await _client.Components.FindComponent(handle, cancellationToken);
                return response.Component;
            }
            catch (SdkException<RawError> ex) when (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        private void Report(string problem)
        {
            _problems.Add(problem);
            Console.WriteLine($"[PROBLEM] {problem}");
        }

        private bool Summarise(ProductFamily? family, IReadOnlyList<Product> plans, Component? component)
        {
            Console.WriteLine();
            Console.WriteLine("--- seed result -------------------------------------------------");
            Console.WriteLine($"Maxio:ProductFamilyHandle    = {_settings.ProductFamilyHandle}");
            Console.WriteLine($"Maxio:ProductFamilyId        = {family?.Id?.ToString(CultureInfo.InvariantCulture) ?? "(unresolved)"}");

            foreach (var plan in plans)
            {
                Console.WriteLine($"plan {plan.Handle,-16} id = {plan.Id}   price = {FormatCents(plan.PriceInCents)}");
            }

            Console.WriteLine(
                $"component {_settings.MeteredComponentHandle,-11} id = {component?.Id?.ToString(CultureInfo.InvariantCulture) ?? "(unresolved)"}" +
                $"   kind = {component?.Kind?.Value ?? "(unresolved)"}");
            Console.WriteLine("-----------------------------------------------------------------");

            if (_problems.Count == 0)
            {
                Console.WriteLine("Seed verified: the catalog matches what the integration expects.");
                return true;
            }

            Console.WriteLine($"{_problems.Count} problem(s) found; the catalog does NOT match what the integration expects:");
            foreach (var problem in _problems)
            {
                Console.WriteLine($"  - {problem}");
            }

            return false;
        }

        private static string FormatCents(long? cents) =>
            ((cents ?? 0L) / 100m).ToString("C", CultureInfo.GetCultureInfo("en-US"));
    }
}
