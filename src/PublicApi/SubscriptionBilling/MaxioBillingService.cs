using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

/// <summary>
/// Typed HttpClient for the Maxio Advanced Billing API plus the application operations the
/// eShopOnWeb subscription flows need.
/// </summary>
public class MaxioBillingService : IMaxioBillingService
{
    public const string RemittanceCollectionMethod = "remittance";

    private const string CustomerLookupRelativePath = "customers/lookup.json";
    private const string CustomersRelativePath = "customers.json";
    private const string SubscriptionsRelativePath = "subscriptions.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioBillingService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var familyHandle = RequiredProductFamilyHandle();
        var path = $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json?per_page=200";
        var body = await SendAsync(HttpMethod.Get, path, null, new[] { HttpStatusCode.OK }, cancellationToken);

        var products = JsonSerializer.Deserialize<List<MaxioProductEnvelope>>(body, SerializerOptions)
            ?? new List<MaxioProductEnvelope>();

        return products
            .Select(e => e.Product)
            .Where(p => p != null && p.ArchivedAt == null)
            .Select(p => new SubscriptionPlan(
                p!.Id,
                p.Handle ?? string.Empty,
                p.Name,
                p.Description,
                p.PriceInCents ?? 0,
                p.Interval,
                p.IntervalUnit,
                p.TrialInterval,
                p.TrialIntervalUnit,
                p.RequireCreditCard,
                p.Taxable,
                p.ArchivedAt))
            .OrderBy(p => p.PriceInCents)
            .ThenBy(p => p.Handle, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(SubscriberAccount subscriber, string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required.", nameof(planHandle));
        }

        var plans = await GetPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            ?? throw new SubscriptionPlanNotFoundException(planHandle);

        var customer = await EnsureCustomerAsync(subscriber, cancellationToken);

        var existing = await FindOngoingSubscriptionForPlanAsync(customer.Id, plan.Handle, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var reference = BuildSubscriptionReference(subscriber, plan.Handle);
        try
        {
            return await CreateSubscriptionAsync(customer, plan.Handle, reference, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.IsReferenceConflict)
        {
            // Lost a create race, or a previous (cancelled) subscription already holds the
            // deterministic reference. Surface the winner when there is one; otherwise retry once
            // with a unique reference so a re-subscribe after cancellation still works.
            var current = await FindOngoingSubscriptionForPlanAsync(customer.Id, plan.Handle, cancellationToken);
            if (current != null)
            {
                return current;
            }

            _logger.LogInformation(
                "Subscription reference '{Reference}' is already taken for customer {CustomerId} with no ongoing subscription to '{PlanHandle}'; retrying with a unique reference.",
                reference, customer.Id, plan.Handle);

            var uniqueReference = $"{reference}-{Guid.NewGuid():N}"[..Math.Min(reference.Length + 9, 120)];
            return await CreateSubscriptionAsync(customer, plan.Handle, uniqueReference, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(SubscriberAccount subscriber, CancellationToken cancellationToken)
    {
        var customer = await LookupCustomerByReferenceAsync(subscriber.CustomerReference, cancellationToken);
        if (customer == null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var path = $"customers/{customer.Id}/subscriptions.json";
        var body = await SendAsync(HttpMethod.Get, path, null, new[] { HttpStatusCode.OK }, cancellationToken);

        var subscriptions = JsonSerializer.Deserialize<List<MaxioSubscriptionEnvelope>>(body, SerializerOptions)
            ?? new List<MaxioSubscriptionEnvelope>();

        return subscriptions
            .Select(e => e.Subscription)
            .Where(s => s != null)
            .Select(s => ToCustomerSubscription(s!))
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
    }

    private async Task<MaxioCustomerDto> EnsureCustomerAsync(SubscriberAccount subscriber, CancellationToken cancellationToken)
    {
        var existing = await LookupCustomerByReferenceAsync(subscriber.CustomerReference, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        try
        {
            var request = new MaxioCreateCustomerRequest
            {
                Customer = new MaxioCreateCustomerDto
                {
                    FirstName = subscriber.FirstName,
                    LastName = subscriber.LastName,
                    Email = subscriber.Email,
                    Reference = subscriber.CustomerReference
                }
            };
            var body = await SendAsync(HttpMethod.Post, CustomersRelativePath, request,
                expected: new[] { HttpStatusCode.Created, HttpStatusCode.OK }, cancellationToken);
            var created = JsonSerializer.Deserialize<MaxioCustomerEnvelope>(body, SerializerOptions)?.Customer;
            if (created == null)
            {
                throw new MaxioApiException(HttpStatusCode.OK, body, new[] { "Customer create returned an empty response." });
            }

            return created;
        }
        catch (MaxioApiException)
        {
            // Two requests raced to create the same customer; Maxio only allows one per
            // reference. Re-read the customer that won.
            var winner = await LookupCustomerByReferenceAsync(subscriber.CustomerReference, cancellationToken);
            if (winner != null)
            {
                return winner;
            }

            throw;
        }
    }

    private async Task<CustomerSubscription> CreateSubscriptionAsync(
        MaxioCustomerDto customer,
        string planHandle,
        string reference,
        CancellationToken cancellationToken)
    {
        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscriptionDto
            {
                ProductHandle = planHandle,
                CustomerReference = customer.Reference,
                PaymentCollectionMethod = RemittanceCollectionMethod,
                Reference = reference
            }
        };

        var body = await SendAsync(HttpMethod.Post, SubscriptionsRelativePath, request,
            expected: new[] { HttpStatusCode.Created, HttpStatusCode.OK }, cancellationToken);
        var subscription = JsonSerializer.Deserialize<MaxioSubscriptionEnvelope>(body, SerializerOptions)?.Subscription;
        if (subscription == null)
        {
            throw new MaxioApiException(HttpStatusCode.OK, body, new[] { "Subscription create returned an empty response." });
        }

        return ToCustomerSubscription(subscription);
    }

    private async Task<MaxioCustomerDto?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"{CustomerLookupRelativePath}?reference={Uri.EscapeDataString(reference)}";
        try
        {
            var body = await SendAsync(HttpMethod.Get, path, null, new[] { HttpStatusCode.OK }, cancellationToken);
            return JsonSerializer.Deserialize<MaxioCustomerEnvelope>(body, SerializerOptions)?.Customer;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<CustomerSubscription?> FindOngoingSubscriptionForPlanAsync(
        long customerId,
        string planHandle,
        CancellationToken cancellationToken)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var body = await SendAsync(HttpMethod.Get, path, null, new[] { HttpStatusCode.OK }, cancellationToken);

        var subscriptions = JsonSerializer.Deserialize<List<MaxioSubscriptionEnvelope>>(body, SerializerOptions)
            ?? new List<MaxioSubscriptionEnvelope>();

        return subscriptions
            .Select(e => e.Subscription)
            .Where(s => s != null)
            .Select(s => ToCustomerSubscription(s!))
            .FirstOrDefault(s => s.IsOngoing && string.Equals(s.PlanHandle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string> SendAsync(
        HttpMethod method,
        string path,
        object? requestBody,
        HttpStatusCode[] expected,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (requestBody != null)
        {
            request.Content = JsonContent.Create(requestBody, options: SerializerOptions);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!expected.Contains(response.StatusCode))
        {
            var errors = MaxioApiException.TryParseErrors(body);
            throw new MaxioApiException(response.StatusCode, body, errors);
        }

        return body;
    }

    private static CustomerSubscription ToCustomerSubscription(MaxioSubscriptionDto subscription)
    {
        var product = subscription.Product;
        var priceInCents = subscription.ProductPriceInCents ?? product?.PriceInCents ?? 0;

        return new CustomerSubscription(
            subscription.Id,
            subscription.State ?? string.Empty,
            subscription.Reference,
            product?.Handle ?? string.Empty,
            product?.Name,
            priceInCents,
            product?.Interval,
            product?.IntervalUnit,
            subscription.CurrentPeriodStartedAt,
            subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
            subscription.ActivatedAt,
            subscription.CreatedAt,
            subscription.UpdatedAt,
            subscription.Currency,
            subscription.PaymentCollectionMethod);
    }

    private string BuildSubscriptionReference(SubscriberAccount subscriber, string planHandle) =>
        $"eshop-{subscriber.UserId}-{planHandle}";

    private string RequiredProductFamilyHandle()
    {
        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new MaxioConfigurationException(
                $"Configuration value '{MaxioOptions.SectionName}:{nameof(MaxioOptions.ProductFamilyHandle)}' is required.");
        }

        return _options.ProductFamilyHandle.Trim();
    }
}
