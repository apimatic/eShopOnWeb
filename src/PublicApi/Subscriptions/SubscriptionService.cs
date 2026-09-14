using System.Collections.Concurrent;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<SubscriptionDto> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string userName, CancellationToken cancellationToken = default);
}

public sealed class SubscriptionService : ISubscriptionService
{
    private const string CustomerReferencePrefix = "eshopweb-user-";
    private const string SubscriptionReferencePrefix = "eshopweb-sub-";

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> s_subscribeLocks = new();

    private readonly IMaxioClient _maxio;
    private readonly MaxioOptions _options;
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionService(IMaxioClient maxio, IOptions<MaxioOptions> options, UserManager<ApplicationUser> userManager)
    {
        _maxio = maxio;
        _options = options.Value;
        _userManager = userManager;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var family = await _maxio.FindProductFamilyByHandleAsync(_options.ProductFamilyHandle, cancellationToken)
            ?? throw new MaxioConfigurationException(
                $"No Maxio product family with handle '{_options.ProductFamilyHandle}' exists on site '{_options.Subdomain}'.");

        var products = await _maxio.ListProductFamilyProductsAsync(family.Id, cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null)
            .Select(p => new SubscriptionPlanDto
            {
                Handle = p.Handle ?? string.Empty,
                Name = p.Name ?? string.Empty,
                Description = p.Description,
                Price = CentsToPrice(p.PriceInCents),
                Interval = p.Interval ?? 1,
                IntervalUnit = p.IntervalUnit ?? "month",
                RequireCreditCard = p.RequireCreditCard ?? false
            })
            .OrderBy(p => p.Price)
            .ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle must be supplied.", nameof(planHandle));
        }

        var user = await ResolveUserAsync(userName, cancellationToken)
            ?? throw new MaxioConfigurationException($"No eShopOnWeb user exists for '{userName}'.");

        var product = await _maxio.GetProductByHandleAsync(planHandle, cancellationToken)
            ?? throw new SubscriptionPlanNotFoundException(planHandle);

        if (product.ProductFamily is null ||
            !string.Equals(product.ProductFamily.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        var gate = s_subscribeLocks.GetOrAdd($"{user.Id}|{planHandle}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await SubscribeCoreAsync(user, product, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string userName, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userName, cancellationToken)
            ?? throw new MaxioConfigurationException($"No eShopOnWeb user exists for '{userName}'.");

        var customer = await _maxio.GetCustomerByReferenceAsync(CustomerReference(user.Id), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(s => MapSubscription(s, alreadySubscribed: false)).ToList();
    }

    private async Task<SubscriptionDto> SubscribeCoreAsync(ApplicationUser user, MaxioProduct product, CancellationToken cancellationToken)
    {
        var customer = await EnsureCustomerAsync(user, cancellationToken);

        var baseReference = SubscriptionReference(user.Id, product.Handle!);
        var reference = baseReference;
        var suffix = 1;
        while (true)
        {
            var existing = await _maxio.GetSubscriptionByReferenceAsync(reference, cancellationToken);
            if (existing is null)
            {
                break;
            }
            if (!existing.IsTerminalState)
            {
                return MapSubscription(existing, alreadySubscribed: true);
            }
            suffix++;
            reference = $"{baseReference}-r{suffix}";
        }

        try
        {
            var created = await _maxio.CreateSubscriptionAsync(product.Handle!, customer.Id, reference, cancellationToken);
            return MapSubscription(created, alreadySubscribed: false);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 409 || IsDuplicateReferenceError(ex))
        {
            var existing = await _maxio.GetSubscriptionByReferenceAsync(reference, cancellationToken)
                ?? throw ex;
            return MapSubscription(existing, alreadySubscribed: true);
        }
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var reference = CustomerReference(user.Id);

        var existing = await _maxio.GetCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveCustomerNames(user);
        var email = !string.IsNullOrWhiteSpace(user.Email) ? user.Email : user.UserName!;

        try
        {
            return await _maxio.CreateCustomerAsync(firstName, lastName, email, reference, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422 && IsDuplicateReferenceError(ex))
        {
            return await _maxio.GetCustomerByReferenceAsync(reference, cancellationToken)
                ?? throw ex;
        }
    }

    private async Task<ApplicationUser?> ResolveUserAsync(string userName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }
        return await _userManager.FindByNameAsync(userName);
    }

    private string CustomerReference(string userId) => $"{CustomerReferencePrefix}{userId}";

    private string SubscriptionReference(string userId, string planHandle)
    {
        var sanitized = new string(planHandle.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        return $"{SubscriptionReferencePrefix}{userId}-{sanitized}";
    }

    private static bool IsDuplicateReferenceError(MaxioApiException ex) =>
        ex.Errors.Any(e => e.Contains("reference", StringComparison.OrdinalIgnoreCase) &&
                           e.Contains("unique", StringComparison.OrdinalIgnoreCase));

    private static (string FirstName, string LastName) DeriveCustomerNames(ApplicationUser user)
    {
        var source = !string.IsNullOrWhiteSpace(user.Email) ? user.Email : (user.UserName ?? "eshop user");
        var localPart = source.Split('@')[0];
        var parts = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var firstName = parts.Length > 0 ? Capitalize(parts[0]) : "eShop";
        var lastName = parts.Length > 1 ? Capitalize(parts[^1]) : "User";
        return (firstName, lastName);
    }

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private static SubscriptionDto MapSubscription(MaxioSubscription subscription, bool alreadySubscribed) =>
        new()
        {
            SubscriptionId = subscription.Id,
            Reference = subscription.Reference,
            State = subscription.State ?? string.Empty,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? string.Empty,
            Price = CentsToPrice(subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents),
            Currency = subscription.Currency,
            Interval = subscription.Product?.Interval ?? 1,
            IntervalUnit = subscription.Product?.IntervalUnit ?? "month",
            NextBillingDate = subscription.CurrentPeriodEndsAt,
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt,
            AlreadySubscribed = alreadySubscribed
        };

    private static decimal CentsToPrice(int? cents) => (cents ?? 0) / 100m;
}
