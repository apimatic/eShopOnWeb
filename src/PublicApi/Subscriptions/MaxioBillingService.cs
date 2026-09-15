using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioBillingService
{
    public const string HttpClientName = "MaxioAdvancedBilling";
    private const int PageSize = 100;
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _settings;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _operationLocks = new(StringComparer.Ordinal);

    public MaxioBillingService(MaxioAdvancedBillingClient client, IOptions<MaxioOptions> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        _settings.Validate();
        var plans = new List<SubscriptionPlanDto>();

        for (var page = 1; ; page++)
        {
            IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse> response;
            try
            {
                response = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: "handle:" + _settings.ProductFamilyHandle,
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: page,
                    perPage: PageSize,
                    ct: cancellationToken);
            }
            catch (SdkException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError> exception)
            {
                throw TranslateTypedError(exception.Error, "Unable to load subscription plans.");
            }
            catch (Exception exception) when (IsProviderBoundaryException(exception))
            {
                throw TranslateTransportError(exception, "Unable to load subscription plans.");
            }

            foreach (var item in response)
            {
                if (item.Product is not null)
                {
                    plans.Add(MapPlan(item.Product));
                }
            }

            if (response.Count < PageSize)
            {
                break;
            }
        }

        return plans;
    }

    public async Task<SubscribeResponse> SubscribeAsync(ClaimsPrincipal principal, string? productHandle, CancellationToken cancellationToken)
    {
        var identity = GetIdentity(principal);
        var planHandle = productHandle?.Trim();
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new MaxioServiceException(400, "ProductHandle is required.");
        }

        var plans = await GetPlansAsync(cancellationToken);
        if (!plans.Any(plan => string.Equals(plan.Handle, planHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new MaxioServiceException(400, "The selected subscription plan is not available.");
        }

        var customerReference = CreateReference("customer", identity);
        var subscriptionReference = CreateReference("subscription", identity + "\n" + planHandle.ToLowerInvariant());
        var gate = _operationLocks.GetOrAdd(subscriptionReference, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(principal, identity, customerReference, cancellationToken);
            var existing = await FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                return new SubscribeResponse
                {
                    Subscription = MapSubscription(existing),
                    AlreadyExisted = true
                };
            }

            MaxioAdvancedBilling.Models.Subscription subscription;
            try
            {
                using (MaxioWriteGuardHandler.BeginWrite())
                {
                    var request = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
                    {
                        Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                        {
                            ProductHandle = planHandle,
                            CustomerId = customer.Id,
                            Reference = subscriptionReference,
                            PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Invoice
                        }
                    };
                    var response = await _client.Subscriptions.CreateSubscription(request, ct: cancellationToken);
                    subscription = response.Subscription
                        ?? throw new MaxioServiceException(502, "Maxio returned an empty subscription response.");
                }
            }
            catch (MaxioWriteRetrySuppressedException)
            {
                var reconciled = await FindSubscriptionAsync(subscriptionReference, cancellationToken);
                if (reconciled is null)
                {
                    throw new MaxioServiceException(503, "Maxio did not confirm the subscription after an interrupted request.");
                }

                subscription = reconciled;
            }
            catch (SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError> exception)
            {
                var reconciled = await FindSubscriptionAsync(subscriptionReference, cancellationToken);
                if (reconciled is not null)
                {
                    subscription = reconciled;
                }
                else
                {
                    throw TranslateTypedError(exception.Error, "Maxio rejected the subscription.");
                }
            }
            catch (Exception exception) when (IsProviderBoundaryException(exception))
            {
                var reconciled = await FindSubscriptionAsync(subscriptionReference, cancellationToken);
                if (reconciled is not null)
                {
                    subscription = reconciled;
                }
                else
                {
                    throw TranslateTransportError(exception, "Unable to confirm the Maxio subscription.");
                }
            }

            return new SubscribeResponse { Subscription = MapSubscription(subscription) };
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<MySubscriptionsResponse> GetMySubscriptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        _settings.Validate();
        var identity = GetIdentity(principal);
        var customerReference = CreateReference("customer", identity);
        var customer = await ReadCustomerAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return new MySubscriptionsResponse();
        }

        IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse> response;
        try
        {
            if (customer.Id is not int customerId)
            {
                throw new MaxioServiceException(502, "Maxio returned a customer without an identifier.");
            }

            response = await _client.Customers.ListCustomerSubscriptions(customerId, ct: cancellationToken);
        }
        catch (SdkException<RawError> exception)
        {
            throw TranslateRawError(exception.Error, "Unable to load your subscriptions.");
        }
        catch (Exception exception) when (IsProviderBoundaryException(exception))
        {
            throw TranslateTransportError(exception, "Unable to load your subscriptions.");
        }

        return new MySubscriptionsResponse
        {
            Subscriptions = response
                .Where(item => item.Subscription is not null)
                .Select(item => MapSubscription(item.Subscription!))
                .ToArray()
        };
    }

    private async Task<MaxioAdvancedBilling.Models.Customer> EnsureCustomerAsync(
        ClaimsPrincipal principal,
        string identity,
        string reference,
        CancellationToken cancellationToken)
    {
        var existing = await ReadCustomerAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName, email) = GetCustomerDetails(principal, identity);
        try
        {
            using (MaxioWriteGuardHandler.BeginWrite())
            {
                var request = new MaxioAdvancedBilling.Models.CreateCustomerRequest
                {
                    Customer = new MaxioAdvancedBilling.Models.CreateCustomer
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = email,
                        Reference = reference
                    }
                };
                var response = await _client.Customers.CreateCustomer(request, ct: cancellationToken);
                return response.Customer;
            }
        }
        catch (MaxioWriteRetrySuppressedException)
        {
            return await ReconcileCustomerAsync(reference, cancellationToken);
        }
        catch (SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError> exception)
        {
            var reconciled = await ReadCustomerAsync(reference, cancellationToken);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw TranslateTypedError(exception.Error, "Maxio rejected the customer.");
        }
        catch (Exception exception) when (IsProviderBoundaryException(exception))
        {
            var reconciled = await ReadCustomerAsync(reference, cancellationToken);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw TranslateTransportError(exception, "Unable to confirm the Maxio customer.");
        }
    }

    private async Task<MaxioAdvancedBilling.Models.Customer> ReconcileCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        var customer = await ReadCustomerAsync(reference, cancellationToken);
        return customer ?? throw new MaxioServiceException(503, "Maxio did not confirm the customer after an interrupted request.");
    }

    private async Task<MaxioAdvancedBilling.Models.Customer?> ReadCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> exception) when (exception.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> exception)
        {
            throw TranslateRawError(exception.Error, "Unable to read the Maxio customer.");
        }
        catch (Exception exception) when (IsProviderBoundaryException(exception))
        {
            throw TranslateTransportError(exception, "Unable to read the Maxio customer.");
        }
    }

    private async Task<MaxioAdvancedBilling.Models.Subscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(reference, ct: cancellationToken);
            return response.Subscription;
        }
        catch (SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError> exception)
        {
            if (exception.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (exception.Error.TryGetRawError(out var rawError))
            {
                throw TranslateRawError(rawError, "Unable to read the Maxio subscription.");
            }

            throw new MaxioServiceException(502, "Maxio returned an unrecognized subscription error.");
        }
        catch (Exception exception) when (IsProviderBoundaryException(exception))
        {
            throw TranslateTransportError(exception, "Unable to read the Maxio subscription.");
        }
    }

    private static SubscriptionPlanDto MapPlan(MaxioAdvancedBilling.Models.Product product) => new()
    {
        Name = product.Name,
        Handle = product.Handle,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit?.Value,
        ProductPricePointName = product.ProductPricePointName,
        ProductPricePointHandle = product.ProductPricePointHandle,
        RequestCreditCard = product.RequestCreditCard,
        RequireCreditCard = product.RequireCreditCard
    };

    private static SubscriptionDto MapSubscription(MaxioAdvancedBilling.Models.Subscription subscription)
    {
        var state = subscription.State?.Value;
        return new SubscriptionDto
        {
            Id = subscription.Id,
            Reference = subscription.Reference,
            State = state,
            Plan = subscription.Product is null ? null : MapPlan(subscription.Product),
            ProductPriceInCents = subscription.ProductPriceInCents,
            CurrentBillingAmountInCents = subscription.CurrentBillingAmountInCents,
            NextBillingDate = subscription.NextAssessmentAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            Currency = subscription.Currency,
            IsCurrent = !string.Equals(state, "canceled", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(state, "expired", StringComparison.OrdinalIgnoreCase)
        };
    }

    private static string GetIdentity(ClaimsPrincipal principal)
    {
        var identity = principal.FindFirstValue(ClaimTypes.NameIdentifier) ??
                       principal.FindFirstValue(ClaimTypes.Name) ??
                       principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(identity))
        {
            throw new MaxioServiceException(401, "A stable authenticated identity is required.");
        }

        return identity.Trim().ToLowerInvariant();
    }

    private static (string FirstName, string LastName, string Email) GetCustomerDetails(ClaimsPrincipal principal, string identity)
    {
        var email = principal.FindFirstValue(ClaimTypes.Email) ?? identity;
        try
        {
            var address = new System.Net.Mail.MailAddress(email);
            email = address.Address;
        }
        catch (FormatException)
        {
            throw new MaxioServiceException(400, "The authenticated identity must contain a valid email address.");
        }

        var localPart = email[..email.IndexOf('@')];
        var pieces = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var firstName = pieces.Length > 0 ? Capitalize(pieces[0]) : "Shopper";
        var lastName = pieces.Length > 1 ? Capitalize(pieces[^1]) : "Customer";
        return (firstName, lastName, email);
    }

    private static string Capitalize(string value) => value.Length == 0
        ? "Customer"
        : char.ToUpperInvariant(value[0]) + value[1..];

    private static string CreateReference(string kind, string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"eshop-{kind}-{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static bool IsProviderBoundaryException(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or JsonException;

    private static MaxioServiceException TranslateTransportError(Exception exception, string message) =>
        new(exception is TaskCanceledException ? 504 : 502, message);

    private static MaxioServiceException TranslateRawError(RawError error, string message) =>
        new((int)error.StatusCode is >= 400 and <= 599 ? (int)error.StatusCode : 502, message);

    private static MaxioServiceException TranslateTypedError<T>(T error, string message)
        where T : MaxioAdvancedBilling.Core.ErrorResponse.ApiError
    {
        if (error is MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError plansError)
        {
            if (plansError.TryGetString(out _))
            {
                return new MaxioServiceException(404, "The configured Maxio product family was not found.");
            }

            if (plansError.TryGetRawError(out var raw))
            {
                return TranslateRawError(raw, message);
            }
        }

        if (error is MaxioAdvancedBilling.Errors.CreateCustomerError customerError)
        {
            if (customerError.TryGetCustomerErrorResponse1(out _))
            {
                return new MaxioServiceException(422, message);
            }

            if (customerError.TryGetRawError(out var raw))
            {
                return TranslateRawError(raw, message);
            }
        }

        if (error is MaxioAdvancedBilling.Errors.CreateSubscriptionError subscriptionError)
        {
            if (subscriptionError.TryGetErrorListResponse1(out var errorList))
            {
                var detail = errorList.Errors is { Count: > 0 }
                    ? string.Join("; ", errorList.Errors)
                    : null;
                return new MaxioServiceException(
                    422,
                    detail is null ? message : $"{message} Provider validation: {detail}");
            }

            if (subscriptionError.TryGetRawError(out var raw))
            {
                return TranslateRawError(raw, message);
            }
        }

        if (error is MaxioAdvancedBilling.Errors.FindSubscriptionError findError &&
            findError.TryGetRawError(out var findRaw))
        {
            return TranslateRawError(findRaw, message);
        }

        return new MaxioServiceException(502, message);
    }
}
