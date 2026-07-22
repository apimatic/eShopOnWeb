using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The one and only place eShopOnWeb talks to Maxio Advanced Billing, implementing the
/// provider-agnostic <see cref="IBillingClient"/> over a typed <see cref="HttpClient"/>.
/// It resolves its outbound target from <see cref="MaxioSettings"/> (plan §2.3), normalizes
/// Maxio's payloads — money always leaves this class in major currency units — and translates
/// every failure into <see cref="BillingProviderException"/>.
/// </summary>
public class MaxioBillingClient : IBillingClient
{
    private const string HandlePrefix = "handle:";
    private const string BasicScheme = "Basic";

    // Maxio authenticates with HTTP Basic where the username is the API key and the password
    // is the literal "x" (maxio-spec/openapi.yaml, securitySchemes: BasicAuth).
    private const string ApiKeyPasswordPlaceholder = "x";

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioBillingClient> _logger;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioSettings> settings, IAppLogger<MaxioBillingClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
        Catalog = _settings.ToCatalog();
    }

    public BillingCatalog Catalog { get; }

    public async Task<IReadOnlyCollection<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        // Handles are the durable identifiers; the numeric id is only a fallback because Maxio
        // reassigns ids whenever the catalog is re-created (plan §1.3).
        var familyHandle = _settings.ProductFamilyHandle;

        // Candidates in order of preference. The family-scoped routes already restrict the result
        // to the configured family; only the site-wide fallback has to filter afterwards.
        var candidates = new List<(string Path, bool FilterByFamily)>();

        if (!string.IsNullOrWhiteSpace(familyHandle))
        {
            candidates.Add(($"product_families/{HandleReference(familyHandle)}/products.json", false));
        }

        if (_settings.ProductFamilyId > 0)
        {
            candidates.Add(($"product_families/{_settings.ProductFamilyId}/products.json", false));
        }

        candidates.Add(("products.json", true));

        foreach (var (path, filterByFamily) in candidates)
        {
            var products = await TryGetAsync<List<ProductEnvelope>>(path, "ListPlans", cancellationToken);
            if (products is null)
            {
                continue;
            }

            var plans = products
                .Select(envelope => envelope.Product)
                .Where(product => product is not null && product.ArchivedAt is null)
                .Select(product => ToPlan(product!))
                .Where(plan => !filterByFamily || MatchesConfiguredFamily(plan, familyHandle))
                .ToArray();

            if (plans.Length > 0)
            {
                return plans;
            }
        }

        return Array.Empty<BillingPlan>();
    }

    public async Task<BillingPlan?> GetPlanByHandleAsync(string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            return null;
        }

        var envelope = await TryGetAsync<ProductEnvelope>(
            $"products/handle/{Uri.EscapeDataString(planHandle)}.json", "GetPlanByHandle", cancellationToken);

        return envelope?.Product is null ? null : ToPlan(envelope.Product);
    }

    public async Task<BillingComponent?> GetComponentByHandleAsync(string componentHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(componentHandle))
        {
            return null;
        }

        var envelope = await TryGetAsync<ComponentEnvelope>(
            $"components/lookup.json?handle={Uri.EscapeDataString(componentHandle)}", "FindComponent", cancellationToken);

        return envelope?.Component is null ? null : ToComponent(envelope.Component);
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string userReference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userReference))
        {
            return null;
        }

        var envelope = await TryGetAsync<CustomerEnvelope>(
            $"customers/lookup.json?reference={Uri.EscapeDataString(userReference)}", "ReadCustomerByReference", cancellationToken);

        return envelope?.Customer is null ? null : ToCustomer(envelope.Customer);
    }

    public async Task<BillingCustomer> EnsureCustomerAsync(string userReference, string? email, CancellationToken cancellationToken = default)
    {
        var existing = await FindCustomerByReferenceAsync(userReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveName(userReference, email);
        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomerAttributes
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email ?? userReference,
                Reference = userReference
            }
        };

        try
        {
            var envelope = await SendAsync<CustomerEnvelope>(HttpMethod.Post, "customers.json", request, "CreateCustomer", cancellationToken);

            return envelope?.Customer is null
                ? throw new BillingProviderException("The billing provider returned no customer for CreateCustomer.")
                : ToCustomer(envelope.Customer);
        }
        catch (BillingProviderException ex) when (ex.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
        {
            // Customer creation is idempotent on the reference: a concurrent subscribe may have
            // won the race, in which case the reference is already taken.
            var raced = await FindCustomerByReferenceAsync(userReference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    public async Task<IReadOnlyCollection<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var subscriptions = await TryGetAsync<List<SubscriptionEnvelope>>(
            $"customers/{customerId}/subscriptions.json", "ListCustomerSubscriptions", cancellationToken);

        if (subscriptions is null)
        {
            return Array.Empty<BillingSubscription>();
        }

        return subscriptions
            .Select(envelope => envelope.Subscription)
            .Where(subscription => subscription is not null)
            .Select(subscription => ToSubscription(subscription!))
            .ToArray();
    }

    public async Task<BillingSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var envelope = await TryGetAsync<SubscriptionEnvelope>(
            $"subscriptions/{subscriptionId}.json", "ReadSubscription", cancellationToken);

        return envelope?.Subscription is null ? null : ToSubscription(envelope.Subscription);
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken = default)
    {
        var request = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscriptionAttributes
            {
                ProductHandle = planHandle,
                CustomerId = customerId
            }
        };

        var envelope = await SendAsync<SubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", request, "CreateSubscription", cancellationToken);

        return RequireSubscription(envelope, "CreateSubscription");
    }

    public async Task<UsageReceipt> RecordUsageAsync(int subscriptionId, string componentHandle, decimal quantity, string? memo, CancellationToken cancellationToken = default)
    {
        var request = new CreateUsageRequest
        {
            Usage = new CreateUsageAttributes { Quantity = quantity, Memo = memo }
        };

        var path = $"subscriptions/{subscriptionId}/components/{HandleReference(componentHandle)}/usages.json";
        var envelope = await SendAsync<UsageEnvelope>(HttpMethod.Post, path, request, "CreateUsage", cancellationToken);

        var usage = envelope?.Usage
            ?? throw new BillingProviderException("The billing provider returned no usage record for CreateUsage.");

        return new UsageReceipt
        {
            Id = (int)usage.Id,
            SubscriptionId = usage.SubscriptionId == 0 ? subscriptionId : usage.SubscriptionId,
            ComponentId = usage.ComponentId,
            ComponentHandle = usage.ComponentHandle ?? componentHandle,
            Quantity = usage.Quantity,
            Memo = usage.Memo ?? memo,
            RecordedAt = usage.CreatedAt
        };
    }

    public async Task<decimal?> GetUsageTotalAsync(int subscriptionId, string componentHandle, CancellationToken cancellationToken = default)
    {
        // Maxio accumulates every reported quantity onto the component line item's unit balance.
        var componentRef = HandleReference(componentHandle);
        var lineItem = await TryGetAsync<SubscriptionComponentEnvelope>(
            $"subscriptions/{subscriptionId}/components/{componentRef}.json", "ReadSubscriptionComponent", cancellationToken);

        if (lineItem?.Component?.UnitBalance is { } unitBalance)
        {
            return unitBalance;
        }

        // Fall back to totalling the recorded usages when the line item carries no balance.
        var usages = await TryGetAsync<List<UsageEnvelope>>(
            $"subscriptions/{subscriptionId}/components/{componentRef}/usages.json", "ListUsages", cancellationToken);

        return usages?.Sum(envelope => envelope.Usage?.Quantity ?? decimal.Zero);
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle, CancellationToken cancellationToken = default)
    {
        var request = new MigrationRequest
        {
            Migration = new MigrationAttributes { ProductHandle = targetPlanHandle, PreservePeriod = true }
        };

        var envelope = await SendAsync<MigrationPreviewEnvelope>(
            HttpMethod.Post, $"subscriptions/{subscriptionId}/migrations/preview.json", request, "PreviewMigration", cancellationToken);

        var migration = envelope?.Migration
            ?? throw new BillingProviderException("The billing provider returned no migration preview.");

        return new PlanChangePreview
        {
            TargetPlanHandle = targetPlanHandle,
            ApplyImmediately = true,
            ProratedAdjustment = FromCents(migration.ProratedAdjustmentInCents),
            Charge = FromCents(migration.ChargeInCents),
            PaymentDue = FromCents(migration.PaymentDueInCents),
            CreditApplied = FromCents(migration.CreditAppliedInCents)
        };
    }

    public async Task<BillingSubscription> ChangePlanAsync(int subscriptionId, string targetPlanHandle, bool applyImmediately, CancellationToken cancellationToken = default)
    {
        if (applyImmediately)
        {
            // Preserving the period is what makes Maxio prorate rather than restart the period.
            var migration = new MigrationRequest
            {
                Migration = new MigrationAttributes { ProductHandle = targetPlanHandle, PreservePeriod = true }
            };

            var migrated = await SendAsync<SubscriptionEnvelope>(
                HttpMethod.Post, $"subscriptions/{subscriptionId}/migrations.json", migration, "MigrateSubscriptionProduct", cancellationToken);

            return RequireSubscription(migrated, "MigrateSubscriptionProduct");
        }

        // A delayed product change takes effect at the next renewal, with nothing prorated.
        var delayed = new UpdateSubscriptionRequest
        {
            Subscription = new UpdateSubscriptionAttributes
            {
                ProductHandle = targetPlanHandle,
                ProductChangeDelayed = true
            }
        };

        var updated = await SendAsync<SubscriptionEnvelope>(
            HttpMethod.Put, $"subscriptions/{subscriptionId}.json", delayed, "UpdateSubscription", cancellationToken);

        return RequireSubscription(updated, "UpdateSubscription");
    }

    public async Task<BillingSubscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<SubscriptionEnvelope>(
            HttpMethod.Post, $"subscriptions/{subscriptionId}/hold.json", new PauseRequest(), "PauseSubscription", cancellationToken);

        return RequireSubscription(envelope, "PauseSubscription");
    }

    public async Task<BillingSubscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<SubscriptionEnvelope>(
            HttpMethod.Post, $"subscriptions/{subscriptionId}/resume.json", null, "ResumeSubscription", cancellationToken);

        return RequireSubscription(envelope, "ResumeSubscription");
    }

    public async Task<BillingSubscription> CancelSubscriptionAsync(int subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default)
    {
        var request = new CancellationRequest
        {
            Subscription = new CancellationOptions { CancellationMessage = reason }
        };

        if (!endOfPeriod)
        {
            var canceled = await SendAsync<SubscriptionEnvelope>(
                HttpMethod.Delete, $"subscriptions/{subscriptionId}.json", request, "CancelSubscription", cancellationToken);

            return RequireSubscription(canceled, "CancelSubscription");
        }

        // A delayed cancellation only acknowledges the request, so the subscription is re-read
        // to report the state and effective date the provider actually holds.
        await SendAsync<DelayedCancellationResponse>(
            HttpMethod.Post, $"subscriptions/{subscriptionId}/delayed_cancel.json", request, "InitiateDelayedCancellation", cancellationToken);

        return await GetSubscriptionAsync(subscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(subscriptionId);
    }

    public async Task<BillingSubscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<SubscriptionEnvelope>(
            HttpMethod.Put, $"subscriptions/{subscriptionId}/reactivate.json", new ReactivateSubscriptionRequest(), "ReactivateSubscription", cancellationToken);

        return RequireSubscription(envelope, "ReactivateSubscription");
    }

    private static BillingSubscription RequireSubscription(SubscriptionEnvelope? envelope, string operation) =>
        envelope?.Subscription is null
            ? throw new BillingProviderException($"The billing provider returned no subscription for {operation}.")
            : ToSubscription(envelope.Subscription);

    /// <summary>
    /// Maxio path segments accept either a numeric id or the durable handle prefixed with
    /// "handle:". The prefix is part of the route syntax, so only the handle itself is escaped.
    /// </summary>
    private static string HandleReference(string handle) => HandlePrefix + Uri.EscapeDataString(handle);

    private static bool MatchesConfiguredFamily(BillingPlan plan, string? familyHandle) =>
        string.IsNullOrWhiteSpace(familyHandle)
        || plan.ProductFamilyHandle is null
        || string.Equals(plan.ProductFamilyHandle, familyHandle, StringComparison.OrdinalIgnoreCase);

    /// <summary>Issues a request, returning null when the provider reports the resource is unknown.</summary>
    private async Task<T?> TryGetAsync<T>(string path, string operation, CancellationToken cancellationToken) where T : class
    {
        try
        {
            return await SendAsync<T>(HttpMethod.Get, path, null, operation, cancellationToken);
        }
        catch (BillingProviderException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, string operation, CancellationToken cancellationToken) where T : class
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Headers = { Authorization = BuildAuthorization() }
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: MaxioJson.Options);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            // Deliberately does not echo the transport message: it carries the outbound host.
            _logger.LogWarning($"The billing provider could not be reached for {operation}: {ex.Message}");
            throw new BillingProviderException($"The billing provider could not be reached for {operation}.", null, null, ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw await BuildProviderExceptionAsync(response, operation, cancellationToken);
            }

            if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
            {
                return null;
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<T>(MaxioJson.Options, cancellationToken);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning($"The billing provider returned an unreadable payload for {operation}: {ex.Message}");
                throw new BillingProviderException($"The billing provider returned an unreadable response for {operation}.", (int)response.StatusCode, null, ex);
            }
        }
    }

    private AuthenticationHeaderValue BuildAuthorization()
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new BillingConfigurationException("No billing provider API key is configured. Set 'Maxio:ApiKey' in user-secrets or the environment.");
        }

        var credentials = Encoding.UTF8.GetBytes($"{_settings.ApiKey}:{ApiKeyPasswordPlaceholder}");

        return new AuthenticationHeaderValue(BasicScheme, Convert.ToBase64String(credentials));
    }

    private async Task<BillingProviderException> BuildProviderExceptionAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception)
        {
            body = string.Empty;
        }

        var errors = MaxioErrorReader.Read(body).Select(Redact).ToArray();
        var detail = errors.Length == 0 ? string.Empty : $": {string.Join("; ", errors)}";

        _logger.LogWarning($"The billing provider returned {status} for {operation}{detail}");

        return new BillingProviderException($"The billing provider returned {status} for {operation}{detail}", status, errors, null);
    }

    /// <summary>
    /// Last line of defence: a provider that echoes credentials back must never have them
    /// forwarded into an exception message, a log entry or an API response.
    /// </summary>
    private string Redact(string message)
    {
        if (string.IsNullOrEmpty(message) || string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            return message;
        }

        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ApiKey}:{ApiKeyPasswordPlaceholder}"));

        return message
            .Replace(_settings.ApiKey, "[REDACTED]", StringComparison.Ordinal)
            .Replace(encoded, "[REDACTED]", StringComparison.Ordinal);
    }

    private static BillingPlan ToPlan(ProductResource product) => new()
    {
        Id = product.Id,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        Price = FromCents(product.PriceInCents),
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty,
        ProductFamilyHandle = product.ProductFamily?.Handle,
        RequiresPaymentMethod = product.RequireCreditCard,
        Archived = product.ArchivedAt is not null
    };

    private static BillingComponent ToComponent(ComponentResource component) => new()
    {
        Id = component.Id,
        Handle = component.Handle,
        Name = component.Name ?? string.Empty,
        Kind = component.Kind ?? string.Empty,
        PricingScheme = component.PricingScheme,
        UnitPrice = component.UnitPrice,
        ProductFamilyId = component.ProductFamilyId,
        ProductFamilyHandle = component.ProductFamilyHandle,
        Archived = component.Archived
    };

    private static BillingCustomer ToCustomer(CustomerResource customer) => new()
    {
        Id = customer.Id,
        Reference = customer.Reference,
        Email = customer.Email,
        FirstName = customer.FirstName,
        LastName = customer.LastName
    };

    private static BillingSubscription ToSubscription(SubscriptionResource subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? string.Empty,
        CustomerId = subscription.Customer?.Id ?? 0,
        CustomerReference = subscription.Customer?.Reference,
        ProductId = subscription.Product?.Id ?? 0,
        ProductHandle = subscription.Product?.Handle,
        ProductName = subscription.Product?.Name,
        ProductPrice = FromCents(subscription.Product?.PriceInCents ?? subscription.ProductPriceInCents),
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextAssessmentAt = subscription.NextAssessmentAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod ?? false,
        DelayedCancelAt = subscription.DelayedCancelAt,
        Balance = FromCents(subscription.BalanceInCents)
    };

    /// <summary>Maxio reports money as integer cents; the domain works in major currency units.</summary>
    private static decimal FromCents(long cents) => cents / 100m;

    private static (string FirstName, string LastName) DeriveName(string userReference, string? email)
    {
        var source = email ?? userReference;
        var localPart = source.Split('@')[0];
        var parts = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);

        var first = Capitalize(parts.Length > 0 ? parts[0] : localPart);
        var last = parts.Length > 1 ? Capitalize(parts[^1]) : "Customer";

        return (string.IsNullOrWhiteSpace(first) ? "eShopOnWeb" : first, last);
    }

    private static string Capitalize(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpper(value[0], CultureInfo.InvariantCulture) + value[1..];
}
