using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio Advanced Billing implementation of <see cref="ISubscriptionBillingService"/>.
/// Maxio is the billing system of record: customers are keyed on the eShopOnWeb user
/// id (customer reference) and subscriptions on a deterministic per-user-per-plan
/// reference, so repeated calls never create duplicates.
/// </summary>
public class MaxioBillingService : ISubscriptionBillingService
{
    private const int PlanPageSize = 50;
    private const int MaxPlanPages = 20;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingService> _logger;
    private readonly MaxioAdvancedBillingClient? _client;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _subscriptionLocks = new();

    public MaxioBillingService(HttpClient httpClient, IOptions<MaxioOptions> options,
        ILogger<MaxioBillingService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = _options.IsConfigured ? CreateClient(httpClient, _options) : null;
    }

    public async Task<IReadOnlyList<SubscriptionPlanInfo>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var client = Client();
        try
        {
            var family = await FindProductFamilyAsync(client, cancellationToken);
            var familyId = family.Id?.ToString()
                ?? throw new MaxioBillingException(MaxioBillingErrorKind.Unparseable,
                    "The billing provider returned a product family without an id.");

            var defaultHandle = _options.DefaultPlanHandle;
            var plans = new List<SubscriptionPlanInfo>();
            for (var page = 1; page <= MaxPlanPages; page++)
            {
                var products = await Bounded(token => client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyId,
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: null,
                    include: null,
                    page: page,
                    perPage: PlanPageSize,
                    ct: token), cancellationToken);

                plans.AddRange(products.Select(p => MapPlan(p.Product, defaultHandle)));
                if (products.Count < PlanPageSize) break;
            }

            return plans;
        }
        catch (MaxioBillingException)
        {
            throw;
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var notFound))
                throw new MaxioBillingException(MaxioBillingErrorKind.Rejected,
                    "The subscription catalog could not be found at the billing provider.", 404, ex);
            if (ex.Error.TryGetRawError(out var raw))
                throw MapRaw(raw, "listing subscription plans", ex);
            throw new MaxioBillingException(MaxioBillingErrorKind.Unknown,
                "The billing provider returned an unexpected error while listing subscription plans.", null, ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRaw(ex.Error, "listing subscription plans", ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioBillingException(MaxioBillingErrorKind.Unparseable,
                "The billing provider returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioBillingException(MaxioBillingErrorKind.Unreachable,
                "The billing provider could not be reached.", null, ex);
        }
    }

    public async Task<SubscribeResult> SubscribeAsync(string userId, string email, string userName,
        string? planHandle, CancellationToken cancellationToken)
    {
        var client = Client();
        try
        {
            var handle = string.IsNullOrWhiteSpace(planHandle) ? _options.DefaultPlanHandle : planHandle;
            if (string.IsNullOrWhiteSpace(handle))
                throw new MaxioBillingException(MaxioBillingErrorKind.Rejected,
                    "No plan was specified and no default plan is configured.", 400);

            await GetProductAsync(client, handle, cancellationToken);

            var reference = SubscriptionReference(userId, handle);
            var gate = _subscriptionLocks.GetOrAdd(reference, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            try
            {
                var customerId = await EnsureCustomerAsync(client, userId, email, userName, cancellationToken);

                var existing = await FindSubscriptionOrNullAsync(client, reference, cancellationToken);
                if (existing != null)
                {
                    return new SubscribeResult
                    {
                        Subscription = MapSubscription(existing),
                        Created = false,
                        BillingCustomerId = customerId
                    };
                }

                return await CreateSubscriptionAsync(client, customerId, handle, reference, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }
        catch (MaxioBillingException)
        {
            throw;
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRaw(ex.Error, "subscribing", ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioBillingException(MaxioBillingErrorKind.Unparseable,
                "The billing provider returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioBillingException(MaxioBillingErrorKind.Unreachable,
                "The billing provider could not be reached.", null, ex);
        }
    }

    public async Task<IReadOnlyList<SubscriptionInfo>> GetUserSubscriptionsAsync(string userId,
        CancellationToken cancellationToken)
    {
        var client = Client();
        try
        {
            var customerId = await TryGetCustomerIdAsync(client, userId, cancellationToken);
            if (customerId is null)
            {
                return Array.Empty<SubscriptionInfo>();
            }

            var subscriptions = await Bounded(token => client.Customers.ListCustomerSubscriptions(
                customerId.Value, token), cancellationToken);

            return subscriptions
                .Where(s => s.Subscription != null)
                .Select(s => MapSubscription(s.Subscription!))
                .OrderByDescending(s => s.IsActive)
                .ThenByDescending(s => s.SubscriptionId)
                .ToList();
        }
        catch (MaxioBillingException)
        {
            throw;
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRaw(ex.Error, "listing subscriptions", ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioBillingException(MaxioBillingErrorKind.Unparseable,
                "The billing provider returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioBillingException(MaxioBillingErrorKind.Unreachable,
                "The billing provider could not be reached.", null, ex);
        }
    }

    private static MaxioAdvancedBillingClient CreateClient(HttpClient httpClient, MaxioOptions options)
    {
        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials
            {
                Username = options.ApiKey!,
                Password = "x"
            }
        };
        clientOptions.Server.Production.Us.Site = options.Subdomain;
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            clientOptions.Server.Production.Us.BaseUrl = options.BaseUrl;
        }

        clientOptions.Retry = RetryOptions.Default() with
        {
            MaxRetries = 2,
            Timeout = TimeSpan.FromSeconds(15)
        };

        return new MaxioAdvancedBillingClient(httpClient, clientOptions);
    }

    private MaxioAdvancedBillingClient Client()
    {
        if (_client is null)
        {
            throw new MaxioBillingException(MaxioBillingErrorKind.Unreachable,
                "Subscription billing is not configured. Set the Maxio: configuration section (ApiKey and Subdomain).");
        }

        return _client;
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private async Task<ProductFamily> FindProductFamilyAsync(MaxioAdvancedBillingClient client,
        CancellationToken cancellationToken)
    {
        var handle = _options.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new MaxioBillingException(MaxioBillingErrorKind.Unknown,
                "No product family is configured for subscriptions (Maxio:ProductFamilyHandle).");
        }

        var families = await Bounded(token => client.ProductFamilies.ListProductFamilies(
            dateField: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null,
            ct: token), cancellationToken);

        var family = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(pf => pf != null && pf.Handle == handle);

        if (family is null)
        {
            throw new MaxioBillingException(MaxioBillingErrorKind.Rejected,
                $"No subscription catalog is configured at the billing provider (product family '{handle}' was not found).",
                404);
        }

        return family;
    }

    private async Task<Product> GetProductAsync(MaxioAdvancedBillingClient client, string handle,
        CancellationToken cancellationToken)
    {
        var response = await Bounded(token => client.Products.ReadProductByHandle(apiHandle: handle, ct: token),
            cancellationToken);

        var product = response.Product
            ?? throw new MaxioBillingException(MaxioBillingErrorKind.Unparseable,
                "The billing provider returned a plan without content.");

        var familyHandle = product.ProductFamily?.Handle;
        if (!string.IsNullOrWhiteSpace(_options.ProductFamilyHandle) &&
            !string.IsNullOrWhiteSpace(familyHandle) &&
            familyHandle != _options.ProductFamilyHandle)
        {
            throw new MaxioBillingException(MaxioBillingErrorKind.Rejected,
                $"Plan '{handle}' is not part of the subscription catalog.", 404);
        }

        return product;
    }

    private async Task<int?> TryGetCustomerIdAsync(MaxioAdvancedBillingClient client, string reference,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(token => client.Customers.ReadCustomerByReference(
                reference: reference, ct: token), cancellationToken);
            return response.Customer?.Id;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<int> EnsureCustomerAsync(MaxioAdvancedBillingClient client, string userId, string email,
        string userName, CancellationToken cancellationToken)
    {
        var existingId = await TryGetCustomerIdAsync(client, userId, cancellationToken);
        if (existingId.HasValue)
        {
            return existingId.Value;
        }

        var (firstName, lastName) = SplitName(userName, email);
        try
        {
            var response = await Bounded(token => client.Customers.CreateCustomer(
                new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = email,
                        Reference = userId
                    }
                }, token), cancellationToken);

            return response.Customer?.Id
                ?? throw new MaxioBillingException(MaxioBillingErrorKind.Unparseable,
                    "The billing provider returned a customer without content.");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // A 422 can mean a concurrent create won the race — reconcile by reference.
            var reconciled = await TryGetCustomerIdAsync(client, userId, cancellationToken);
            if (reconciled.HasValue)
            {
                _logger.LogInformation(
                    "Maxio customer for user {UserId} was created concurrently; using existing customer {CustomerId}.",
                    userId, reconciled.Value);
                return reconciled.Value;
            }

            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                throw new MaxioBillingException(MaxioBillingErrorKind.Rejected,
                    "The billing provider rejected creating the billing customer.", 422, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRaw(raw, "creating the billing customer", ex);
            }

            throw new MaxioBillingException(MaxioBillingErrorKind.Unknown,
                "The billing provider returned an unexpected error while creating the billing customer.", null, ex);
        }
    }

    private async Task<Subscription?> FindSubscriptionOrNullAsync(MaxioAdvancedBillingClient client,
        string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(token => client.Subscriptions.FindSubscription(
                reference: reference, ct: token), cancellationToken);
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
                throw MapRaw(raw, "looking up the subscription", ex);
            }

            throw new MaxioBillingException(MaxioBillingErrorKind.Unknown,
                "The billing provider returned an unexpected error while looking up the subscription.", null, ex);
        }
    }

    private async Task<SubscribeResult> CreateSubscriptionAsync(MaxioAdvancedBillingClient client, int customerId,
        string planHandle, string reference, CancellationToken cancellationToken)
    {
        try
        {
            var subscription = await AttemptCreateSubscriptionAsync(client, customerId, planHandle, reference,
                CollectionMethod.Remittance, cancellationToken);

            _logger.LogInformation("Created Maxio subscription {SubscriptionId} for reference {Reference}.",
                subscription.Id, reference);

            return new SubscribeResult
            {
                Subscription = MapSubscription(subscription),
                Created = true,
                BillingCustomerId = customerId
            };
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            // The create may have reached the provider even though we saw a failure:
            // reconcile by reference before reporting.
            var reconciled = await FindSubscriptionOrNullAsync(client, reference, cancellationToken);
            if (reconciled != null)
            {
                _logger.LogInformation(
                    "Maxio subscription for reference {Reference} was created concurrently; returning it.",
                    reference);
                return new SubscribeResult
                {
                    Subscription = MapSubscription(reconciled),
                    Created = true,
                    BillingCustomerId = customerId
                };
            }

            // "Remittance" is the manual-collection method of the Relationship Invoicing
            // architecture; on a legacy Statements-architecture site the manual method is
            // "invoice". Live sandbox evidence showed signup rejecting without an explicit
            // collection method, so fall back to the legacy equivalent when rejected.
            if (IsCollectionMethodRejection(ex))
            {
                try
                {
                    var subscription = await AttemptCreateSubscriptionAsync(client, customerId, planHandle,
                        reference, CollectionMethod.Invoice, cancellationToken);

                    _logger.LogInformation(
                        "Created Maxio subscription {SubscriptionId} for reference {Reference} using the legacy invoice collection method.",
                        subscription.Id, reference);

                    return new SubscribeResult
                    {
                        Subscription = MapSubscription(subscription),
                        Created = true,
                        BillingCustomerId = customerId
                    };
                }
                catch (SdkException<CreateSubscriptionError> invoiceEx)
                {
                    var reconciledAfterRetry = await FindSubscriptionOrNullAsync(client, reference, cancellationToken);
                    if (reconciledAfterRetry != null)
                    {
                        return new SubscribeResult
                        {
                            Subscription = MapSubscription(reconciledAfterRetry),
                            Created = true,
                            BillingCustomerId = customerId
                        };
                    }

                    throw MapCreateSubscriptionError(invoiceEx);
                }
            }

            throw MapCreateSubscriptionError(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Transport failure on a write: the create may have gone through — reconcile.
            var reconciled = await FindSubscriptionOrNullAsync(client, reference, cancellationToken);
            if (reconciled != null)
            {
                _logger.LogInformation(
                    "Maxio subscription for reference {Reference} survived a transport failure; returning it.",
                    reference);
                return new SubscribeResult
                {
                    Subscription = MapSubscription(reconciled),
                    Created = true,
                    BillingCustomerId = customerId
                };
            }

            throw new MaxioBillingException(MaxioBillingErrorKind.Unreachable,
                "The billing provider could not be reached while creating the subscription.", null, ex);
        }
    }

    private async Task<Subscription> AttemptCreateSubscriptionAsync(MaxioAdvancedBillingClient client,
        int customerId, string planHandle, string reference, CollectionMethod collectionMethod,
        CancellationToken cancellationToken)
    {
        var response = await Bounded(token => client.Subscriptions.CreateSubscription(
            new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = planHandle,
                    CustomerId = customerId,
                    Reference = reference,
                    PaymentCollectionMethod = collectionMethod
                }
            }, token), cancellationToken);

        return response.Subscription
            ?? throw new MaxioBillingException(MaxioBillingErrorKind.Unparseable,
                "The billing provider returned a subscription without content.");
    }

    private static bool IsCollectionMethodRejection(SdkException<CreateSubscriptionError> ex)
    {
        if (!ex.Error.TryGetErrorListResponse1(out var errorList) || errorList.Errors is not { Count: > 0 })
        {
            return false;
        }

        var joined = string.Join(" ", errorList.Errors);
        return joined.Contains("payment method", StringComparison.OrdinalIgnoreCase) ||
               joined.Contains("collection", StringComparison.OrdinalIgnoreCase) ||
               joined.Contains("balance", StringComparison.OrdinalIgnoreCase);
    }

    private MaxioBillingException MapCreateSubscriptionError(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var errorList) && errorList.Errors is { Count: > 0 })
        {
            return new MaxioBillingException(MaxioBillingErrorKind.Rejected,
                string.Join("; ", errorList.Errors), 422, ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw(raw, "creating the subscription", ex);
        }

        return new MaxioBillingException(MaxioBillingErrorKind.Unknown,
            "The billing provider returned an unexpected error while creating the subscription.", null, ex);
    }

    private static SubscriptionInfo MapSubscription(Subscription subscription)
    {
        return new SubscriptionInfo
        {
            SubscriptionId = subscription.Id ?? 0,
            Reference = subscription.Reference ?? string.Empty,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? string.Empty,
            PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0,
            Interval = subscription.Product?.Interval ?? 0,
            IntervalUnit = subscription.Product?.IntervalUnit?.Value,
            State = subscription.State?.Value ?? string.Empty,
            IsActive = subscription.State == SubscriptionState.Active ||
                       subscription.State == SubscriptionState.Trialing,
            NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
        };
    }

    private static SubscriptionPlanInfo MapPlan(Product? product, string? defaultHandle)
    {
        return new SubscriptionPlanInfo
        {
            Handle = product?.Handle ?? string.Empty,
            Name = product?.Name ?? string.Empty,
            PriceInCents = product?.PriceInCents ?? 0,
            Interval = product?.Interval ?? 0,
            IntervalUnit = product?.IntervalUnit?.Value,
            IsDefault = !string.IsNullOrEmpty(product?.Handle) && product!.Handle == defaultHandle
        };
    }

    private static string SubscriptionReference(string userId, string planHandle) => $"{userId}:{planHandle}";

    private static (string FirstName, string LastName) SplitName(string userName, string email)
    {
        var source = string.IsNullOrWhiteSpace(userName) ? email ?? string.Empty : userName;
        var local = source.Contains('@') ? source[..source.IndexOf('@')] : source;
        var parts = local.Split(new[] { '.', '_', '-', ' ' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var first = parts.Length > 0 ? Capitalize(parts[0]) : "eShop";
        var last = parts.Length > 1 ? Capitalize(parts[^1]) : "Customer";
        return (first, last);
    }

    private static string Capitalize(string value) =>
        char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();

    private MaxioBillingException MapRaw(RawError raw, string action, Exception? innerException = null)
    {
        var status = (int)raw.StatusCode;
        var body = SafeBody(raw);
        _logger.LogWarning("Maxio call failed while {Action}: HTTP {Status} {Body}", action, status, body);

        var kind = status >= 400 && status < 500
            ? MaxioBillingErrorKind.Rejected
            : MaxioBillingErrorKind.Unknown;
        var message = status switch
        {
            401 or 403 => "The billing provider rejected the configured credentials.",
            _ => $"The billing provider returned HTTP {status} while {action}."
        };
        return new MaxioBillingException(kind, message, status, innerException);
    }

    private static string SafeBody(RawError raw)
    {
        try
        {
            var body = raw.ReadAsString();
            if (string.IsNullOrEmpty(body)) return "<empty>";
            return body.Length <= 500 ? body : body[..500];
        }
        catch
        {
            return "<unreadable>";
        }
    }
}
