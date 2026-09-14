using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// <see cref="IBillingService"/> implementation backed by the Maxio Advanced Billing REST API.
/// Endpoints, verbs, and JSON shapes below are confirmed against the current Maxio .NET SDK source
/// (maxio-com/ab-dotnet-sdk v10.0.0) - see doc/controllers/{customers,subscriptions,product-families}.md
/// and the corresponding *Controller.cs route templates in that repository.
/// </summary>
public class MaxioBillingService : IBillingService
{
    // Subscription states that are NOT end-of-life. A repeat subscribe request for a plan the customer
    // already holds in one of these states is treated as idempotent: we return the existing subscription
    // instead of creating a second one. (Maxio has no idempotency-key support on subscription creation.)
    private static readonly HashSet<string> NonTerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending", "trialing", "assessing", "active", "soft_failure", "past_due",
        "suspended", "paused", "unpaid", "on_hold", "awaiting_signup"
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly string _productFamilyHandle;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioBillingService> logger)
    {
        _httpClient = httpClient;
        _productFamilyHandle = options.Value.ProductFamilyHandle;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BillingPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"product_families/handle:{_productFamilyHandle}/products.json", cancellationToken);
        await EnsureSuccessAsync(response, "list subscription plans", cancellationToken);

        var envelopes = await response.Content.ReadFromJsonAsync<List<ProductEnvelope>>(JsonOptions, cancellationToken)
            ?? new List<ProductEnvelope>();

        return envelopes
            .Select(e => e.Product)
            .Where(p => p is not null && p.ArchivedAt is null)
            .Select(p => new BillingPlan(p!.Handle, p.Name, p.Description, p.PriceInCents, p.Interval, p.IntervalUnit))
            .ToList();
    }

    public async Task<BillingSubscription> SubscribeAsync(string customerReference, string customerEmail, string planHandle, CancellationToken cancellationToken = default)
    {
        var customer = await FindOrCreateCustomerAsync(customerReference, customerEmail, cancellationToken);

        var existing = await FindActiveSubscriptionForPlanAsync(customer.Id, planHandle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("Customer {CustomerReference} already has a non-terminal subscription to {PlanHandle}; returning existing subscription {SubscriptionId} instead of creating a duplicate.",
                customerReference, planHandle, existing.Id);
            return ToBillingSubscription(existing);
        }

        var body = new CreateSubscriptionEnvelope
        {
            Subscription = new CreateSubscriptionWire
            {
                ProductHandle = planHandle,
                CustomerId = customer.Id,
                PaymentCollectionMethod = await GetNoCardCollectionMethodAsync(cancellationToken)
            }
        };
        var response = await _httpClient.PostAsJsonAsync("subscriptions.json", body, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, $"subscribe to plan '{planHandle}'", cancellationToken);

        var created = await response.Content.ReadFromJsonAsync<SubscriptionEnvelope>(JsonOptions, cancellationToken);
        if (created?.Subscription is null)
        {
            throw new BillingException("Maxio returned an empty response when creating the subscription.");
        }

        return ToBillingSubscription(created.Subscription);
    }

    public async Task<IReadOnlyList<BillingSubscription>> GetSubscriptionsForCustomerAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        var customer = await FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<BillingSubscription>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(ToBillingSubscription).ToList();
    }

    /// <summary>
    /// Picks the payment-collection method that lets a subscription start without a card on file:
    /// "remittance" on Relationship Invoicing sites, "invoice" on legacy Statements-Architecture sites.
    /// The site's own default ("automatic") requires a stored payment method, so it is never usable
    /// here - our seeded plans are configured with "payment method not required".
    /// </summary>
    private async Task<string> GetNoCardCollectionMethodAsync(CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync("site.json", cancellationToken);
        await EnsureSuccessAsync(response, "read site configuration", cancellationToken);

        var site = await response.Content.ReadFromJsonAsync<SiteEnvelope>(JsonOptions, cancellationToken);
        return site?.Site?.RelationshipInvoicingEnabled == true ? "remittance" : "invoice";
    }

    private async Task<CustomerWire> FindOrCreateCustomerAsync(string customerReference, string customerEmail, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitDisplayName(customerEmail);
        var createBody = new CreateCustomerEnvelope
        {
            Customer = new CreateCustomerWire
            {
                FirstName = firstName,
                LastName = lastName,
                Email = customerEmail,
                Reference = customerReference
            }
        };

        var response = await _httpClient.PostAsJsonAsync("customers.json", createBody, JsonOptions, cancellationToken);

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Reference must be unique per Maxio's validation rules. A 422 here almost always means a
            // concurrent request (e.g. a double-click) already created the customer - re-fetch it rather
            // than failing, so subscribing stays idempotent under races.
            var raceWinner = await FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (raceWinner is not null)
            {
                return raceWinner;
            }
        }

        await EnsureSuccessAsync(response, "create billing customer", cancellationToken);

        var created = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(JsonOptions, cancellationToken);
        if (created?.Customer is null)
        {
            throw new BillingException("Maxio returned an empty response when creating the customer.");
        }

        return created.Customer;
    }

    private async Task<CustomerWire?> FindCustomerByReferenceAsync(string customerReference, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync(
            $"customers/lookup.json?reference={Uri.EscapeDataString(customerReference)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "look up billing customer", cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(JsonOptions, cancellationToken);
        return envelope?.Customer;
    }

    private async Task<List<SubscriptionWire>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        await EnsureSuccessAsync(response, "list customer subscriptions", cancellationToken);

        var envelopes = await response.Content.ReadFromJsonAsync<List<SubscriptionEnvelope>>(JsonOptions, cancellationToken)
            ?? new List<SubscriptionEnvelope>();

        return envelopes.Select(e => e.Subscription).Where(s => s is not null).Select(s => s!).ToList();
    }

    private async Task<SubscriptionWire?> FindActiveSubscriptionForPlanAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            NonTerminalStates.Contains(s.State) &&
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    private static BillingSubscription ToBillingSubscription(SubscriptionWire subscription) =>
        new(
            subscription.Id.ToString(),
            subscription.Product?.Handle ?? string.Empty,
            subscription.Product?.Name ?? string.Empty,
            subscription.Product?.PriceInCents ?? 0,
            subscription.State,
            subscription.CurrentPeriodEndsAt,
            subscription.NextAssessmentAt);

    /// <summary>
    /// eShopOnWeb's identity model has no first/last name - the username (email) is all we have.
    /// Maxio requires both first_name and last_name, so we derive them from the local part of the email.
    /// </summary>
    private static (string FirstName, string LastName) SplitDisplayName(string email)
    {
        var localPart = email.Split('@')[0];
        var segments = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);

        var firstName = segments.Length > 0 ? segments[0] : "eShopOnWeb";
        var lastName = segments.Length > 1 ? string.Join(" ", segments.Skip(1)) : "Customer";
        return (Capitalize(firstName), Capitalize(lastName));
    }

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = TryExtractErrorMessage(body) ?? body;

        _logger.LogError("Maxio request to {Action} failed with {StatusCode}: {Message}", action, response.StatusCode, message);

        var status = response.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest
            ? HttpStatusCode.BadRequest
            : HttpStatusCode.BadGateway;

        throw new BillingException($"Unable to {action}: {message}", status);
    }

    private static string? TryExtractErrorMessage(string body)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<MaxioErrorEnvelope>(body, JsonOptions);
            var errors = envelope?.Errors;
            if (errors is not { ValueKind: JsonValueKind.Array or JsonValueKind.Object })
            {
                return null;
            }

            var messages = new List<string>();
            if (errors.Value.ValueKind == JsonValueKind.Array)
            {
                messages.AddRange(errors.Value.EnumerateArray().Select(e => e.ToString()));
            }
            else
            {
                foreach (var field in errors.Value.EnumerateObject())
                {
                    var fieldMessages = field.Value.ValueKind == JsonValueKind.Array
                        ? field.Value.EnumerateArray().Select(e => e.ToString())
                        : new[] { field.Value.ToString() };
                    messages.AddRange(fieldMessages.Select(m => $"{field.Name}: {m}"));
                }
            }

            return messages.Count > 0 ? string.Join("; ", messages) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
