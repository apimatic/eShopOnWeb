using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.SubscriptionsSeed;

/// <summary>
/// UC0 operator tooling: provisions and verifies the billing entities the integration expects.
/// </summary>
/// <remarks>
/// This deliberately does not go through <c>IBillingClient</c>. That seam is the running
/// application's read/write surface and must never be able to create or archive catalog entities;
/// seeding is an operator task performed once per sandbox, so its admin calls live here instead.
/// </remarks>
public sealed class MaxioSeeder
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly TextWriter _output;

    public MaxioSeeder(HttpClient httpClient, TextWriter output)
    {
        _httpClient = httpClient;
        _output = output;
    }

    /// <summary>
    /// Brings the sandbox to the shape <paramref name="specification"/> describes.
    /// </summary>
    /// <param name="verifyOnly">
    /// When true nothing is created or archived; the sandbox is only inspected and reported on.
    /// </param>
    /// <returns>True when the sandbox matches the specification once the run finishes.</returns>
    public async Task<bool> RunAsync(SeedSpecification specification, bool verifyOnly, CancellationToken cancellationToken)
    {
        var satisfied = true;

        var family = await FindFamilyAsync(specification.FamilyHandle, cancellationToken);
        if (family is null)
        {
            if (verifyOnly)
            {
                await ReportAsync($"MISSING  product family '{specification.FamilyHandle}'");
                return false;
            }

            family = await CreateFamilyAsync(specification, cancellationToken);
            await ReportAsync($"CREATED  product family '{family.Handle}' (id {family.Id})");
        }
        else
        {
            await ReportAsync($"OK       product family '{family.Handle}' (id {family.Id})");
        }

        var products = await ListProductsAsync(family.Id, cancellationToken);
        foreach (var plan in specification.Plans)
        {
            satisfied &= await EnsurePlanAsync(family.Id, plan, products, verifyOnly, cancellationToken);
        }

        satisfied &= await EnsureComponentAsync(family.Id, specification.MeteredComponent, verifyOnly, cancellationToken);

        // Read the family back so the report reflects the provider's state, not our assumptions.
        await ReportAsync(string.Empty);
        await ReportAsync("Verification read-back:");
        foreach (var product in (await ListProductsAsync(family.Id, cancellationToken)).Where(p => p.ArchivedAt is null))
        {
            await ReportAsync($"  plan      {product.Handle,-16} {FormatMoney(product.PriceInCents)}/{product.IntervalUnit}  requires payment method: {product.RequireCreditCard}");
        }

        foreach (var component in (await ListComponentsAsync(family.Id, cancellationToken)).Where(c => !c.Archived))
        {
            await ReportAsync($"  component {component.Handle,-16} kind {component.Kind}  unit price {component.UnitPrice}");
        }

        return satisfied;
    }

    private async Task<bool> EnsurePlanAsync(long familyId,
        PlanSpecification plan,
        IReadOnlyCollection<SeedProduct> existingProducts,
        bool verifyOnly,
        CancellationToken cancellationToken)
    {
        var existing = existingProducts.FirstOrDefault(p =>
            p.ArchivedAt is null && string.Equals(p.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            if (verifyOnly)
            {
                await ReportAsync($"MISSING  plan '{plan.Handle}'");
                return false;
            }

            var created = await CreatePlanAsync(familyId, plan, cancellationToken);
            await ReportAsync($"CREATED  plan '{created.Handle}' (id {created.Id}) at {FormatMoney(created.PriceInCents)}/{created.IntervalUnit}");
            return true;
        }

        // A handle taken by an entity of the wrong shape must be reported, never silently reused.
        var problems = new List<string>();
        if (existing.PriceInCents != plan.PriceInCents)
        {
            problems.Add($"price is {FormatMoney(existing.PriceInCents)}, expected {FormatMoney(plan.PriceInCents)}");
        }

        if (!string.Equals(existing.IntervalUnit, plan.IntervalUnit, StringComparison.OrdinalIgnoreCase) ||
            existing.Interval != plan.Interval)
        {
            problems.Add($"billing period is {existing.Interval} {existing.IntervalUnit}, expected {plan.Interval} {plan.IntervalUnit}");
        }

        if (existing.RequireCreditCard != plan.RequiresPaymentMethod)
        {
            problems.Add($"requires payment method is {existing.RequireCreditCard}, expected {plan.RequiresPaymentMethod}");
        }

        if (problems.Count > 0)
        {
            await ReportAsync($"MISMATCH plan '{plan.Handle}' (id {existing.Id}): {string.Join("; ", problems)}");
            await ReportAsync($"         Archive it and re-run the seed rather than reusing a mismatched plan.");
            return false;
        }

        await ReportAsync($"OK       plan '{existing.Handle}' (id {existing.Id}) at {FormatMoney(existing.PriceInCents)}/{existing.IntervalUnit}");
        return true;
    }

    private async Task<bool> EnsureComponentAsync(long familyId,
        ComponentSpecification component,
        bool verifyOnly,
        CancellationToken cancellationToken)
    {
        var components = await ListComponentsAsync(familyId, cancellationToken);
        var existing = components.FirstOrDefault(c =>
            !c.Archived && string.Equals(c.Handle, component.Handle, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            if (verifyOnly)
            {
                await ReportAsync($"MISSING  component '{component.Handle}'");
                return false;
            }

            var created = await CreateComponentAsync(familyId, component, cancellationToken);
            await ReportAsync($"CREATED  component '{created.Handle}' (id {created.Id}) kind {created.Kind} at {created.UnitPrice}/unit");
            return true;
        }

        // A component's kind cannot be converted in place: the only correct fix is archive and
        // recreate, and skipping that fails UC2 at runtime with a confusing error.
        if (!string.Equals(existing.Kind, component.Kind, StringComparison.OrdinalIgnoreCase))
        {
            await ReportAsync($"MISMATCH component '{component.Handle}' (id {existing.Id}) is kind '{existing.Kind}', expected '{component.Kind}'.");

            if (verifyOnly)
            {
                return false;
            }

            await ArchiveComponentAsync(familyId, existing.Id, cancellationToken);
            await ReportAsync($"ARCHIVED component '{component.Handle}' (id {existing.Id}); a component's kind cannot be changed in place.");

            var replacement = await CreateComponentAsync(familyId, component, cancellationToken);
            await ReportAsync($"CREATED  component '{replacement.Handle}' (id {replacement.Id}) kind {replacement.Kind} at {replacement.UnitPrice}/unit");
            return true;
        }

        await ReportAsync($"OK       component '{existing.Handle}' (id {existing.Id}) kind {existing.Kind} at {existing.UnitPrice}/unit");
        return true;
    }

    private async Task<SeedFamily?> FindFamilyAsync(string handle, CancellationToken cancellationToken)
    {
        var families = await GetAsync<List<SeedFamilyEnvelope>>("/product_families.json", cancellationToken)
            ?? new List<SeedFamilyEnvelope>();

        return families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f is not null
                && f.ArchivedAt is null
                && string.Equals(f.Handle, handle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<SeedFamily> CreateFamilyAsync(SeedSpecification specification, CancellationToken cancellationToken)
    {
        var payload = new { product_family = new { name = specification.FamilyName, handle = specification.FamilyHandle } };
        var created = await PostAsync<SeedFamilyEnvelope>("/product_families.json", payload, cancellationToken);

        return created?.ProductFamily
            ?? throw new InvalidOperationException("Maxio returned no product family after creating one.");
    }

    private async Task<IReadOnlyCollection<SeedProduct>> ListProductsAsync(long familyId, CancellationToken cancellationToken)
    {
        var products = await GetAsync<List<SeedProductEnvelope>>($"/product_families/{familyId}/products.json", cancellationToken)
            ?? new List<SeedProductEnvelope>();

        return products.Select(p => p.Product).Where(p => p is not null).Select(p => p!).ToList();
    }

    private async Task<SeedProduct> CreatePlanAsync(long familyId, PlanSpecification plan, CancellationToken cancellationToken)
    {
        var payload = new
        {
            product = new
            {
                name = plan.Name,
                handle = plan.Handle,
                price_in_cents = plan.PriceInCents,
                interval = plan.Interval,
                interval_unit = plan.IntervalUnit,
                require_credit_card = plan.RequiresPaymentMethod,
                taxable = false
            }
        };

        var created = await PostAsync<SeedProductEnvelope>($"/product_families/{familyId}/products.json", payload, cancellationToken);

        return created?.Product ?? throw new InvalidOperationException($"Maxio returned no product after creating '{plan.Handle}'.");
    }

    private async Task<IReadOnlyCollection<SeedComponent>> ListComponentsAsync(long familyId, CancellationToken cancellationToken)
    {
        var components = await GetAsync<List<SeedComponentEnvelope>>($"/product_families/{familyId}/components.json", cancellationToken)
            ?? new List<SeedComponentEnvelope>();

        return components.Select(c => c.Component).Where(c => c is not null).Select(c => c!).ToList();
    }

    private async Task<SeedComponent> CreateComponentAsync(long familyId, ComponentSpecification component, CancellationToken cancellationToken)
    {
        var payload = new
        {
            metered_component = new
            {
                name = component.Name,
                handle = component.Handle,
                unit_name = component.UnitName,
                unit_price = component.UnitPrice.ToString(CultureInfo.InvariantCulture),
                pricing_scheme = component.PricingScheme,
                taxable = false
            }
        };

        var created = await PostAsync<SeedComponentEnvelope>(
            $"/product_families/{familyId}/metered_components.json", payload, cancellationToken);

        return created?.Component ?? throw new InvalidOperationException($"Maxio returned no component after creating '{component.Handle}'.");
    }

    private async Task ArchiveComponentAsync(long familyId, long componentId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.DeleteAsync($"/product_families/{familyId}/components/{componentId}.json", cancellationToken);
        await EnsureSuccessAsync(response, "DELETE", $"/product_families/{familyId}/components/{componentId}.json", cancellationToken);
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        await EnsureSuccessAsync(response, "GET", path, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken);
    }

    private async Task<T?> PostAsync<T>(string path, object payload, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(path, payload, SerializerOptions, cancellationToken);
        await EnsureSuccessAsync(response, "POST", path, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string method, string path, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException(
            $"Maxio rejected {method} {path} with status {(int)response.StatusCode}: {body}");
    }

    private Task ReportAsync(string message) => _output.WriteLineAsync(message);

    private static string FormatMoney(int cents) =>
        (cents / 100m).ToString("C", CultureInfo.GetCultureInfo("en-US"));

    // Minimal wire shapes for the entities the seed touches.

    private sealed class SeedFamilyEnvelope
    {
        [JsonPropertyName("product_family")]
        public SeedFamily? ProductFamily { get; set; }
    }

    private sealed class SeedFamily
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("handle")]
        public string? Handle { get; set; }

        [JsonPropertyName("archived_at")]
        public DateTimeOffset? ArchivedAt { get; set; }
    }

    private sealed class SeedProductEnvelope
    {
        [JsonPropertyName("product")]
        public SeedProduct? Product { get; set; }
    }

    private sealed class SeedProduct
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("handle")]
        public string? Handle { get; set; }

        [JsonPropertyName("price_in_cents")]
        public int PriceInCents { get; set; }

        [JsonPropertyName("interval")]
        public int Interval { get; set; }

        [JsonPropertyName("interval_unit")]
        public string? IntervalUnit { get; set; }

        [JsonPropertyName("require_credit_card")]
        public bool RequireCreditCard { get; set; }

        [JsonPropertyName("archived_at")]
        public DateTimeOffset? ArchivedAt { get; set; }
    }

    private sealed class SeedComponentEnvelope
    {
        [JsonPropertyName("component")]
        public SeedComponent? Component { get; set; }
    }

    private sealed class SeedComponent
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("handle")]
        public string? Handle { get; set; }

        [JsonPropertyName("kind")]
        public string? Kind { get; set; }

        [JsonPropertyName("unit_price")]
        public string? UnitPrice { get; set; }

        [JsonPropertyName("archived")]
        public bool Archived { get; set; }
    }
}
