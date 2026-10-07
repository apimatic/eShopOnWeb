using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private static readonly HashSet<string> ReusableStates = new(StringComparer.OrdinalIgnoreCase)
    { "active", "trialing", "awaiting_signup", "on_hold", "past_due" };

    private static readonly string DuplicateReferenceMarker = "reference";

    private readonly IMaxioApiClient _apiClient;
    private readonly MaxioOptions _options;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _userLocks = new(StringComparer.Ordinal);

    public MaxioSubscriptionService(IMaxioApiClient apiClient, Microsoft.Extensions.Options.IOptions<MaxioOptions> options)
    {
        _apiClient = apiClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<MaxioProduct>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _apiClient.ListProductsAsync(cancellationToken: cancellationToken);
        return products
            .Where(p => !p.ArchivedAt.HasValue &&
                        string.Equals(p.ProductFamily.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.PriceInCents)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<MaxioSubscription> SubscribeAsync(string userId, string userName, string? email, string productHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new MaxioPlanNotFoundException(productHandle ?? string.Empty);
        }

        var userLock = _userLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var product = await ResolvePlanAsync(productHandle, cancellationToken);
            var customer = await EnsureCustomerAsync(userId, userName, email, cancellationToken);

            var existing = await FindReusableSubscriptionAsync(customer.Id, product.Handle, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            var reference = BuildSubscriptionReference(userId, product.Handle);
            var request = new MaxioCreateSubscriptionRequest
            {
                ProductHandle = product.Handle,
                CustomerId = customer.Id,
                Reference = reference
            };
            try
            {
                return await _apiClient.CreateSubscriptionAsync(request, cancellationToken);
            }
            catch (MaxioApiException ex) when (ex.StatusCode == 422 && ContainsDuplicateReferenceError(ex.Errors))
            {
                var taken = (await _apiClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken))
                    .FirstOrDefault(s => string.Equals(s.Reference, reference, StringComparison.Ordinal) &&
                                         ReusableStates.Contains(s.State));
                if (taken is not null)
                {
                    return taken;
                }
                request.Reference = $"{reference}:{Guid.NewGuid():N}";
                return await _apiClient.CreateSubscriptionAsync(request, cancellationToken);
            }
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var customer = await _apiClient.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<MaxioSubscription>();
        }
        return await _apiClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<MaxioProduct> ResolvePlanAsync(string productHandle, CancellationToken cancellationToken)
    {
        var product = await _apiClient.GetProductByHandleAsync(productHandle, cancellationToken);
        if (product is null || product.ArchivedAt.HasValue ||
            !string.Equals(product.ProductFamily.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new MaxioPlanNotFoundException(productHandle);
        }
        return product;
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string userId, string userName, string? email, CancellationToken cancellationToken)
    {
        var existing = await _apiClient.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveCustomerName(userName, email);
        var createRequest = new MaxioCreateCustomerRequest
        {
            Reference = userId,
            FirstName = firstName,
            LastName = lastName,
            Email = string.IsNullOrWhiteSpace(email) ? $"{userName}@users.noreply.eshoponweb" : email
        };
        try
        {
            return await _apiClient.CreateCustomerAsync(createRequest, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422 && ContainsDuplicateReferenceError(ex.Errors))
        {
            var racedCustomer = await _apiClient.FindCustomerByReferenceAsync(userId, cancellationToken);
            if (racedCustomer is not null)
            {
                return racedCustomer;
            }
            throw;
        }
    }

    private async Task<MaxioSubscription?> FindReusableSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _apiClient.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            ReusableStates.Contains(s.State) &&
            string.Equals(s.Product.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsDuplicateReferenceError(IReadOnlyList<string> errors)
    {
        return errors.Any(e => e.Contains(DuplicateReferenceMarker, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildSubscriptionReference(string userId, string productHandle)
    {
        var raw = $"{userId}:{productHandle}";
        return raw.Length > 100 ? raw[..100] : raw;
    }

    private static (string FirstName, string LastName) DeriveCustomerName(string userName, string? email)
    {
        var source = !string.IsNullOrWhiteSpace(email) ? email : userName;
        var localPart = source.Split('@')[0];
        var tokens = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var firstName = Capitalize(tokens.Length > 0 ? tokens[0] : null) ?? "eShopOnWeb";
        var lastName = tokens.Length > 1 ? Capitalize(string.Join(" ", tokens.Skip(1))) : null;
        return (firstName, lastName ?? "Customer");
    }

    private static string? Capitalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return char.ToUpper(value[0], CultureInfo.InvariantCulture) + value[1..].ToLower(CultureInfo.InvariantCulture);
    }
}
