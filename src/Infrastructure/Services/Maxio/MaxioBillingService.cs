using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services.Maxio.Dto;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

/// <summary>
/// Hand-written client for the subset of the Maxio Advanced Billing API
/// (see maxio-spec/openapi.yaml) that eShopOnWeb's subscribe flow needs:
/// listing plans, and finding-or-creating a customer + enrolling them in a plan.
///
/// Maxio has no native "create subscription" idempotency key, so idempotency here is
/// achieved two ways: (1) always re-reading Maxio state first - the customer is looked
/// up by its (unique) reference before ever being created, and a customer's existing live
/// subscriptions are checked before a new one is created for the same plan - and (2) an
/// in-process per-customer lock around that read-then-write, so two truly concurrent
/// requests from the same shopper (e.g. a double-click) serialize instead of both racing
/// past the "does a subscription already exist" check. That lock only holds within a
/// single instance of this process; a horizontally-scaled deployment would need a
/// distributed lock (e.g. a database row) to close the same race across instances.
/// </summary>
public class MaxioBillingService : IMaxioBillingService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingService> _logger;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocksByCustomerReference = new();

    public MaxioBillingService(HttpClient httpClient, MaxioOptions options, ILogger<MaxioBillingService> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        RequireConfiguration();

        var familyHandle = Uri.EscapeDataString(_options.ProductFamilyHandle);
        var products = await SendAsync<List<ProductEnvelopeDto>>(
            HttpMethod.Get, $"product_families/handle:{familyHandle}/products.json", body: null, cancellationToken);

        return products!
            .Select(p => p.Product)
            .Where(p => p is not null && p.ArchivedAt is null)
            .Select(p => new SubscriptionPlan
            {
                Handle = p!.Handle,
                Name = p.Name,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit,
                ProductFamilyHandle = p.ProductFamily?.Handle ?? _options.ProductFamilyHandle
            })
            .ToList();
    }

    public async Task<MaxioSubscription> SubscribeAsync(MaxioCustomerProfile customer, string planHandle, CancellationToken cancellationToken = default)
    {
        RequireConfiguration();

        var gate = SubscribeLocksByCustomerReference.GetOrAdd(customer.Reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var maxioCustomer = await FindOrCreateCustomerAsync(customer, cancellationToken);

            var existing = await GetCustomerSubscriptionsAsync(maxioCustomer.Id, cancellationToken);
            var existingForPlan = existing.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) && Map(s).IsLive);

            if (existingForPlan is not null)
            {
                _logger.LogInformation(
                    "Maxio customer {CustomerReference} already has a live subscription {SubscriptionId} to plan {PlanHandle}; not creating a duplicate.",
                    customer.Reference, existingForPlan.Id, planHandle);
                return Map(existingForPlan);
            }

            var createBody = new CreateSubscriptionEnvelopeDto
            {
                Subscription = new CreateSubscriptionDto
                {
                    CustomerId = maxioCustomer.Id,
                    ProductHandle = planHandle
                }
            };

            var created = await SendAsync<SubscriptionEnvelopeDto>(
                HttpMethod.Post, "subscriptions.json", createBody, cancellationToken);

            return Map(created!.Subscription!);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        RequireConfiguration();

        var customer = await FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        var subscriptions = await GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(Map).ToList();
    }

    private async Task<CustomerDto> FindOrCreateCustomerAsync(MaxioCustomerProfile customer, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerByReferenceAsync(customer.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var createBody = new CreateCustomerEnvelopeDto
        {
            Customer = new CreateCustomerDto
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference
            }
        };

        try
        {
            var created = await SendAsync<CustomerEnvelopeDto>(HttpMethod.Post, "customers.json", createBody, cancellationToken);
            return created!.Customer!;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            // Concurrent request (e.g. a double-click) may have created the customer between our
            // lookup and this create call, since `reference` must be unique. Re-read Maxio's state
            // rather than surfacing the race as an error.
            var afterRace = await FindCustomerByReferenceAsync(customer.Reference, cancellationToken);
            if (afterRace is not null)
            {
                return afterRace;
            }

            throw;
        }
    }

    private async Task<CustomerDto?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await SendRawAsync(HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", body: null, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        await ThrowIfUnsuccessful(response);
        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelopeDto>(SerializerOptions, cancellationToken);
        return envelope?.Customer;
    }

    private async Task<List<SubscriptionDto>> GetCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var envelopes = await SendAsync<List<SubscriptionEnvelopeDto>>(
            HttpMethod.Get, $"customers/{customerId}/subscriptions.json", body: null, cancellationToken);

        return envelopes!.Select(e => e.Subscription!).ToList();
    }

    private static MaxioSubscription Map(SubscriptionDto dto) => new()
    {
        Id = dto.Id,
        State = dto.State,
        PlanHandle = dto.Product?.Handle ?? string.Empty,
        PlanName = dto.Product?.Name ?? string.Empty,
        PriceInCents = dto.ProductPriceInCents,
        Interval = dto.Product?.Interval ?? 0,
        IntervalUnit = dto.Product?.IntervalUnit ?? string.Empty,
        CurrentPeriodStartsAt = dto.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = dto.CurrentPeriodEndsAt,
        NextAssessmentAt = dto.NextAssessmentAt,
        ActivatedAt = dto.ActivatedAt,
        CreatedAt = dto.CreatedAt
    };

    private async Task<T?> SendAsync<T>(HttpMethod method, string relativeUrl, object? body, CancellationToken cancellationToken)
    {
        var response = await SendRawAsync(method, relativeUrl, body, cancellationToken);
        await ThrowIfUnsuccessful(response);
        return await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string relativeUrl, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativeUrl);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: SerializerOptions);
        }

        try
        {
            return await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to reach Maxio Advanced Billing at {BaseAddress}", _httpClient.BaseAddress);
            throw new MaxioApiException(System.Net.HttpStatusCode.BadGateway, "Unable to reach the Maxio billing service.");
        }
    }

    private async Task ThrowIfUnsuccessful(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        var errors = MaxioErrorParser.Parse(body);
        var message = errors.Count > 0
            ? string.Join("; ", errors)
            : $"Maxio request failed with status {(int)response.StatusCode}.";

        _logger.LogWarning("Maxio API call to {Uri} failed with {StatusCode}: {Message}", response.RequestMessage?.RequestUri, response.StatusCode, message);
        throw new MaxioApiException(response.StatusCode, message, errors);
    }

    private void RequireConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
            string.IsNullOrWhiteSpace(_options.Subdomain) ||
            string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                "Maxio is not configured. Set Maxio:ApiKey, Maxio:Subdomain and Maxio:ProductFamilyHandle " +
                "(e.g. via user-secrets or MAXIO_API_KEY/MAXIO_SITE_SUBDOMAIN/MAXIO_DEFAULT_PRODUCT_FAMILY environment variables).");
        }
    }
}
