using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>Application boundary around Maxio Advanced Billing subscription operations.</summary>
public sealed class MaxioSubscriptionService
{
    public const string HttpClientName = "MaxioAdvancedBilling";
    private const int ProviderPageSize = 100;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnrollmentLocks = new(StringComparer.Ordinal);
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, IOptions<MaxioOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanResponse>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var plans = new List<SubscriptionPlanResponse>();

        for (var page = 1; ; page++)
        {
            var pageItems = await CallAsync(ct => _client.Products.ListProducts(
                dateField: null,
                filter: null,
                endDate: null,
                endDatetime: null,
                startDate: null,
                startDatetime: null,
                includeArchived: false,
                include: null,
                page: page,
                perPage: ProviderPageSize,
                ct: ct), cancellationToken);

            plans.AddRange(pageItems
                .Select(item => item.Product)
                .Where(product => product.ArchivedAt is null &&
                    string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(product.Handle))
                .Select(product => new SubscriptionPlanResponse(
                    product.Handle!,
                    product.Name ?? product.Handle!,
                    (product.PriceInCents ?? 0) / 100m,
                    product.Interval,
                    product.IntervalUnit?.Value)));

            if (pageItems.Count < ProviderPageSize)
            {
                return plans;
            }
        }
    }

    public async Task<SubscriptionResponse> SubscribeAsync(ClaimsPrincipal principal, string productHandle, CancellationToken cancellationToken)
    {
        var subscriber = Subscriber.From(principal);
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new ArgumentException("A product handle is required.", nameof(productHandle));
        }

        var plan = await FindPlanAsync(productHandle, cancellationToken);
        if (plan is null)
        {
            throw new MaxioProviderException("The requested subscription plan is not available.", (int)HttpStatusCode.NotFound);
        }

        var subscriptionReference = CreateSubscriptionReference(subscriber.Reference, plan.Handle);
        var gate = EnrollmentLocks.GetOrAdd(subscriptionReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await TryFindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                return ToSubscriptionResponse(existing);
            }

            var customer = await GetOrCreateCustomerAsync(subscriber, cancellationToken);
            var paymentCollectionMethod = await GetPaymentCollectionMethodAsync(cancellationToken);
            var response = await CreateSubscriptionAsync(new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customer.Id,
                    ProductHandle = plan.Handle,
                    PaymentCollectionMethod = paymentCollectionMethod,
                    Reference = subscriptionReference
                }
            }, cancellationToken);

            return ToSubscriptionResponse(RequireSubscription(response));
        }
        catch (MaxioWriteRetryBlockedException ex)
        {
            var reconciled = await TryFindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (reconciled is not null)
            {
                return ToSubscriptionResponse(reconciled);
            }

            throw new MaxioProviderException("The subscription outcome could not be confirmed. Retry this request to reconcile it.", null, ex);
        }
        catch (MaxioProviderException ex) when (ex.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
        {
            var reconciled = await TryFindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (reconciled is not null)
            {
                return ToSubscriptionResponse(reconciled);
            }

            throw;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionResponse>> GetMySubscriptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var subscriber = Subscriber.From(principal);
        Customer? customer;
        try
        {
            customer = (await CallAsync(ct => _client.Customers.ReadCustomerByReference(subscriber.Reference, ct), cancellationToken)).Customer;
        }
        catch (MaxioProviderException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return Array.Empty<SubscriptionResponse>();
        }

        if (customer?.Id is not int customerId)
        {
            throw new MaxioProviderException("Maxio returned a customer without an identifier.");
        }

        var subscriptions = await CallAsync(ct => _client.Customers.ListCustomerSubscriptions(customerId, ct), cancellationToken);
        return subscriptions
            .Select(item => item.Subscription)
            .Where(subscription => subscription is not null)
            .Select(subscription => ToSubscriptionResponse(subscription!))
            .ToArray();
    }

    private async Task<SubscriptionPlanResponse?> FindPlanAsync(string requestedHandle, CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        return plans.SingleOrDefault(plan => string.Equals(plan.Handle, requestedHandle, StringComparison.Ordinal));
    }

    private async Task<MaxioAdvancedBilling.Models.Enums.CollectionMethod> GetPaymentCollectionMethodAsync(CancellationToken cancellationToken)
    {
        var site = (await CallAsync(ct => _client.Sites.ReadSite(ct), cancellationToken)).Site;
        return site.RelationshipInvoicingEnabled switch
        {
            true => MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance,
            false => MaxioAdvancedBilling.Models.Enums.CollectionMethod.Invoice,
            _ => throw new MaxioProviderException("Maxio did not report its invoicing architecture.")
        };
    }

    private async Task<Customer> GetOrCreateCustomerAsync(Subscriber subscriber, CancellationToken cancellationToken)
    {
        try
        {
            var existing = await CallAsync(ct => _client.Customers.ReadCustomerByReference(subscriber.Reference, ct), cancellationToken);
            return RequireCustomer(existing);
        }
        catch (MaxioProviderException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            try
            {
                var created = await CreateCustomerAsync(new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = subscriber.FirstName,
                        LastName = subscriber.LastName,
                        Email = subscriber.Email,
                        Reference = subscriber.Reference
                    }
                }, cancellationToken);
                return RequireCustomer(created);
            }
            catch (MaxioProviderException createException) when (createException.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
            {
                var reconciled = await CallAsync(ct => _client.Customers.ReadCustomerByReference(subscriber.Reference, ct), cancellationToken);
                return RequireCustomer(reconciled);
            }
        }
    }

    private async Task<Subscription?> TryFindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            return (await FindSubscriptionAsync(reference, cancellationToken)).Subscription;
        }
        catch (MaxioProviderException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<T> CallAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        try
        {
            return await BoundedAsync(operation, cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRawError(ex.Error, ex);
        }
    }

    private static async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            return await operation(timeout.Token);
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioProviderException("Maxio is currently unavailable.", null, ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MaxioProviderException("Maxio did not respond in time.", null, ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioProviderException("Maxio returned a response that could not be processed.", null, ex);
        }
    }

    private async Task<CustomerResponse> CreateCustomerAsync(CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        try
        {
            using var write = MaxioSingleSendHandler.BeginWriteScope();
            return await BoundedAsync(ct => _client.Customers.CreateCustomer(request, ct), cancellationToken);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                throw new MaxioProviderException("Maxio rejected the customer.", (int)HttpStatusCode.UnprocessableEntity, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw, ex);
            }
            throw new MaxioProviderException("Maxio rejected the customer.", null, ex);
        }
    }

    private async Task<MaxioAdvancedBilling.Models.SubscriptionResponse> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            return await BoundedAsync(ct => _client.Subscriptions.FindSubscription(reference, ct), cancellationToken);
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out var raw))
            {
                throw FromRawError(raw, ex);
            }
            if (ex.Error.TryGetRawError(out raw))
            {
                throw FromRawError(raw, ex);
            }
            throw new MaxioProviderException("Maxio could not find the subscription.", null, ex);
        }
    }

    private async Task<MaxioAdvancedBilling.Models.SubscriptionResponse> CreateSubscriptionAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            using var write = MaxioSingleSendHandler.BeginWriteScope();
            return await BoundedAsync(ct => _client.Subscriptions.CreateSubscription(request, ct), cancellationToken);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var validation))
            {
                // Keep the provider exception out of the chain: it can carry the raw provider body and
                // should not be emitted by a host's exception logger. The generated 422 payload supplies
                // the typed validation messages needed by the caller instead.
                throw new MaxioProviderException(ToSafeValidationMessage(validation.Errors), (int)HttpStatusCode.UnprocessableEntity);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw, ex);
            }
            throw new MaxioProviderException("Maxio rejected the subscription.", null, ex);
        }
    }

    private static Customer RequireCustomer(CustomerResponse response) =>
        response.Customer ?? throw new MaxioProviderException("Maxio returned an empty customer response.");

    private static Subscription RequireSubscription(MaxioAdvancedBilling.Models.SubscriptionResponse response) =>
        response.Subscription ?? throw new MaxioProviderException("Maxio returned an empty subscription response.");

    private static MaxioProviderException FromRawError(RawError error, Exception innerException) =>
        new("Maxio rejected the request.", (int)error.StatusCode, innerException);

    private static string ToSafeValidationMessage(IReadOnlyList<string> errors)
    {
        var details = errors
            .Where(error => !string.IsNullOrWhiteSpace(error))
            .Select(error => error.Replace('\r', ' ').Replace('\n', ' ').Trim())
            .Take(3)
            .ToArray();

        if (details.Length == 0)
        {
            return "Maxio rejected the subscription validation request.";
        }

        var message = $"Maxio rejected the subscription: {string.Join("; ", details)}";
        return message.Length <= 512 ? message : message[..512];
    }

    private static SubscriptionResponse ToSubscriptionResponse(Subscription subscription)
    {
        var priceInCents = subscription.CurrentBillingAmountInCents ?? subscription.ProductPriceInCents ?? 0;
        return new SubscriptionResponse(
            subscription.Reference ?? string.Empty,
            subscription.Product?.Handle ?? string.Empty,
            subscription.Product?.Name ?? subscription.Product?.Handle ?? "Subscription",
            priceInCents / 100m,
            subscription.State?.Value,
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);
    }

    private static string CreateSubscriptionReference(string customerReference, string productHandle) =>
        $"eshop-sub-{Hash(customerReference)[..24]}-{Hash(productHandle)[..24]}";

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record Subscriber(string Email, string FirstName, string LastName, string Reference)
    {
        public static Subscriber From(ClaimsPrincipal principal)
        {
            var email = principal.FindFirstValue(ClaimTypes.Name);
            if (string.IsNullOrWhiteSpace(email))
            {
                throw new ArgumentException("The authenticated user has no email identity.");
            }

            try
            {
                _ = new MailAddress(email);
            }
            catch (FormatException)
            {
                throw new ArgumentException("The authenticated user identity must be an email address.");
            }

            var localPart = email.Split('@')[0];
            var firstName = string.IsNullOrWhiteSpace(localPart) ? "Shopper" : localPart[..Math.Min(localPart.Length, 50)];
            return new Subscriber(email, firstName, "Customer", $"eshop-user-{Hash(email.ToUpperInvariant())[..32]}");
        }
    }
}
