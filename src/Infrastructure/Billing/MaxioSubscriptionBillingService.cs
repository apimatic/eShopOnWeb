using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new(StringComparer.Ordinal);

    private readonly IMaxioAdvancedBillingClient _maxio;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioAdvancedBillingClient maxio,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        try
        {
            var products = await _maxio.ListProductsForFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
            return products.Select(ToPlan).Where(p => p is not null).Cast<SubscriptionPlan>().ToList();
        }
        catch (MaxioApiException ex)
        {
            throw new BillingException(ex.Message, ex);
        }
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeToPlan request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            throw new PlanNotFoundException(request.ProductHandle ?? string.Empty);
        }

        SubscriptionPlan plan;
        try
        {
            plan = await FindPlanAsync(request.ProductHandle, cancellationToken)
                   ?? throw new PlanNotFoundException(request.ProductHandle);
        }
        catch (MaxioApiException ex)
        {
            throw new BillingException(ex.Message, ex);
        }

        var gate = UserLocks.GetOrAdd(request.UserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(request, cancellationToken);
            var existing = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Returning existing Maxio subscription {SubscriptionId} for user {UserId} and plan {Plan}",
                    existing.Id, request.UserId, plan.Handle);
                return new SubscribeResult { Subscription = ToCustomerSubscription(existing), Created = false };
            }

            try
            {
                var created = await _maxio.CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest
                {
                    ProductHandle = plan.Handle,
                    CustomerId = customer.Id,
                    Reference = SubscriptionReference(request.UserId, plan.Handle)
                }, cancellationToken);

                _logger.LogInformation(
                    "Created Maxio subscription {SubscriptionId} for user {UserId} and plan {Plan}",
                    created.Id, request.UserId, plan.Handle);

                return new SubscribeResult { Subscription = ToCustomerSubscription(created), Created = true };
            }
            catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                var raced = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
                if (raced is not null)
                {
                    return new SubscribeResult { Subscription = ToCustomerSubscription(raced), Created = false };
                }

                throw new BillingException(ex.Message, ex);
            }
        }
        catch (MaxioApiException ex)
        {
            throw new BillingException(ex.Message, ex);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsForUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        try
        {
            var customer = await _maxio.FindCustomerByReferenceAsync(userId, cancellationToken);
            if (customer is null)
            {
                return Array.Empty<CustomerSubscription>();
            }

            var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            return subscriptions.Select(ToCustomerSubscription).ToList();
        }
        catch (MaxioApiException ex)
        {
            throw new BillingException(ex.Message, ex);
        }
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscribeToPlan request, CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(request.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitDisplayName(request);
        try
        {
            return await _maxio.CreateCustomerAsync(new MaxioCreateCustomerRequest
            {
                FirstName = firstName,
                LastName = lastName,
                Email = request.Email,
                Reference = request.UserId
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var raced = await _maxio.FindCustomerByReferenceAsync(request.UserId, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw new BillingException(ex.Message, ex);
        }
    }

    private async Task<SubscriptionPlan?> FindPlanAsync(string productHandle, CancellationToken cancellationToken)
    {
        var products = await _maxio.ListProductsForFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
        var match = products.FirstOrDefault(p =>
            string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
        return match is null ? null : ToPlan(match);
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(
        int customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase)
            && !IsTerminal(s.State));
    }

    private void EnsureConfigured()
    {
        try
        {
            _options.EnsureConfigured();
        }
        catch (InvalidOperationException ex)
        {
            throw new BillingConfigurationException(ex.Message);
        }
    }

    private static SubscriptionPlan? ToPlan(MaxioProduct product)
    {
        if (string.IsNullOrWhiteSpace(product.Handle) || string.IsNullOrWhiteSpace(product.Name))
        {
            return null;
        }

        return new SubscriptionPlan
        {
            Handle = product.Handle,
            Name = product.Name,
            Description = product.Description,
            Price = ToMoney(product.PriceInCents),
            Interval = product.Interval ?? 1,
            IntervalUnit = product.IntervalUnit ?? "month"
        };
    }

    private static CustomerSubscription ToCustomerSubscription(MaxioSubscription subscription)
    {
        return new CustomerSubscription
        {
            Id = subscription.Id,
            ProductHandle = subscription.Product?.Handle ?? string.Empty,
            ProductName = subscription.Product?.Name ?? string.Empty,
            Price = ToMoney(subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents),
            State = subscription.State ?? "unknown",
            NextBillingDate = subscription.NextAssessmentAt
        };
    }

    private static decimal ToMoney(long? cents) => (cents ?? 0) / 100m;

    private static string SubscriptionReference(string userId, string productHandle) => $"{userId}:{productHandle}";

    private static bool IsTerminal(string? state) =>
        state is not null && (
            state.Equals("canceled", StringComparison.OrdinalIgnoreCase)
            || state.Equals("expired", StringComparison.OrdinalIgnoreCase)
            || state.Equals("failed_to_create", StringComparison.OrdinalIgnoreCase)
            || state.Equals("trial_ended", StringComparison.OrdinalIgnoreCase));

    private static (string FirstName, string LastName) SplitDisplayName(SubscribeToPlan request)
    {
        var source = request.Email;
        if (string.IsNullOrWhiteSpace(source))
        {
            source = request.UserName;
        }

        var local = (source ?? "shopper").Split('@')[0];
        var parts = local.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        var first = parts.Length > 0 ? Capitalize(parts[0]) : "Shopper";
        var last = parts.Length > 1 ? Capitalize(string.Join(" ", parts.Skip(1))) : "eShopOnWeb";
        return (first, last);
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return char.ToUpperInvariant(value[0]) + value[1..];
    }
}
