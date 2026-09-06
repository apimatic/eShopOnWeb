using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// <see cref="IBillingGateway"/> over the Maxio Advanced Billing REST API.
/// </summary>
/// <remarks>
/// Endpoints, request bodies and response shapes used here were confirmed against the
/// official Maxio Advanced Billing .NET SDK (github.com/maxio-com/ab-dotnet-sdk) and then
/// exercised against a live sandbox site:
/// <list type="bullet">
/// <item>GET  /product_families/{product_family_id}/products.json - the family may be
/// addressed as <c>handle:my-family</c>, which is what we do, because Maxio reassigns
/// numeric ids when a catalog is re-seeded;</item>
/// <item>GET  /site.json - carries the site currency, which products do not;</item>
/// <item>GET  /customers/lookup.json?reference=... - exact match, 404 when absent;</item>
/// <item>POST /customers.json - body <c>{"customer": {...}}</c>, 422 when the reference is taken;</item>
/// <item>GET  /customers/{customer_id}/subscriptions.json;</item>
/// <item>POST /subscriptions.json - body <c>{"subscription": {...}}</c>;</item>
/// <item>GET  /subscriptions/lookup.json?reference=... - exact match, 404 when absent.</item>
/// </list>
/// </remarks>
public class MaxioBillingGateway : IBillingGateway
{
    private const int MaxLoggedErrorBodyLength = 2000;

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingGateway> _logger;

    public MaxioBillingGateway(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        ILogger<MaxioBillingGateway> logger)
    {
        _httpClient = httpClient;

        // Resolving Value here validates the configuration, so a missing or malformed
        // "Maxio" section surfaces on the subscription endpoints rather than at startup.
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyHandle = _options.ProductFamilyHandle!.Trim();

        var productsTask = SendAsync<List<MaxioProductEnvelope>>(
            HttpMethod.Get,
            $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json",
            body: null,
            cancellationToken);

        var currencyTask = GetSiteCurrencyAsync(cancellationToken);

        // Awaited first because it cannot fault: if the products call blows up afterwards,
        // there is no unobserved task left behind.
        var currency = await currencyTask;
        var products = await productsTask;

        if (products is null)
        {
            // The family handle resolved to nothing: treat as a configuration error rather
            // than quietly offering an empty catalogue.
            throw new MaxioApiException(
                $"Maxio product family '{familyHandle}' was not found.",
                (int)HttpStatusCode.NotFound);
        }

        return products
            .Select(envelope => envelope.Product)
            .Where(product => product is not null && !string.IsNullOrWhiteSpace(product!.Handle))
            .Where(product => product!.ArchivedAt is null)
            .Select(product => MapPlan(product!, currency))
            .OrderBy(plan => plan.PriceInCents)
            .ThenBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<SubscriptionPlan?> FindPlanAsync(string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            return null;
        }

        // Scoped to the configured family on purpose: a product handle that exists
        // elsewhere on the site is not something this storefront offers.
        var plans = await ListPlansAsync(cancellationToken);
        return plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioCustomerEnvelope>(
            HttpMethod.Get,
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            body: null,
            cancellationToken);

        return envelope?.Customer is null ? null : MapCustomer(envelope.Customer);
    }

    public async Task<BillingCustomer> CreateCustomerAsync(
        NewBillingCustomer customer,
        CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCustomerAttributes
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference
            }
        };

        var envelope = await SendAsync<MaxioCustomerEnvelope>(
            HttpMethod.Post, "customers.json", request, cancellationToken);

        if (envelope?.Customer is null)
        {
            throw new MaxioApiException("Maxio accepted the customer but returned no customer payload.");
        }

        return MapCustomer(envelope.Customer);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        var envelopes = await SendAsync<List<MaxioSubscriptionEnvelope>>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            body: null,
            cancellationToken);

        if (envelopes is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        return envelopes
            .Select(envelope => envelope.Subscription)
            .Where(subscription => subscription is not null)
            .Select(subscription => MapSubscription(subscription!))
            .ToList();
    }

    public async Task<CustomerSubscription> CreateSubscriptionAsync(
        NewSubscription subscription,
        CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioSubscriptionAttributes
            {
                ProductHandle = subscription.PlanHandle,
                CustomerId = subscription.CustomerId,
                PaymentCollectionMethod = subscription.PaymentCollectionMethod,
                Reference = subscription.Reference
            }
        };

        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Post, "subscriptions.json", request, cancellationToken);

        if (envelope?.Subscription is null)
        {
            throw new MaxioApiException("Maxio accepted the subscription but returned no subscription payload.");
        }

        return MapSubscription(envelope.Subscription);
    }

    public async Task<CustomerSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Get,
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            body: null,
            cancellationToken);

        return envelope?.Subscription is null ? null : MapSubscription(envelope.Subscription);
    }

    private async Task<string?> GetSiteCurrencyAsync(CancellationToken cancellationToken)
    {
        // Products carry no currency of their own; the site does. Prices stay useful even
        // if this read fails, so it must never take the plan listing down with it.
        try
        {
            var envelope = await SendAsync<MaxioSiteEnvelope>(
                HttpMethod.Get, "site.json", body: null, cancellationToken);
            return envelope?.Site?.Currency;
        }
        catch (Exception ex)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            _logger.LogWarning(ex, "Could not read the Maxio site currency; plan prices will omit it.");
            return null;
        }
    }

    /// <summary>
    /// Sends a request and deserialises the response, returning null for 404 so callers can
    /// express "not found" without exception control flow.
    /// </summary>
    private async Task<TResponse?> SendAsync<TResponse>(
        HttpMethod method,
        string relativeUrl,
        object? body,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        using var request = new HttpRequestMessage(method, relativeUrl);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: MaxioJson.Options);
        }

        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // A timeout also lands here: HttpClient cancels the request on its own token.
            _logger.LogError(ex, "Maxio {Method} {Path} did not complete.", method, relativeUrl);
            throw new MaxioApiException(
                $"The billing system could not be reached ({method} {relativeUrl}).",
                statusCode: null,
                innerException: ex);
        }

        using (response)
        {
            _logger.LogInformation(
                "Maxio {Method} {Path} responded {StatusCode} in {ElapsedMs}ms.",
                method, relativeUrl, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw await CreateFailureAsync(method, relativeUrl, response, cancellationToken);
            }

            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return null;
            }

            try
            {
                return await response.Content
                    .ReadFromJsonAsync<TResponse>(MaxioJson.Options, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                throw new MaxioApiException(
                    $"The billing system returned a response that could not be read ({method} {relativeUrl}).",
                    (int)response.StatusCode,
                    innerException: ex);
            }
        }
    }

    private async Task<MaxioApiException> CreateFailureAsync(
        HttpMethod method,
        string relativeUrl,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(response, cancellationToken);
        var errors = MaxioErrorReader.Read(body);
        var statusCode = (int)response.StatusCode;

        _logger.LogError(
            "Maxio {Method} {Path} failed with {StatusCode}: {Body}",
            method, relativeUrl, statusCode, Truncate(body));

        var summary = errors.Count > 0
            ? string.Join("; ", errors.Take(5))
            : response.ReasonPhrase ?? "no detail";

        return new MaxioApiException(
            $"The billing system rejected {method} {relativeUrl} with {statusCode}: {summary}",
            statusCode,
            errors,
            MaxioErrorReader.IsDuplicateReference(errors));
    }

    private static async Task<string?> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Truncate(string? body) =>
        body is null || body.Length <= MaxLoggedErrorBodyLength
            ? body ?? string.Empty
            : body.Substring(0, MaxLoggedErrorBodyLength) + "...";

    private static SubscriptionPlan MapPlan(MaxioProduct product, string? currency) => new()
    {
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Currency = currency,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        ProductFamilyHandle = product.ProductFamily?.Handle,
        RequiresPaymentMethod = product.RequireCreditCard,
        TrialInterval = product.TrialInterval,
        TrialIntervalUnit = product.TrialIntervalUnit
    };

    private static BillingCustomer MapCustomer(MaxioCustomer customer) => new()
    {
        Id = customer.Id,
        Reference = customer.Reference,
        Email = customer.Email,
        FirstName = customer.FirstName,
        LastName = customer.LastName
    };

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? string.Empty,
        CustomerId = subscription.Customer?.Id ?? 0,
        Reference = subscription.Reference,
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        PriceInCents = subscription.ProductPriceInCents,
        Currency = subscription.Currency,
        Interval = subscription.Product?.Interval,
        IntervalUnit = subscription.Product?.IntervalUnit,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextAssessmentAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        CreatedAt = subscription.CreatedAt,
        BalanceInCents = subscription.BalanceInCents,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod
    };
}
