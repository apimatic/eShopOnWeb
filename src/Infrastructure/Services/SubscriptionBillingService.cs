using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new();

    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "cancelled",
        "expired",
        "trial_ended",
        "failed_to_create"
    };

    private readonly IMaxioAdvancedBillingClient _maxio;
    private readonly MaxioOptions _options;
    private readonly IAppLogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        IMaxioAdvancedBillingClient maxio,
        IOptions<MaxioOptions> options,
        IAppLogger<SubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<SubscriptionPlan>>> ListPlansAsync(
        CancellationToken cancellationToken = default)
    {
        var configured = EnsureConfigured();
        if (!configured.IsSuccess)
        {
            return Result<IReadOnlyList<SubscriptionPlan>>.Error(string.Join(" ", configured.Errors));
        }

        try
        {
            var products = await _maxio.ListProductsForFamilyAsync(
                _options.ProductFamilyHandle, cancellationToken);
            var plans = products
                .Select(MapPlan)
                .OrderBy(p => p.PriceInCents)
                .ThenBy(p => p.Handle, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return Result<IReadOnlyList<SubscriptionPlan>>.Success(plans);
        }
        catch (MaxioApiException ex)
        {
            _logger.LogWarning("Failed to list Maxio subscription plans: {Message}", ex.Message);
            return Result<IReadOnlyList<SubscriptionPlan>>.Error(ex.Message);
        }
    }

    public async Task<Result<SubscribeResult>> SubscribeAsync(
        ShopperBillingIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        var configured = EnsureConfigured();
        if (!configured.IsSuccess)
        {
            return Result<SubscribeResult>.Error(string.Join(" ", configured.Errors));
        }

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            return Result<SubscribeResult>.Invalid(new List<ValidationError>
            {
                new() { Identifier = nameof(productHandle), ErrorMessage = "A productHandle is required." }
            });
        }

        if (string.IsNullOrWhiteSpace(shopper.UserId))
        {
            return Result<SubscribeResult>.Invalid(new List<ValidationError>
            {
                new() { Identifier = nameof(shopper.UserId), ErrorMessage = "The authenticated shopper could not be identified." }
            });
        }

        var gate = SubscribeLocks.GetOrAdd(shopper.UserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await SubscribeCoreAsync(shopper, productHandle.Trim(), cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Result<IReadOnlyList<ShopperSubscription>>> ListMySubscriptionsAsync(
        ShopperBillingIdentity shopper,
        CancellationToken cancellationToken = default)
    {
        var configured = EnsureConfigured();
        if (!configured.IsSuccess)
        {
            return Result<IReadOnlyList<ShopperSubscription>>.Error(string.Join(" ", configured.Errors));
        }

        try
        {
            var customer = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
            if (customer is null)
            {
                return Result<IReadOnlyList<ShopperSubscription>>.Success(
                    Array.Empty<ShopperSubscription>());
            }

            var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var mapped = subscriptions.Select(MapSubscription).ToList();
            return Result<IReadOnlyList<ShopperSubscription>>.Success(mapped);
        }
        catch (MaxioApiException ex)
        {
            _logger.LogWarning("Failed to list Maxio subscriptions for user {UserId}: {Message}",
                shopper.UserId, ex.Message);
            return Result<IReadOnlyList<ShopperSubscription>>.Error(ex.Message);
        }
    }

    private async Task<Result<SubscribeResult>> SubscribeCoreAsync(
        ShopperBillingIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken)
    {
        try
        {
            var products = await _maxio.ListProductsForFamilyAsync(
                _options.ProductFamilyHandle, cancellationToken);
            var plan = products.FirstOrDefault(p =>
                string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
            if (plan is null)
            {
                return Result<SubscribeResult>.NotFound(
                    $"No subscription plan with handle '{productHandle}' is available.");
            }

            var customer = await EnsureCustomerAsync(shopper, cancellationToken);
            var existing = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var liveMatch = existing.FirstOrDefault(s =>
                IsLive(s.State)
                && string.Equals(s.ProductHandle, plan.Handle, StringComparison.OrdinalIgnoreCase));
            if (liveMatch is not null)
            {
                _logger.LogInformation(
                    "Returning existing Maxio subscription {SubscriptionId} for user {UserId} plan {ProductHandle}.",
                    liveMatch.Id, shopper.UserId, plan.Handle);
                return Result<SubscribeResult>.Success(new SubscribeResult(MapSubscription(liveMatch), Created: false));
            }

            var created = await _maxio.CreateSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} for user {UserId} plan {ProductHandle}.",
                created.Id, shopper.UserId, plan.Handle);
            return Result<SubscribeResult>.Success(new SubscribeResult(MapSubscription(created), Created: true));
        }
        catch (MaxioApiException ex) when (ex.IsUnprocessableEntity)
        {
            return Result<SubscribeResult>.Invalid(new List<ValidationError>
            {
                new() { ErrorMessage = ex.Message }
            });
        }
        catch (MaxioApiException ex)
        {
            _logger.LogWarning("Maxio subscribe failed for user {UserId}: {Message}", shopper.UserId, ex.Message);
            return Result<SubscribeResult>.Error(ex.Message);
        }
    }

    private async Task<MaxioCustomerInfo> EnsureCustomerAsync(
        ShopperBillingIdentity shopper,
        CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = NamesFor(shopper);
        var email = string.IsNullOrWhiteSpace(shopper.Email)
            ? $"{shopper.UserId}@users.eshoponweb.local"
            : shopper.Email;

        try
        {
            var created = await _maxio.CreateCustomerAsync(
                firstName, lastName, email, shopper.UserId, cancellationToken);
            _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}.",
                created.Id, shopper.UserId);
            return created;
        }
        catch (MaxioApiException ex) when (ex.IsUnprocessableEntity)
        {
            // Duplicate reference from a concurrent signup: the unique reference is the source of truth.
            var raced = await _maxio.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private Result EnsureConfigured()
    {
        if (_options.IsConfigured())
        {
            return Result.Success();
        }

        return Result.Error(
            "Maxio billing is not configured. Set Maxio:ApiKey, Maxio:ProductFamilyHandle, and Maxio:Subdomain or Maxio:BaseUrl.");
    }

    internal static (string FirstName, string LastName) NamesFor(ShopperBillingIdentity shopper)
    {
        var source = !string.IsNullOrWhiteSpace(shopper.UserName) ? shopper.UserName! : shopper.Email;
        var local = (source ?? "shopper").Split('@')[0];
        var tokens = local.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        var first = tokens.Length > 0 ? SanitizeName(tokens[0]) : "Shopper";
        var last = tokens.Length > 1 ? SanitizeName(tokens[^1]) : "eShopOnWeb";
        if (string.Equals(first, last, StringComparison.OrdinalIgnoreCase) && tokens.Length <= 1)
        {
            last = "eShopOnWeb";
        }

        return (first, last);
    }

    private static string SanitizeName(string value)
    {
        var chars = value.Where(char.IsLetterOrDigit).ToArray();
        var name = chars.Length == 0 ? "Shopper" : new string(chars);
        if (name.Length == 1)
        {
            return name.ToUpperInvariant();
        }

        return char.ToUpperInvariant(name[0]) + name[1..];
    }

    private static bool IsLive(string? state)
        => !string.IsNullOrWhiteSpace(state) && !TerminalStates.Contains(state);

    private static SubscriptionPlan MapPlan(MaxioProductInfo product)
        => new(product.Handle, product.Name, product.Description, product.PriceInCents, product.Interval,
            product.IntervalUnit);

    private static ShopperSubscription MapSubscription(MaxioSubscriptionInfo subscription)
        => new(
            subscription.Id,
            subscription.State,
            subscription.ProductHandle,
            subscription.ProductName,
            subscription.PriceInCents,
            subscription.NextBillingAt);
}
