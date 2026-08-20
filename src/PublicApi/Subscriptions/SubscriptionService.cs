using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionService
{
    private static readonly TimeSpan[] LookupDelays =
    {
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(300),
        TimeSpan.FromMilliseconds(900)
    };

    private readonly IMaxioClient _maxioClient;
    private readonly IRepository<UserSubscription> _repository;
    private readonly SubscriptionOperationLock _operationLock;
    private readonly MaxioOptions _options;

    public SubscriptionService(IMaxioClient maxioClient, IRepository<UserSubscription> repository,
        SubscriptionOperationLock operationLock, IOptions<MaxioOptions> options)
    {
        _maxioClient = maxioClient;
        _repository = repository;
        _operationLock = operationLock;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxioClient.ListProductsAsync(cancellationToken);
        return products
            .Where(IsConfiguredPlan)
            .OrderBy(product => product.PriceInCents)
            .ThenBy(product => product.Name, StringComparer.Ordinal)
            .Select(ToPlanDto)
            .ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(ApplicationUser user, string requestedProductHandle,
        CancellationToken cancellationToken)
    {
        await using var operation = await _operationLock.AcquireAsync(user.Id, cancellationToken);

        var plan = (await _maxioClient.ListProductsAsync(cancellationToken))
            .SingleOrDefault(product => IsConfiguredPlan(product) &&
                string.Equals(product.Handle, requestedProductHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
            throw new SubscriptionPlanNotFoundException(requestedProductHandle);

        var customer = await EnsureCustomerAsync(user, cancellationToken);
        var reference = CreateSubscriptionReference(user.Id, plan.Handle);
        var subscription = await _maxioClient.FindSubscriptionAsync(reference, cancellationToken)
            ?? await CreateSubscriptionAsync(customer.Id, plan.Handle, reference, cancellationToken);

        ValidateSubscription(subscription, customer.Id, plan.Handle);
        await SynchronizeAsync(user.Id, subscription, reference, cancellationToken);
        return ToSubscriptionDto(subscription);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(ApplicationUser user,
        CancellationToken cancellationToken)
    {
        await using var operation = await _operationLock.AcquireAsync(user.Id, cancellationToken);
        var customer = await _maxioClient.FindCustomerAsync(user.Id, cancellationToken);
        if (customer is null)
            return Array.Empty<SubscriptionDto>();

        var subscriptions = (await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken))
            .Where(subscription => subscription.Product is not null && IsConfiguredPlan(subscription.Product))
            .OrderBy(subscription => subscription.Id)
            .ToList();

        foreach (var subscription in subscriptions)
        {
            var reference = string.IsNullOrWhiteSpace(subscription.Reference)
                ? $"maxio-{subscription.Id}"
                : subscription.Reference;
            await SynchronizeAsync(user.Id, subscription, reference!, cancellationToken);
        }

        return subscriptions.Select(ToSubscriptionDto).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user,
        CancellationToken cancellationToken)
    {
        var customer = await _maxioClient.FindCustomerAsync(user.Id, cancellationToken);
        if (customer is not null)
            return customer;

        var email = user.Email ?? user.UserName
            ?? throw new MaxioDataIntegrityException("The authenticated user does not have an email address.");
        var localPart = email.Split('@', 2)[0];
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart;
        var draft = new MaxioCustomerDraft(firstName, "Customer", email, user.Id);
        var token = CreateUniquenessToken("customer", user.Id);

        try
        {
            return await _maxioClient.CreateCustomerAsync(draft, token, cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode is HttpStatusCode.Conflict
            or HttpStatusCode.UnprocessableEntity)
        {
            var existing = await FindCustomerAfterAmbiguousCreateAsync(user.Id, cancellationToken);
            if (existing is not null)
                return existing;
            throw;
        }
        catch (HttpRequestException)
        {
            var existing = await FindCustomerAfterAmbiguousCreateAsync(user.Id, cancellationToken);
            if (existing is not null)
                return existing;
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var existing = await FindCustomerAfterAmbiguousCreateAsync(user.Id, cancellationToken);
            if (existing is not null)
                return existing;
            throw;
        }
    }

    private async Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string productHandle,
        string reference, CancellationToken cancellationToken)
    {
        var draft = new MaxioSubscriptionDraft(customerId, productHandle, reference);
        var token = CreateUniquenessToken("subscription", reference);
        try
        {
            return await _maxioClient.CreateSubscriptionAsync(draft, token, cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode is HttpStatusCode.Conflict
            or HttpStatusCode.UnprocessableEntity)
        {
            var existing = await FindSubscriptionAfterAmbiguousCreateAsync(reference, cancellationToken);
            if (existing is not null)
                return existing;
            throw;
        }
        catch (HttpRequestException)
        {
            var existing = await FindSubscriptionAfterAmbiguousCreateAsync(reference, cancellationToken);
            if (existing is not null)
                return existing;
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var existing = await FindSubscriptionAfterAmbiguousCreateAsync(reference, cancellationToken);
            if (existing is not null)
                return existing;
            throw;
        }
    }

    private async Task<MaxioCustomer?> FindCustomerAfterAmbiguousCreateAsync(string reference,
        CancellationToken cancellationToken)
    {
        foreach (var delay in LookupDelays)
        {
            var customer = await _maxioClient.FindCustomerAsync(reference, cancellationToken);
            if (customer is not null)
                return customer;
            await Task.Delay(delay, cancellationToken);
        }
        return await _maxioClient.FindCustomerAsync(reference, cancellationToken);
    }

    private async Task<MaxioSubscription?> FindSubscriptionAfterAmbiguousCreateAsync(string reference,
        CancellationToken cancellationToken)
    {
        foreach (var delay in LookupDelays)
        {
            var subscription = await _maxioClient.FindSubscriptionAsync(reference, cancellationToken);
            if (subscription is not null)
                return subscription;
            await Task.Delay(delay, cancellationToken);
        }
        return await _maxioClient.FindSubscriptionAsync(reference, cancellationToken);
    }

    private async Task SynchronizeAsync(string userId, MaxioSubscription subscription, string reference,
        CancellationToken cancellationToken)
    {
        if (subscription.Product is null)
            throw new MaxioDataIntegrityException($"Maxio subscription {subscription.Id} has no plan.");

        var specification = new UserSubscriptionByMaxioIdSpecification(subscription.Id);
        var local = await _repository.FirstOrDefaultAsync(specification, cancellationToken);
        if (local is null)
        {
            local = new UserSubscription(userId, subscription.Customer.Id, subscription.Id, reference,
                subscription.Product.Handle, subscription.Product.Name, subscription.ProductPriceInCents,
                subscription.Product.Interval, subscription.Product.IntervalUnit, subscription.State,
                subscription.NextAssessmentAt);
            await _repository.AddAsync(local, cancellationToken);
            return;
        }

        if (!string.Equals(local.UserId, userId, StringComparison.Ordinal))
            throw new MaxioDataIntegrityException($"Maxio subscription {subscription.Id} is mapped to another user.");

        local.Synchronize(subscription.Customer.Id, subscription.Id, subscription.Product.Handle,
            subscription.Product.Name, subscription.ProductPriceInCents, subscription.Product.Interval,
            subscription.Product.IntervalUnit, subscription.State, subscription.NextAssessmentAt);
        await _repository.UpdateAsync(local, cancellationToken);
    }

    private bool IsConfiguredPlan(MaxioProduct product) =>
        product.ArchivedAt is null &&
        string.Equals(product.ProductFamily.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase);

    private static void ValidateSubscription(MaxioSubscription subscription, long customerId, string productHandle)
    {
        if (subscription.Customer.Id != customerId || subscription.Product is null ||
            !string.Equals(subscription.Product.Handle, productHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new MaxioDataIntegrityException(
                "The subscription returned by Maxio does not match the requested customer and plan.");
        }
    }

    private static SubscriptionPlanDto ToPlanDto(MaxioProduct product) =>
        new(product.Handle, product.Name, product.Description, product.PriceInCents,
            product.PriceInCents / 100m, product.Interval, product.IntervalUnit);

    private static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription)
    {
        var product = subscription.Product
            ?? throw new MaxioDataIntegrityException($"Maxio subscription {subscription.Id} has no plan.");
        return new SubscriptionDto(subscription.Id, product.Handle, product.Name,
            subscription.ProductPriceInCents, subscription.ProductPriceInCents / 100m,
            product.Interval, product.IntervalUnit, subscription.State, subscription.NextAssessmentAt);
    }

    private static string CreateSubscriptionReference(string userId, string productHandle)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}\n{productHandle.ToLowerInvariant()}"));
        return $"eshop-{Convert.ToHexString(hash[..24]).ToLowerInvariant()}";
    }

    private static string CreateUniquenessToken(string scope, string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"eshop-maxio\n{scope}\n{value}"));
        return new Guid(hash[..16]).ToString();
    }
}
