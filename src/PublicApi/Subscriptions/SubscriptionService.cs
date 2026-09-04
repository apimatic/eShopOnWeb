using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken);

    Task<SubscriptionDto> SubscribeAsync(ClaimsPrincipal principal, string planHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}

public sealed class MaxioSubscriptionService : ISubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new(StringComparer.Ordinal);

    private readonly IMaxioClient _maxioClient;
    private readonly AppIdentityDbContext _identityDbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly MaxioOptions _options;

    public MaxioSubscriptionService(
        IMaxioClient maxioClient,
        AppIdentityDbContext identityDbContext,
        UserManager<ApplicationUser> userManager,
        IOptions<MaxioOptions> options)
    {
        _maxioClient = maxioClient;
        _identityDbContext = identityDbContext;
        _userManager = userManager;
        _options = options.Value;
        _options.Validate();
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxioClient.ListProductsAsync(_options.ProductFamilyHandle, cancellationToken);
        return products
            .Where(product => !string.IsNullOrWhiteSpace(product.Handle) && product.ArchivedAt == null)
            .Select(ToPlan)
            .ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(ClaimsPrincipal principal, string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new SubscriptionPlanNotFoundException(planHandle);

        var userName = GetUserName(principal);
        var user = await _userManager.FindByNameAsync(userName);
        if (user == null)
            throw new SubscriptionUserNotFoundException(userName);

        var products = await _maxioClient.ListProductsAsync(_options.ProductFamilyHandle, cancellationToken);
        var product = products.FirstOrDefault(candidate =>
            candidate.ArchivedAt == null &&
            string.Equals(candidate.Handle, planHandle.Trim(), StringComparison.OrdinalIgnoreCase));
        if (product?.Handle == null)
            throw new SubscriptionPlanNotFoundException(planHandle);

        var canonicalPlanHandle = product.Handle;
        var subscriptionReference = BuildSubscriptionReference(user.Id, canonicalPlanHandle);
        var gate = UserLocks.GetOrAdd(user.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var mapping = await _identityDbContext.MaxioSubscriptionMappings
                .SingleOrDefaultAsync(item => item.UserId == user.Id && item.PlanHandle == canonicalPlanHandle, cancellationToken);

            if (mapping != null)
            {
                try
                {
                    var current = await _maxioClient.GetSubscriptionAsync(mapping.MaxioSubscriptionId, cancellationToken);
                    return ToSubscription(current, canonicalPlanHandle, mapping.SubscriptionReference);
                }
                catch (MaxioApiException exception) when (exception.StatusCode == 404)
                {
                    _identityDbContext.MaxioSubscriptionMappings.Remove(mapping);
                    await _identityDbContext.SaveChangesAsync(cancellationToken);
                }
            }

            var customer = await EnsureCustomerAsync(user, cancellationToken);
            var existing = (await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken))
                .FirstOrDefault(subscription => string.Equals(subscription.Reference, subscriptionReference, StringComparison.Ordinal));

            if (existing == null)
            {
                try
                {
                    existing = await _maxioClient.CreateSubscriptionAsync(
                        canonicalPlanHandle,
                        customer.Id,
                        subscriptionReference,
                        cancellationToken);
                }
                catch (MaxioApiException exception) when (exception.StatusCode == 422)
                {
                    // A second process may have won the create race. Re-read Maxio before surfacing an error.
                    existing = (await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken))
                        .FirstOrDefault(subscription => string.Equals(subscription.Reference, subscriptionReference, StringComparison.Ordinal));
                    if (existing == null)
                        throw;
                }
            }

            await SaveMappingAsync(user.Id, canonicalPlanHandle, customer.Id, existing, subscriptionReference, cancellationToken);
            return ToSubscription(existing, canonicalPlanHandle, subscriptionReference);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var userName = GetUserName(principal);
        var user = await _userManager.FindByNameAsync(userName);
        if (user == null)
            throw new SubscriptionUserNotFoundException(userName);

        var customerReference = BuildCustomerReference(user.Id);
        var customer = await _maxioClient.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer == null)
            return Array.Empty<SubscriptionDto>();

        var prefix = $"eshoponweb:{user.Id}:";
        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .Where(subscription => subscription.Reference?.StartsWith(prefix, StringComparison.Ordinal) == true)
            .Select(subscription => ToSubscription(subscription, subscription.Product?.Handle ?? string.Empty, subscription.Reference ?? string.Empty))
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var reference = BuildCustomerReference(user.Id);
        var existing = await _maxioClient.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing != null)
            return existing;

        var (firstName, lastName) = SplitName(user.Email ?? user.UserName ?? user.Id);
        try
        {
            return await _maxioClient.CreateCustomerAsync(firstName, lastName, user.Email ?? user.UserName ?? reference, reference, cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == 422)
        {
            // Maxio enforces reference uniqueness. If another request created it first, use that customer.
            var createdByOtherRequest = await _maxioClient.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (createdByOtherRequest != null)
                return createdByOtherRequest;
            throw;
        }
    }

    private async Task SaveMappingAsync(
        string userId,
        string planHandle,
        int customerId,
        MaxioSubscription subscription,
        string reference,
        CancellationToken cancellationToken)
    {
        var mapping = await _identityDbContext.MaxioSubscriptionMappings
            .SingleOrDefaultAsync(item => item.UserId == userId && item.PlanHandle == planHandle, cancellationToken);
        var now = DateTime.UtcNow;
        if (mapping == null)
        {
            mapping = new MaxioSubscriptionMapping
            {
                UserId = userId,
                PlanHandle = planHandle,
                CreatedAtUtc = now
            };
            _identityDbContext.MaxioSubscriptionMappings.Add(mapping);
        }

        mapping.MaxioCustomerId = customerId;
        mapping.MaxioSubscriptionId = subscription.Id;
        mapping.SubscriptionReference = reference;
        mapping.UpdatedAtUtc = now;
        await _identityDbContext.SaveChangesAsync(cancellationToken);
    }

    private static string GetUserName(ClaimsPrincipal principal)
    {
        return principal.FindFirstValue(ClaimTypes.Name)
            ?? principal.Identity?.Name
            ?? throw new SubscriptionUserNotFoundException("unknown");
    }

    private static (string FirstName, string LastName) SplitName(string value)
    {
        var localPart = value.Split('@', 2)[0];
        var words = localPart.Split(new[] { '.', '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return words.Length switch
        {
            0 => ("eShopOnWeb", "Customer"),
            1 => (words[0], "Customer"),
            _ => (words[0], string.Join(' ', words.Skip(1)))
        };
    }

    private static string BuildCustomerReference(string userId) => $"eshoponweb:{userId}";

    private static string BuildSubscriptionReference(string userId, string planHandle) => $"eshoponweb:{userId}:{planHandle}";

    private static SubscriptionPlanDto ToPlan(MaxioProduct product) => new(
        product.Handle!,
        product.Name,
        product.PriceInCents,
        product.Interval,
        product.IntervalUnit,
        product.RequireCreditCard);

    private static SubscriptionDto ToSubscription(MaxioSubscription subscription, string fallbackPlanHandle, string reference) => new(
        subscription.Id,
        subscription.Product?.Handle ?? fallbackPlanHandle,
        subscription.Product?.Name ?? string.Empty,
        subscription.ProductPriceInCents,
        subscription.Currency,
        subscription.State,
        subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        reference);
}

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    bool PaymentMethodRequired);

public sealed record SubscriptionDto(
    int Id,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    string? Currency,
    string State,
    DateTimeOffset? NextBillingDate,
    string Reference);
