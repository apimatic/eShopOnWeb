using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public class MaxioSubscriptionService : ISubscriptionService
{
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(30);
    private static readonly KeyedLock Locks = new();
    private static readonly int ProductsPerPage = 20;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly IRepository<MaxioCustomerLink> _customerLinks;
    private readonly IRepository<SubscriptionEnrollment> _enrollments;
    private readonly IAppLogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        MaxioOptions options,
        IRepository<MaxioCustomerLink> customerLinks,
        IRepository<SubscriptionEnrollment> enrollments,
        IAppLogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _options = options;
        _customerLinks = customerLinks;
        _enrollments = enrollments;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var productFamilyId = await ResolveProductFamilyIdAsync(cancellationToken);
        var products = await ListProductsForFamilyAsync(productFamilyId, cancellationToken);

        return products
            .Select(ToPlan)
            .Where(plan => plan is not null)
            .Select(plan => plan!)
            .ToList();
    }

    public async Task<SubscriptionDetails> SubscribeAsync(string userId, string planHandle, CancellationToken cancellationToken)
    {
        var plan = await GetPlanByHandleAsync(planHandle, cancellationToken);

        using (await Locks.LockAsync(SubscribeKey(userId, planHandle), cancellationToken))
        {
            var existing = await FindEnrollmentAsync(userId, planHandle, cancellationToken);
            if (existing is not null)
            {
                var existingSubscription = await TryFindSubscriptionByReferenceAsync(existing.SubscriptionReference, cancellationToken);
                if (existingSubscription is not null)
                {
                    await EnsureEnrollmentCompletedAsync(existing, existingSubscription, cancellationToken);
                    return ToDetails(existingSubscription, plan);
                }
            }

            await EnsureCustomerAsync(userId, cancellationToken);

            var subscriptionReference = BuildSubscriptionReference(userId, planHandle);
            var previousAttempt = await TryFindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (previousAttempt is not null)
            {
                await SaveEnrollmentAsync(userId, planHandle, previousAttempt, cancellationToken);
                return ToDetails(previousAttempt, plan);
            }

            var subscription = await CreateSubscriptionAsync(plan, userId, subscriptionReference, cancellationToken);

            await SaveEnrollmentAsync(userId, planHandle, subscription, cancellationToken);
            _logger.LogInformation("Created Maxio subscription {Reference} ({SubscriptionId}) for user {UserId} on plan {PlanHandle}",
                subscription.Reference, subscription.Id, userId, planHandle);

            return ToDetails(subscription, plan);
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> ListMySubscriptionsAsync(string userId, CancellationToken cancellationToken)
    {
        var localLink = await FindCustomerLinkAsync(userId, cancellationToken);
        if (localLink is not null)
        {
            try
            {
                var subscriptions = await CallAsync(
                    token => _client.Customers.ListCustomerSubscriptions(localLink.MaxioCustomerId, token),
                    cancellationToken);

                return MapSubscriptions(subscriptions);
            }
            catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
            }
            catch (SdkException<RawError> ex)
            {
                throw FromRawError(ex.Error);
            }
        }

        var customer = await TryFindCustomerByReferenceAsync(userId, cancellationToken);
        if (customer is null)
        {
            return new List<SubscriptionDetails>();
        }

        var customerId = customer.Id;
        if (!customerId.HasValue)
        {
            return new List<SubscriptionDetails>();
        }

        try
        {
            var subscriptions = await CallAsync(
                token => _client.Customers.ListCustomerSubscriptions(customerId.Value, token),
                cancellationToken);

            return MapSubscriptions(subscriptions);
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRawError(ex.Error);
        }
    }

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await CallAsync(
                token => _client.ProductFamilies.ListProductFamilies(
                    dateField: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    ct: token),
                cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRawError(ex.Error);
        }

        var productFamily = families
            .Select(family => family.ProductFamily)
            .FirstOrDefault(family => family is not null &&
                                      string.Equals(family.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (productFamily is null || !productFamily.Id.HasValue)
        {
            throw new MaxioApiException(
                (int)HttpStatusCode.NotFound,
                $"The configured product family '{_options.ProductFamilyHandle}' was not found on the Maxio site.");
        }

        return productFamily.Id.Value;
    }

    private async Task<List<Product>> ListProductsForFamilyAsync(int productFamilyId, CancellationToken cancellationToken)
    {
        var products = new List<Product>();
        var page = 1;

        while (true)
        {
            IReadOnlyList<ProductResponse> pageResponse;
            try
            {
                pageResponse = await CallAsync(
                    token => _client.ProductFamilies.ListProductsForProductFamily(
                        productFamilyId: productFamilyId.ToString(CultureInfo.InvariantCulture),
                        dateField: null,
                        filter: null,
                        startDate: null,
                        endDate: null,
                        startDatetime: null,
                        endDatetime: null,
                        includeArchived: false,
                        include: null,
                        page: page,
                        perPage: ProductsPerPage,
                        ct: token),
                    cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out var providerMessage))
                {
                    throw new MaxioApiException(
                        (int)HttpStatusCode.NotFound,
                        $"The configured product family could not be read from the Maxio site: {providerMessage}");
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw FromRawError(raw);
                }

                throw new MaxioApiException(
                    (int)HttpStatusCode.BadGateway,
                    "The billing provider returned an unreadable error response.");
            }

            products.AddRange(pageResponse
                .Select(response => response.Product)
                .Where(product => product is not null)
                .Select(product => product!));

            if (pageResponse.Count < ProductsPerPage)
            {
                break;
            }

            page++;
        }

        return products;
    }

    private async Task<SubscriptionPlan> GetPlanByHandleAsync(string planHandle, CancellationToken cancellationToken)
    {
        Product product;
        try
        {
            var response = await CallAsync(
                token => _client.Products.ReadProductByHandle(planHandle, token),
                cancellationToken);

            product = response.Product;
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                throw new SubscriptionPlanNotFoundException(planHandle);
            }

            throw FromRawError(ex.Error);
        }

        var belongsToFamily = product.ProductFamily is not null &&
                              string.Equals(product.ProductFamily.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase);
        if (!belongsToFamily)
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        var plan = ToPlan(product);
        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        return plan;
    }

    private async Task<MaxioCustomerLink> EnsureCustomerAsync(string userId, CancellationToken cancellationToken)
    {
        using (await Locks.LockAsync(CustomerKey(userId), cancellationToken))
        {
            var localLink = await FindCustomerLinkAsync(userId, cancellationToken);
            if (localLink is not null)
            {
                return localLink;
            }

            var existingCustomer = await TryFindCustomerByReferenceAsync(userId, cancellationToken);
            if (existingCustomer is not null)
            {
                return await SaveCustomerLinkAsync(userId, existingCustomer, cancellationToken);
            }

            var createdCustomer = await CreateCustomerAsync(userId, cancellationToken);
            return await SaveCustomerLinkAsync(userId, createdCustomer, cancellationToken);
        }
    }

    private async Task<Customer?> TryFindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await CallAsync(
                token => _client.Customers.ReadCustomerByReference(reference, token),
                cancellationToken);

            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRawError(ex.Error);
        }
    }

    private async Task<Customer> CreateCustomerAsync(string userId, CancellationToken cancellationToken)
    {
        var email = userId;
        var (firstName, lastName) = SplitName(email);

        try
        {
            var body = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = userId
                }
            };

            var response = await CallAsync(
                token => _client.Customers.CreateCustomer(body, token),
                cancellationToken);

            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                var winner = await TryFindCustomerByReferenceAsync(userId, cancellationToken);
                if (winner is not null)
                {
                    return winner;
                }

                throw new MaxioApiException(
                    (int)HttpStatusCode.UnprocessableEntity,
                    "The billing provider could not create a customer for this account. Please try again.");
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw);
            }

            throw new MaxioApiException(
                (int)HttpStatusCode.BadGateway,
                "The billing provider returned an unreadable error response.");
        }
    }

    private async Task<MaxioCustomerLink> SaveCustomerLinkAsync(string userId, Customer customer, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerLinkAsync(userId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        if (!customer.Id.HasValue)
        {
            throw new MaxioApiException(
                (int)HttpStatusCode.BadGateway,
                "The billing provider created a customer without an id.");
        }

        var link = new MaxioCustomerLink(userId, customer.Reference ?? userId, customer.Id.Value, DateTimeOffset.UtcNow);
        await _customerLinks.AddAsync(link, cancellationToken);
        return link;
    }

    private async Task<MaxioCustomerLink?> FindCustomerLinkAsync(string userId, CancellationToken cancellationToken)
    {
        var links = await _customerLinks.ListAsync(new CustomerLinkByUserSpecification(userId), cancellationToken);
        return links.FirstOrDefault();
    }

    private async Task<Subscription> CreateSubscriptionAsync(SubscriptionPlan plan, string userId, string subscriptionReference, CancellationToken cancellationToken)
    {
        try
        {
            var body = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = plan.Handle,
                    CustomerReference = userId,
                    Reference = subscriptionReference,
                    NextBillingAt = ComputeNextBillingAt(plan)
                }
            };

            var response = await CallAsync(
                token => _client.Subscriptions.CreateSubscription(body, token),
                cancellationToken);

            return response.Subscription ??
                   throw new MaxioApiException(
                       (int)HttpStatusCode.BadGateway,
                       "The billing provider accepted the subscription but returned no subscription data.");
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                var winner = await TryFindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
                if (winner is not null)
                {
                    return winner;
                }

                var messages = errorList.Errors is { Count: > 0 }
                    ? string.Join("; ", errorList.Errors)
                    : "The billing provider rejected the subscription.";
                throw new MaxioApiException((int)HttpStatusCode.UnprocessableEntity, messages, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw);
            }

            throw new MaxioApiException(
                (int)HttpStatusCode.BadGateway,
                "The billing provider returned an unreadable error response.");
        }
    }

    private async Task<Subscription?> TryFindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await CallAsync(
                token => _client.Subscriptions.FindSubscription(reference: reference, ct: token),
                cancellationToken);

            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw);
            }

            throw new MaxioApiException(
                (int)HttpStatusCode.BadGateway,
                "The billing provider returned an unreadable error response.");
        }
    }

    private async Task<SubscriptionEnrollment?> FindEnrollmentAsync(string userId, string planHandle, CancellationToken cancellationToken)
    {
        var enrollments = await _enrollments.ListAsync(new EnrollmentByUserAndPlanSpecification(userId, planHandle), cancellationToken);
        return enrollments.FirstOrDefault();
    }

    private async Task SaveEnrollmentAsync(string userId, string planHandle, Subscription subscription, CancellationToken cancellationToken)
    {
        var reference = subscription.Reference ?? BuildSubscriptionReference(userId, planHandle);
        var existing = await FindEnrollmentAsync(userId, planHandle, cancellationToken);
        if (existing is not null)
        {
            if (subscription.Id.HasValue && existing.MaxioSubscriptionId != subscription.Id)
            {
                existing.MarkCompleted(subscription.Id.Value);
                await _enrollments.UpdateAsync(existing, cancellationToken);
            }

            return;
        }

        if (!subscription.Id.HasValue)
        {
            throw new MaxioApiException(
                (int)HttpStatusCode.BadGateway,
                "The billing provider created a subscription without an id.");
        }

        var enrollment = new SubscriptionEnrollment(userId, planHandle, reference, DateTimeOffset.UtcNow);
        enrollment.MarkCompleted(subscription.Id.Value);
        await _enrollments.AddAsync(enrollment, cancellationToken);
    }

    private async Task EnsureEnrollmentCompletedAsync(SubscriptionEnrollment enrollment, Subscription subscription, CancellationToken cancellationToken)
    {
        if (enrollment.MaxioSubscriptionId is null && subscription.Id.HasValue)
        {
            enrollment.MarkCompleted(subscription.Id.Value);
            await _enrollments.UpdateAsync(enrollment, cancellationToken);
        }
    }

    private List<SubscriptionDetails> MapSubscriptions(IReadOnlyList<SubscriptionResponse> subscriptions)
    {
        var details = new List<SubscriptionDetails>();
        foreach (var response in subscriptions)
        {
            var subscription = response.Subscription;
            if (subscription is null)
            {
                continue;
            }

            details.Add(ToDetails(subscription, null));
        }

        return details;
    }

    private static SubscriptionDetails ToDetails(Subscription subscription, SubscriptionPlan? plan)
    {
        var product = subscription.Product;
        var price = ToMoney(product?.PriceInCents ?? subscription.ProductPriceInCents) ?? plan?.Price;

        return new SubscriptionDetails(
            subscription.Id,
            subscription.Reference ?? string.Empty,
            product?.Handle ?? plan?.Handle,
            product?.Name ?? plan?.Name,
            price,
            subscription.State?.Value,
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            subscription.CreatedAt);
    }

    private static SubscriptionPlan? ToPlan(Product product)
    {
        if (string.IsNullOrWhiteSpace(product.Handle))
        {
            return null;
        }

        return new SubscriptionPlan(
            product.Handle,
            product.Name ?? product.Handle,
            ToMoney(product.PriceInCents) ?? 0m,
            product.IntervalUnit?.Value ?? "month",
            product.Interval is { } interval && interval > 0 ? interval : 1);
    }

    private static decimal? ToMoney(long? cents)
    {
        return cents is { } value ? value / 100m : null;
    }

    private static MaxioApiException FromRawError(RawError raw)
    {
        return new MaxioApiException(
            (int)raw.StatusCode,
            "The billing provider rejected the request.");
    }

    private async Task<T> CallAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(CallTimeout);

        try
        {
            return await operation(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new MaxioApiException(
                (int)HttpStatusCode.BadGateway,
                "The billing provider did not respond in time.",
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioApiException(
                (int)HttpStatusCode.BadGateway,
                "The billing provider could not be reached.",
                ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new MaxioApiException(
                (int)HttpStatusCode.BadGateway,
                "The billing provider returned a response that could not be processed.",
                ex);
        }
    }

    private static (string FirstName, string LastName) SplitName(string email)
    {
        var localPart = email;
        var atIndex = email.IndexOf('@');
        if (atIndex > 0)
        {
            localPart = email.Substring(0, atIndex);
        }

        var segments = localPart.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 2)
        {
            return (segments[0], string.Join(" ", segments.Skip(1)));
        }

        return (localPart, "Maxio Customer");
    }

    private static DateTimeOffset ComputeNextBillingAt(SubscriptionPlan plan)
    {
        var now = DateTimeOffset.UtcNow;
        return string.Equals(plan.Interval, "day", StringComparison.OrdinalIgnoreCase)
            ? now.AddDays(plan.IntervalLength)
            : now.AddMonths(plan.IntervalLength);
    }

    private static string BuildSubscriptionReference(string userId, string planHandle)
    {
        return $"eshop-sub:{userId}:{planHandle}";
    }

    private static string SubscribeKey(string userId, string planHandle)
    {
        return $"subscribe:{userId}:{planHandle}";
    }

    private static string CustomerKey(string userId)
    {
        return $"customer:{userId}";
    }

    private sealed class CustomerLinkByUserSpecification : Specification<MaxioCustomerLink>
    {
        public CustomerLinkByUserSpecification(string userId)
        {
            Query.Where(link => link.UserId == userId);
        }
    }

    private sealed class EnrollmentByUserAndPlanSpecification : Specification<SubscriptionEnrollment>
    {
        public EnrollmentByUserAndPlanSpecification(string userId, string planHandle)
        {
            Query.Where(enrollment => enrollment.UserId == userId && enrollment.PlanHandle == planHandle);
        }
    }
}
