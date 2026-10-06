using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Maxio Advanced Billing-backed implementation. Maxio records are keyed by
/// deterministic references derived from the eShopOnWeb username, which makes
/// find-or-create and enrollment idempotent without a local store:
///   customer reference:     "eshop-user:{username}"
///   subscription reference: "eshop-sub:{username}:{planHandle}"
/// </summary>
public sealed class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private static readonly TimeSpan PerCallBudget = TimeSpan.FromSeconds(30);
    private const int PlansPageSize = 100;
    private const int MaxPlanPages = 25;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly string _productFamilyHandle;
    private readonly ILogger<MaxioSubscriptionService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _enrollmentLocks = new();

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, MaxioSettings settings, ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _productFamilyHandle = settings.ProductFamilyHandle;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct = default)
    {
        return await BoundedAsync(async token =>
        {
            var plans = new List<SubscriptionPlanDto>();
            for (var page = 1; page <= MaxPlanPages; page++)
            {
                IReadOnlyList<ProductResponse> products;
                try
                {
                    products = await _client.Products.ListProducts(
                        dateField: null,
                        filter: null,
                        endDate: null,
                        endDatetime: null,
                        startDate: null,
                        startDatetime: null,
                        includeArchived: null,
                        include: null,
                        page: page,
                        perPage: PlansPageSize,
                        ct: token);
                }
                catch (SdkException<RawError> ex)
                {
                    throw MaxioApiException.FromProviderError((int)ex.Error.StatusCode, RawDetail(ex.Error));
                }
                catch (JsonException)
                {
                    throw new MaxioApiException(502, "The billing provider returned a response that could not be processed.");
                }

                foreach (var response in products)
                {
                    var product = response.Product;
                    if (product is null || product.ArchivedAt is not null) continue;
                    if (!string.Equals(product.ProductFamily?.Handle, _productFamilyHandle, StringComparison.OrdinalIgnoreCase)) continue;
                    plans.Add(MapPlan(product));
                }

                if (products.Count < PlansPageSize) break;
            }

            return plans;
        }, ct);
    }

    public async Task<SubscriptionDto> SubscribeAsync(string username, string planHandle, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username)) throw new MaxioApiException(400, "An authenticated user is required.");
        if (string.IsNullOrWhiteSpace(planHandle)) throw new MaxioApiException(400, "A plan handle is required.");
        username = username.Trim();
        planHandle = planHandle.Trim();

        var gate = _enrollmentLocks.GetOrAdd($"{username}|{planHandle}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            return await BoundedAsync(async token =>
            {
                var customer = await EnsureCustomerAsync(username, token);
                var subscriptionReference = SubscriptionReference(username, planHandle);

                var existing = await FindByReferenceOrNullAsync(subscriptionReference, token);
                if (existing is not null)
                {
                    return MapSubscription(existing, alreadySubscribed: true);
                }

                var product = await ReadProductByHandleOrNullAsync(planHandle, token);
                if (product is null)
                {
                    throw new MaxioApiException(404, $"Unknown subscription plan '{planHandle}'.");
                }

                var duplicate = await FindActiveByProductHandleAsync(customer.Id!.Value, planHandle, token);
                if (duplicate is not null)
                {
                    return MapSubscription(duplicate, alreadySubscribed: true);
                }

                var createBody = new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = planHandle,
                        CustomerId = customer.Id,
                        Reference = subscriptionReference,
                        PaymentCollectionMethod = CollectionMethod.Remittance
                    }
                };

                SubscriptionResponse created;
                try
                {
                    created = await _client.Subscriptions.CreateSubscription(createBody, token);
                }
                catch (SdkException<CreateSubscriptionError> ex)
                {
                    throw DescribeCreateSubscriptionError(ex);
                }
                catch (JsonException)
                {
                    return await ReconcileAfterAmbiguousCreateAsync(customer.Id!.Value, planHandle, subscriptionReference, token);
                }
                catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !token.IsCancellationRequested))
                {
                    return await ReconcileAfterAmbiguousCreateAsync(customer.Id!.Value, planHandle, subscriptionReference, token);
                }

                if (created.Subscription is null)
                {
                    return await ReconcileAfterAmbiguousCreateAsync(customer.Id!.Value, planHandle, subscriptionReference, token);
                }

                return MapSubscription(created, alreadySubscribed: false);
            }, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListForUserAsync(string username, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username)) throw new MaxioApiException(400, "An authenticated user is required.");
        username = username.Trim();

        return await BoundedAsync<IReadOnlyList<SubscriptionDto>>(async token =>
        {
            Customer customer;
            try
            {
                var response = await _client.Customers.ReadCustomerByReference(CustomerReference(username), token);
                customer = response.Customer ?? throw new MaxioApiException(502, "The billing provider returned an unreadable customer.");
            }
            catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
            {
                return Array.Empty<SubscriptionDto>();
            }
            catch (SdkException<RawError> ex)
            {
                throw MaxioApiException.FromProviderError((int)ex.Error.StatusCode, RawDetail(ex.Error));
            }
            catch (JsonException)
            {
                throw new MaxioApiException(502, "The billing provider returned a response that could not be processed.");
            }

            IReadOnlyList<SubscriptionResponse> subscriptions;
            try
            {
                subscriptions = await _client.Customers.ListCustomerSubscriptions(customer.Id!.Value, token);
            }
            catch (SdkException<RawError> ex)
            {
                throw MaxioApiException.FromProviderError((int)ex.Error.StatusCode, RawDetail(ex.Error));
            }
            catch (JsonException)
            {
                throw new MaxioApiException(502, "The billing provider returned a response that could not be processed.");
            }

            return subscriptions
                .Where(s => s.Subscription is not null)
                .Select(s => MapSubscription(s, alreadySubscribed: false))
                .OrderByDescending(s => s.CreatedAtUtc)
                .ToList();
        }, ct);
    }

    private async Task<Customer> EnsureCustomerAsync(string username, CancellationToken token)
    {
        var reference = CustomerReference(username);
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, token);
            return response.Customer ?? throw new MaxioApiException(502, "The billing provider returned an unreadable customer.");
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            var (firstName, lastName) = SplitDisplayName(username);
            var createBody = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = username,
                    Reference = reference
                }
            };
            try
            {
                var created = await _client.Customers.CreateCustomer(createBody, token);
                return created.Customer ?? throw new MaxioApiException(502, "The billing provider returned an unreadable customer.");
            }
            catch (SdkException<CreateCustomerError> cex)
            {
                throw DescribeCreateCustomerError(cex);
            }
            catch (JsonException)
            {
                throw new MaxioApiException(502, "The billing provider returned a response that could not be processed.");
            }
        }
        catch (SdkException<RawError> ex)
        {
            throw MaxioApiException.FromProviderError((int)ex.Error.StatusCode, RawDetail(ex.Error));
        }
        catch (JsonException)
        {
            throw new MaxioApiException(502, "The billing provider returned a response that could not be processed.");
        }
    }

    private async Task<SubscriptionResponse?> FindByReferenceOrNullAsync(string reference, CancellationToken token)
    {
        try
        {
            return await _client.Subscriptions.FindSubscription(reference, token);
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _)) return null;
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MaxioApiException.FromProviderError((int)raw.StatusCode, RawDetail(raw));
            }
            throw new MaxioApiException(502, "The billing provider returned an unexpected error.");
        }
        catch (JsonException)
        {
            throw new MaxioApiException(502, "The billing provider returned a response that could not be processed.");
        }
    }

    private async Task<Product?> ReadProductByHandleOrNullAsync(string handle, CancellationToken token)
    {
        try
        {
            var response = await _client.Products.ReadProductByHandle(handle, token);
            return response.Product;
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw MaxioApiException.FromProviderError((int)ex.Error.StatusCode, RawDetail(ex.Error));
        }
        catch (JsonException)
        {
            throw new MaxioApiException(502, "The billing provider returned a response that could not be processed.");
        }
    }

    private async Task<SubscriptionResponse?> FindActiveByProductHandleAsync(int customerId, string planHandle, CancellationToken token)
    {
        IReadOnlyList<SubscriptionResponse> subscriptions;
        try
        {
            subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId, token);
        }
        catch (SdkException<RawError> ex)
        {
            throw MaxioApiException.FromProviderError((int)ex.Error.StatusCode, RawDetail(ex.Error));
        }
        catch (JsonException)
        {
            throw new MaxioApiException(502, "The billing provider returned a response that could not be processed.");
        }

        return subscriptions.FirstOrDefault(s =>
            s.Subscription is not null &&
            !IsTerminal(s.Subscription.State?.Value) &&
            string.Equals(s.Subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<SubscriptionDto> ReconcileAfterAmbiguousCreateAsync(int customerId, string planHandle, string subscriptionReference, CancellationToken token)
    {
        var found = await FindByReferenceOrNullAsync(subscriptionReference, token);
        if (found?.Subscription is not null)
        {
            var subscription = found.Subscription;
            if (!string.Equals(subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            {
                await TryCancelAsync(subscription.Id, token);
                throw new MaxioApiException(502, "The enrollment attempt created an unexpected record at the billing provider and was rolled back. Please retry.");
            }
            _logger.LogWarning("Enrollment response from the billing provider was unreadable; reconciled to subscription {SubscriptionId}.", subscription.Id);
            return MapSubscription(found, alreadySubscribed: true);
        }

        var unreferenced = await FindActiveByProductHandleAsync(customerId, planHandle, token);
        if (unreferenced is not null)
        {
            _logger.LogWarning("Enrollment response from the billing provider was unreadable; found an existing subscription by product scan.");
            return MapSubscription(unreferenced, alreadySubscribed: true);
        }

        throw new MaxioApiException(502, "The enrollment outcome at the billing provider is unknown; no subscription was confirmed. Please retry.");
    }

    private async Task TryCancelAsync(int? subscriptionId, CancellationToken token)
    {
        if (subscriptionId is not int id) return;
        try
        {
            var body = new CancellationRequest
            {
                Subscription = new CancellationOptions
                {
                    CancellationMessage = "eShopOnWeb enrollment rollback"
                }
            };
            await _client.SubscriptionStatus.CancelSubscription(id, body, token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to roll back subscription {SubscriptionId} after an ambiguous enrollment.", id);
        }
    }

    private async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken requestAborted)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        cts.CancelAfter(PerCallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (OperationCanceledException) when (!requestAborted.IsCancellationRequested)
        {
            throw new MaxioApiException(504, "The billing provider did not respond in time.");
        }
        catch (HttpRequestException)
        {
            throw new MaxioApiException(502, "The billing provider could not be reached.");
        }
    }

    private static MaxioApiException DescribeCreateSubscriptionError(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var validationErrors))
        {
            var messages = string.Join("; ", validationErrors.Errors ?? Array.Empty<string>());
            return MaxioApiException.FromProviderError(422, messages);
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return MaxioApiException.FromProviderError((int)raw.StatusCode, RawDetail(raw));
        }
        return new MaxioApiException(502, "The billing provider returned an unexpected error.");
    }

    private static MaxioApiException DescribeCreateCustomerError(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out var errorResponse))
        {
            return MaxioApiException.FromProviderError(422, errorResponse.Errors?.ToString());
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return MaxioApiException.FromProviderError((int)raw.StatusCode, RawDetail(raw));
        }
        return new MaxioApiException(502, "The billing provider returned an unexpected error.");
    }

    private static string? RawDetail(RawError raw)
    {
        try
        {
            return raw.ReadAsString();
        }
        catch
        {
            return null;
        }
    }

    private static bool IsTerminal(string? state) =>
        state is "canceled" or "expired";

    private static SubscriptionPlanDto MapPlan(Product product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        PriceInCents = product.PriceInCents ?? 0,
        Interval = product.Interval ?? 0,
        IntervalUnit = product.IntervalUnit?.Value ?? string.Empty
    };

    private static SubscriptionDto MapSubscription(SubscriptionResponse response, bool alreadySubscribed)
    {
        var subscription = response.Subscription
            ?? throw new MaxioApiException(502, "The billing provider returned an unreadable subscription.");
        return new SubscriptionDto
        {
            SubscriptionId = subscription.Id ?? 0,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? string.Empty,
            PriceInCents = subscription.ProductPriceInCents ?? 0,
            Currency = subscription.Currency ?? string.Empty,
            State = subscription.State?.Value ?? string.Empty,
            NextBillingAtUtc = subscription.NextAssessmentAt,
            CreatedAtUtc = subscription.CreatedAt,
            AlreadySubscribed = alreadySubscribed
        };
    }

    private static string CustomerReference(string username) => $"eshop-user:{username.ToLowerInvariant()}";

    private static string SubscriptionReference(string username, string planHandle) =>
        $"eshop-sub:{username.ToLowerInvariant()}:{planHandle.ToLowerInvariant()}";

    private static (string FirstName, string LastName) SplitDisplayName(string username)
    {
        var localPart = username.Contains('@') ? username[..username.IndexOf('@')] : username;
        var segments = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var firstName = segments.Length > 0 ? Capitalize(segments[0]) : "eShop";
        var lastName = segments.Length > 1 ? string.Join(" ", segments[1..].Select(Capitalize)) : "Shopper";
        return (firstName, lastName);
    }

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
}