using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>Coordinates the local identity user with Maxio, which remains the subscription system of record.</summary>
public sealed class SubscriptionEnrollmentService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CustomerLocks = new(StringComparer.Ordinal);
    private readonly IMaxioBillingClient _maxio;

    public SubscriptionEnrollmentService(IMaxioBillingClient maxio) => _maxio = maxio;

    public Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken) =>
        _maxio.GetPlansAsync(cancellationToken);

    public async Task<SubscriptionDto> SubscribeAsync(ApplicationUser user, string planHandle, CancellationToken cancellationToken)
    {
        var plans = await _maxio.GetPlansAsync(cancellationToken);
        var plan = plans.SingleOrDefault(item => string.Equals(item.Handle, planHandle, StringComparison.Ordinal));
        if (plan is null)
        {
            throw new SubscriptionRequestException(HttpStatusCode.BadRequest, "The selected subscription plan is unavailable.");
        }

        var reference = GetCustomerReference(user.Id);
        var customerLock = CustomerLocks.GetOrAdd(reference, _ => new SemaphoreSlim(1, 1));
        await customerLock.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(user, reference, cancellationToken);
            var existing = await _maxio.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var enrolled = existing.FirstOrDefault(subscription =>
                string.Equals(subscription.Product?.Handle, plan.Handle, StringComparison.Ordinal) && IsCurrent(subscription.State));
            if (enrolled is not null)
            {
                return ToDto(enrolled, plan);
            }

            try
            {
                var created = await _maxio.CreateSubscriptionAsync(customer.Id, plan.Handle, GetUniquenessToken(reference, plan.Handle), cancellationToken);
                return ToDto(created, plan);
            }
            catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
            {
                var recovered = (await _maxio.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken)).FirstOrDefault(subscription =>
                    string.Equals(subscription.Product?.Handle, plan.Handle, StringComparison.Ordinal) && IsCurrent(subscription.State));
                if (recovered is not null)
                {
                    return ToDto(recovered, plan);
                }

                throw new SubscriptionRequestException(HttpStatusCode.Conflict,
                    "The subscription request is already being processed. Please check your subscriptions and retry if needed.");
            }
        }
        finally
        {
            customerLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var customer = await _maxio.FindCustomerByReferenceAsync(GetCustomerReference(user.Id), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        return (await _maxio.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken))
            .Where(subscription => subscription.Product is not null)
            .Select(subscription => ToDto(subscription, null))
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, string reference, CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var email = user.Email ?? user.UserName;
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new SubscriptionRequestException(HttpStatusCode.UnprocessableEntity,
                "Your account needs an email address before it can subscribe.");
        }

        try
        {
            return await _maxio.CreateCustomerAsync(new MaxioCustomerInput
            {
                FirstName = "eShopOnWeb",
                LastName = "Shopper",
                Email = email,
                Reference = reference
            }, GetUniquenessToken(reference, "customer"), cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // A simultaneous request can win the unique-reference race. Resolve the authoritative record.
            var recovered = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }

            throw;
        }
    }

    private static bool IsCurrent(string? state) => !string.Equals(state, "canceled", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(state, "expired", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(state, "failed_to_create", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(state, "trial_ended", StringComparison.OrdinalIgnoreCase);

    private static SubscriptionDto ToDto(MaxioSubscription subscription, SubscriptionPlanDto? plan) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.Product?.Handle ?? plan?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? plan?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents != 0 ? subscription.ProductPriceInCents : plan?.PriceInCents ?? 0,
        State = subscription.State ?? "unknown",
        NextBillingAt = subscription.CurrentPeriodEndsAt
    };

    private static string GetCustomerReference(string userId) => $"eshop-user:{userId}";

    private static string GetUniquenessToken(string customerReference, string resource)
    {
        // Deterministic tokens let separate application instances safely converge on one signup.
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{customerReference}:{resource}"));
        return Convert.ToHexString(bytes);
    }
}

public sealed class SubscriptionRequestException : Exception
{
    public SubscriptionRequestException(HttpStatusCode statusCode, string message) : base(message) => StatusCode = statusCode;
    public HttpStatusCode StatusCode { get; }
}
