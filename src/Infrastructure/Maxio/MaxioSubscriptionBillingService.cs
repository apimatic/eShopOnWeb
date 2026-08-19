using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new();

    private readonly MaxioAdvancedBillingClient _maxio;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient maxio,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _maxio.ListProductsForFamilyAsync(cancellationToken);
        return products.Select(MaxioMappings.ToPlan).ToList();
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));

        var customer = await _maxio.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(MaxioMappings.ToCustomerSubscription).ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(SubscribeToPlanRequest request, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(request, nameof(request));
        Guard.Against.NullOrEmpty(request.UserId, nameof(request.UserId));
        Guard.Against.NullOrEmpty(request.Email, nameof(request.Email));

        var plans = await ListPlansAsync(cancellationToken);
        var productHandle = MaxioMappings.ResolveProductHandle(request.ProductHandle, plans);
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new SubscriptionPlanNotFoundException(request.ProductHandle ?? MaxioMappings.DefaultProductHandle);
        }

        if (!plans.Any(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        var lockKey = $"subscribe:{request.UserId}:{productHandle}";
        var gate = SubscribeLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await SubscribeLockedAsync(request, productHandle, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<CustomerSubscription> SubscribeLockedAsync(
        SubscribeToPlanRequest request,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var customer = await EnsureCustomerAsync(request, cancellationToken);
        var reference = MaxioMappings.SubscriptionReference(request.UserId, productHandle);

        var existingByReference = await _maxio.FindSubscriptionByReferenceAsync(reference, cancellationToken);
        if (existingByReference is not null && MaxioMappings.IsLive(existingByReference.State))
        {
            _logger.LogInformation("Returning existing Maxio subscription {SubscriptionId} for user {UserId} plan {Plan}.",
                existingByReference.Id, request.UserId, productHandle);
            return MaxioMappings.ToCustomerSubscription(existingByReference);
        }

        var customerSubscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var liveForPlan = customerSubscriptions.FirstOrDefault(s =>
            MaxioMappings.IsLive(s.State)
            && string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
        if (liveForPlan is not null)
        {
            _logger.LogInformation("Returning live Maxio subscription {SubscriptionId} for user {UserId} plan {Plan}.",
                liveForPlan.Id, request.UserId, productHandle);
            return MaxioMappings.ToCustomerSubscription(liveForPlan);
        }

        var createRequest = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscriptionPayload
            {
                ProductHandle = productHandle,
                CustomerId = customer.Id,
                CustomerReference = request.UserId,
                Reference = existingByReference is null ? reference : $"{reference}:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}"
            }
        };

        using var raw = await _maxio.SendCreateSubscriptionRawAsync(createRequest, cancellationToken);
        if (await _maxio.IsDuplicateReferenceConflictAsync(raw, cancellationToken))
        {
            var raced = await _maxio.FindSubscriptionByReferenceAsync(createRequest.Subscription.Reference!, cancellationToken)
                        ?? await _maxio.FindSubscriptionByReferenceAsync(reference, cancellationToken);
            if (raced is not null)
            {
                return MaxioMappings.ToCustomerSubscription(raced);
            }
        }

        if (!raw.IsSuccessStatusCode)
        {
            var body = await raw.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Maxio create subscription failed with {StatusCode}: {Body}", (int)raw.StatusCode, body);
            throw new MaxioBillingException(
                $"Maxio create subscription failed with status {(int)raw.StatusCode}: {TruncateForClient(body)}",
                (int)raw.StatusCode,
                body);
        }

        var envelope = await System.Text.Json.JsonSerializer.DeserializeAsync<SubscriptionEnvelope>(
            await raw.Content.ReadAsStreamAsync(cancellationToken),
            MaxioJson.Options,
            cancellationToken);
        if (envelope?.Subscription is null)
        {
            throw new MaxioBillingException("Maxio create subscription returned an empty body.", (int)raw.StatusCode);
        }

        _logger.LogInformation("Created Maxio subscription {SubscriptionId} for user {UserId} plan {Plan} in state {State}.",
            envelope.Subscription.Id, request.UserId, productHandle, envelope.Subscription.State);
        return MaxioMappings.ToCustomerSubscription(envelope.Subscription);
    }

    private async Task<CustomerDto> EnsureCustomerAsync(SubscribeToPlanRequest request, CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(request.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var createRequest = new CreateCustomerRequest
        {
            Customer = new CreateCustomerPayload
            {
                FirstName = string.IsNullOrWhiteSpace(request.FirstName) ? "eShop" : request.FirstName,
                LastName = string.IsNullOrWhiteSpace(request.LastName) ? "Shopper" : request.LastName,
                Email = request.Email,
                Reference = request.UserId,
                Organization = "eShopOnWeb"
            }
        };

        using var raw = await _maxio.SendCreateCustomerRawAsync(createRequest, cancellationToken);
        if (raw.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict)
        {
            var raced = await _maxio.FindCustomerByReferenceAsync(request.UserId, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }
        }

        if (!raw.IsSuccessStatusCode)
        {
            var body = await raw.Content.ReadAsStringAsync(cancellationToken);
            throw new MaxioBillingException(
                $"Maxio create customer failed with status {(int)raw.StatusCode}.",
                (int)raw.StatusCode,
                body);
        }

        var envelope = await System.Text.Json.JsonSerializer.DeserializeAsync<CustomerEnvelope>(
            await raw.Content.ReadAsStreamAsync(cancellationToken),
            MaxioJson.Options,
            cancellationToken);
        if (envelope?.Customer is null)
        {
            throw new MaxioBillingException("Maxio create customer returned an empty body.", (int)raw.StatusCode);
        }

        return envelope.Customer;
    }

    private static string TruncateForClient(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        return body.Length <= 300 ? body : body[..300];
    }
}
