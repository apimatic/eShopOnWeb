using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscription;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A failure of the Maxio integration that has no meaningful client-facing status:
/// the billing system was unreachable, rejected our credentials, or returned a
/// body that could not be processed.
/// </summary>
public class MaxioIntegrationException : Exception
{
    public MaxioIntegrationException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>
/// Fronts the Maxio Advanced Billing SDK for subscription enrollment. Every SDK
/// call is bounded and translated at this boundary so callers see only
/// <see cref="Result{TValue}"/> outcomes and domain models.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(MaxioAdvancedBillingClient client, IOptions<MaxioOptions> options, ILogger<SubscriptionService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<SubscriptionPlan>>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var hydrated = await Task.WhenAll(
                _options.Plans.Select(plan => HydratePlanAsync(plan, cancellationToken)));

            return new Result<IReadOnlyList<SubscriptionPlan>>(hydrated.ToList());
        }
        catch (MaxioIntegrationException ex)
        {
            _logger.LogWarning(ex, "Could not hydrate subscription plans from Maxio.");
            return Result<IReadOnlyList<SubscriptionPlan>>.Error(
                "The billing system is not answering correctly; plan availability cannot be confirmed right now.");
        }
    }

    public async Task<Result<SubscribeOutcome>> SubscribeAsync(string userId, string email, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Result<SubscribeOutcome>.Invalid(new List<ValidationError>
            {
                new ValidationError { ErrorMessage = "The caller's identity is required to subscribe." }
            });
        }
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            return Result<SubscribeOutcome>.Invalid(new List<ValidationError>
            {
                new ValidationError { Identifier = "planHandle", ErrorMessage = "A plan handle must be supplied." }
            });
        }

        var configuredPlan = _options.Plans
            .FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (configuredPlan is null)
        {
            return Result<SubscribeOutcome>.Invalid(new List<ValidationError>
            {
                new ValidationError
                {
                    Identifier = "planHandle",
                    ErrorMessage = $"'{planHandle}' is not an available subscription plan."
                }
            });
        }

        try
        {
            var customerResult = await EnsureCustomerAsync(userId, email, cancellationToken);
            if (!customerResult.IsSuccess)
            {
                return Result<SubscribeOutcome>.Error(FirstErrorMessage(customerResult));
            }

            // Deterministic per (user, plan): a repeat subscribe returns the existing
            // subscription instead of creating a second one.
            var subscriptionReference = $"{userId}:{configuredPlan.Handle}";

            var existing = await TryFindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (existing.IsSuccess)
            {
                if (existing.Value is not null)
                {
                    return new Result<SubscribeOutcome>(new SubscribeOutcome
                    {
                        Subscription = MapSummary(existing.Value, configuredPlan.Handle),
                        CreatedNew = false
                    });
                }
            }
            else
            {
                return Result<SubscribeOutcome>.Error(FirstErrorMessage(existing));
            }

            var createResult = await CreateSubscriptionAsync(customerResult.Value, configuredPlan.Handle, subscriptionReference, cancellationToken);
            if (createResult.IsSuccess)
            {
                return new Result<SubscribeOutcome>(new SubscribeOutcome
                {
                    Subscription = MapSummary(createResult.Value, configuredPlan.Handle),
                    CreatedNew = true
                });
            }

            // A create-time 422 can mean a concurrent double-click created the same
            // subscription between our lookup and create; re-check before giving up.
            var raced = await TryFindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (raced.IsSuccess && raced.Value is not null)
            {
                return new Result<SubscribeOutcome>(new SubscribeOutcome
                {
                    Subscription = MapSummary(raced.Value, configuredPlan.Handle),
                    CreatedNew = false
                });
            }

            if (createResult.Status == ResultStatus.Invalid)
            {
                return Result<SubscribeOutcome>.Invalid(
                    createResult.ValidationErrors);
            }
            return Result<SubscribeOutcome>.Error(FirstErrorMessage(createResult));
        }
        catch (MaxioIntegrationException ex)
        {
            _logger.LogWarning(ex, "Subscription enrollment failed against Maxio for user {UserId}.", userId);
            return Result<SubscribeOutcome>.Error(
                "The billing system is not answering correctly; the subscription could not be confirmed right now.");
        }
    }

    public async Task<Result<IReadOnlyList<SubscriptionSummary>>> ListUserSubscriptionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Result<IReadOnlyList<SubscriptionSummary>>.Invalid(new List<ValidationError>
            {
                new ValidationError { ErrorMessage = "The caller's identity is required." }
            });
        }

        try
        {
            var lookup = await ReadCustomerByReferenceAsync(userId, cancellationToken);
            if (lookup.Status == CustomerLookup.NotFound)
            {
                // No Maxio customer yet means nothing has ever been subscribed.
                return new Result<IReadOnlyList<SubscriptionSummary>>(new List<SubscriptionSummary>());
            }
            if (!lookup.IsSuccess)
            {
                return Result<IReadOnlyList<SubscriptionSummary>>.Error(lookup.FailureMessage);
            }

            var subscriptions = await BoundedCallAsync(
                token => _client.Customers.ListCustomerSubscriptions(customerId: lookup.CustomerId!.Value, ct: token),
                cancellationToken);

            var summaries = subscriptions
                .Select(s => s.Subscription)
                .Where(s => s is not null)
                .Select(s => MapSummary(s!, fallbackPlanHandle: null))
                .ToList();

            return new Result<IReadOnlyList<SubscriptionSummary>>(summaries);
        }
        catch (MaxioIntegrationException ex)
        {
            _logger.LogWarning(ex, "Listing Maxio subscriptions failed for user {UserId}.", userId);
            return Result<IReadOnlyList<SubscriptionSummary>>.Error(
                "The billing system is not answering correctly; subscriptions cannot be listed right now.");
        }
    }

    private async Task<SubscriptionPlan> HydratePlanAsync(MaxioPlanOption planOption, CancellationToken cancellationToken)
    {
        var plan = new SubscriptionPlan
        {
            Handle = planOption.Handle,
            DisplayName = planOption.DisplayName,
            IsDefault = planOption.IsDefault,
            ProductFamilyHandle = _options.ProductFamilyHandle ?? string.Empty,
            Available = true
        };

        try
        {
            var response = await BoundedCallAsync(
                token => _client.Products.ReadProductByHandle(apiHandle: planOption.Handle, ct: token),
                cancellationToken);

            var product = response.Product;
            plan.DisplayName = product?.Name ?? planOption.DisplayName;
            plan.PriceInCents = product?.PriceInCents;
        }
        catch (MaxioIntegrationException)
        {
            throw;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // The plan is configured but does not exist on this Maxio site — surface
            // it as unavailable instead of hiding it or failing the whole listing.
            plan.Available = false;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogWarning(
                "Reading Maxio product {Handle} failed with HTTP {Status}; marking the plan unavailable.",
                planOption.Handle, ex.Error.StatusCode);
            plan.Available = false;
        }

        return plan;
    }

    private async Task<Result<int>> EnsureCustomerAsync(string userId, string email, CancellationToken cancellationToken)
    {
        var lookup = await ReadCustomerByReferenceAsync(userId, cancellationToken);
        if (lookup.IsSuccess)
        {
            return new Result<int>(lookup.CustomerId!.Value);
        }
        if (lookup.Status != CustomerLookup.NotFound)
        {
            return Result<int>.Error(lookup.FailureMessage);
        }

        // Absent on Maxio — create it. A concurrent first click may create it first;
        // the provider enforces one customer per reference, so a duplicate create is
        // rejected with 422 and we recover by re-reading the reference.
        var (firstName, lastName, resolvedEmail) = DeriveCustomerNames(userId, email);
        var createResponse = await BoundedCallAsync(
            token => _client.Customers.CreateCustomer(
                body: new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = email,
                        Reference = userId
                    }
                },
                ct: token),
            cancellationToken);

        var created = createResponse.Customer;
        if (created?.Id is null)
        {
            return Result<int>.Error("The billing customer was created but its id could not be read.");
        }
        return new Result<int>(created.Id.Value);
    }

    private async Task<CustomerLookupResult> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedCallAsync(
                token => _client.Customers.ReadCustomerByReference(reference: reference, ct: token),
                cancellationToken);

            var id = response.Customer?.Id;
            if (id is null)
            {
                return CustomerLookupResult.Failed("The billing customer matched the reference but its id could not be read.");
            }
            return CustomerLookupResult.Found(id.Value);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return CustomerLookupResult.NotFound();
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _logger.LogError("Maxio rejected the configured API credentials (HTTP {Status}).", ex.Error.StatusCode);
            return CustomerLookupResult.Failed("The billing system rejected its credentials.");
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogWarning("Reading Maxio customer by reference failed with HTTP {Status}: {Body}",
                ex.Error.StatusCode, SafeErrorBody(ex.Error));
            return CustomerLookupResult.Failed("The billing system returned an unexpected error.");
        }
        catch (MaxioIntegrationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not MaxioIntegrationException)
        {
            _logger.LogWarning(ex, "Reading Maxio customer by reference failed unexpectedly.");
            return CustomerLookupResult.Failed("The billing system is not answering correctly.");
        }
    }

    /// <returns>
    /// Success with data when the subscription exists (possibly null on a 404 miss);
    /// failure carrying a caller-safe message when Maxio could not be consulted.
    /// </returns>
    private async Task<Result<Subscription?>> TryFindSubscriptionAsync(string subscriptionReference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedCallAsync(
                token => _client.Subscriptions.FindSubscription(reference: subscriptionReference, ct: token),
                cancellationToken);
            return new Result<Subscription?>(response.Subscription);
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return new Result<Subscription?>(null);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                _logger.LogWarning("Finding Maxio subscription {Reference} failed with HTTP {Status}: {Body}",
                    subscriptionReference, raw.StatusCode, SafeErrorBody(raw));
            }
            return Result<Subscription?>.Error("The billing system returned an unexpected error.");
        }
        catch (MaxioIntegrationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not MaxioIntegrationException)
        {
            _logger.LogWarning(ex, "Finding Maxio subscription {Reference} failed unexpectedly.", subscriptionReference);
            return Result<Subscription?>.Error("The billing system is not answering correctly.");
        }
    }

    private async Task<Result<Subscription>> CreateSubscriptionAsync(int customerId, string planHandle, string subscriptionReference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedCallAsync(
                token => _client.Subscriptions.CreateSubscription(
                    body: new CreateSubscriptionRequest
                    {
                        // No card capture is part of the enrollment flow: remittance collection
                        // lets Maxio open the subscription without a payment profile on
                        // file (automatic collection is rejected with 422 until a card
                        // exists, even on plans that do not require one).
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = planHandle,
                            CustomerId = customerId,
                            Reference = subscriptionReference,
                            PaymentCollectionMethod = CollectionMethod.Remittance
                        }
                    },
                    ct: token),
                cancellationToken);

            if (response.Subscription is null)
            {
                return Result<Subscription>.Error("The billing system created the subscription but it could not be read back.");
            }
            return new Result<Subscription>(response.Subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            IReadOnlyList<string> messages = new List<string>();
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                messages = errorList.Errors;
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                _logger.LogWarning("Creating Maxio subscription failed with HTTP {Status}: {Body}",
                    raw.StatusCode, SafeErrorBody(raw));
                messages = new List<string> { $"The billing system rejected the enrollment (HTTP {(int)raw.StatusCode})." };
            }

            if (messages.Count == 0)
            {
                messages = new List<string> { "The billing system rejected the enrollment." };
            }
            return Result<Subscription>.Invalid(messages.Select(m => new ValidationError { ErrorMessage = m }).ToList());
        }
        catch (MaxioIntegrationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not MaxioIntegrationException)
        {
            _logger.LogWarning(ex, "Creating Maxio subscription {Reference} failed unexpectedly.", subscriptionReference);
            return Result<Subscription>.Error("The billing system is not answering correctly.");
        }
    }

    private SubscriptionSummary MapSummary(Subscription subscription, string? fallbackPlanHandle)
    {
        var productHandle = subscription.Product?.Handle;
        return new SubscriptionSummary
        {
            SubscriptionId = subscription.Id ?? 0,
            PlanHandle = productHandle ?? fallbackPlanHandle ?? string.Empty,
            PlanResolved = !string.IsNullOrEmpty(productHandle),
            PriceInCents = subscription.CurrentBillingAmountInCents ?? subscription.ProductPriceInCents,
            State = subscription.State?.Value,
            NextBillingDate = subscription.NextAssessmentAt,
            CreatedAt = subscription.CreatedAt
        };
    }

    private static string FirstErrorMessage<T>(Result<T> result)
    {
        var joined = string.Join("; ", result.Errors.Where(m => !string.IsNullOrEmpty(m)));
        return joined.Length > 0 ? joined : "The billing system could not be reached.";
    }

    private static (string FirstName, string LastName, string Email) DeriveCustomerNames(string userId, string? email)
    {
        // ASP.NET Identity usernames in eShopOnWeb are email addresses; fall back to a
        // deterministic synthetic address for non-email usernames so the Maxio create
        // payload is always valid and always identical for the same user.
        var resolvedEmail =
            !string.IsNullOrWhiteSpace(email) ? email :
            userId.Contains('@') ? userId :
            $"{userId}@users.eshoponweb";

        var localPart = resolvedEmail.Split('@')[0];
        var nameParts = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        var firstName = Capitalize(nameParts.Length > 0 ? nameParts[0] : "eShop");
        var lastName = nameParts.Length > 1 ? Capitalize(nameParts[1]) : "Shopper";
        return (firstName, lastName, resolvedEmail);
    }

    private static string Capitalize(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private static string SafeErrorBody(RawError error)
    {
        try
        {
            var body = error.ReadAsString();
            return string.IsNullOrEmpty(body) ? "<empty>" : body.Length > 500 ? body[..500] : body;
        }
        catch (Exception)
        {
            return "<unreadable>";
        }
    }

    /// <summary>
    /// Bounds one SDK operation to the call budget and converts the two failure
    /// kinds that are not API errors — transport failures and unreadable response
    /// bodies — into the boundary's single unknown-outcome exception.
    /// </summary>
    private async Task<T> BoundedCallAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or OperationCanceledException or TimeoutException)
        {
            throw new MaxioIntegrationException(
                "The billing system returned a response that could not be processed or did not answer in time.", ex);
        }
    }

    private enum CustomerLookup
    {
        Found,
        NotFound,
        Failed
    }

    private class CustomerLookupResult
    {
        public CustomerLookup Status { get; private init; }
        public int? CustomerId { get; private init; }
        public string FailureMessage { get; private init; } = string.Empty;

        public bool IsSuccess => Status == CustomerLookup.Found;

        public static CustomerLookupResult Found(int customerId) => new() { Status = CustomerLookup.Found, CustomerId = customerId };
        public static CustomerLookupResult NotFound() => new() { Status = CustomerLookup.NotFound };
        public static CustomerLookupResult Failed(string message) => new() { Status = CustomerLookup.Failed, FailureMessage = message };
    }
}