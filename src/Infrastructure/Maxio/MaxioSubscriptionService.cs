using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private static readonly HashSet<string> TerminalSubscriptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create"
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _maxioOptions;

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, IOptions<MaxioOptions> maxioOptions)
    {
        _client = client;
        _maxioOptions = maxioOptions.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanModel>> ListPlansAsync(CancellationToken ct)
    {
        using var budget = LinkBudget(ct);

        try
        {
            var products = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: $"handle:{_maxioOptions.ProductFamilyHandle}",
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: null,
                include: null,
                page: 1,
                perPage: 100,
                ct: budget.Token);

            return products
                .Where(item => item.Product.ArchivedAt is null)
                .Select(item => MapPlan(item.Product))
                .ToList();
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var message))
            {
                throw new MaxioIntegrationException(
                    $"Maxio product family '{_maxioOptions.ProductFamilyHandle}' was not found: {message}");
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                throw new MaxioIntegrationException(
                    $"Failed to list subscription plans (HTTP {(int)raw.StatusCode}): {raw.ReadAsString()}");
            }

            throw new MaxioIntegrationException("Failed to list subscription plans due to an unexpected billing provider error.");
        }
        catch (JsonException ex)
        {
            throw new MaxioIntegrationException("The billing provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioIntegrationException("The billing provider is currently unavailable.", ex);
        }
    }

    public async Task<CustomerSubscriptionModel> SubscribeAsync(
        string customerReference,
        string customerEmail,
        string customerFirstName,
        string customerLastName,
        string planHandle,
        CancellationToken ct)
    {
        var plans = await ListPlansAsync(ct);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new MaxioValidationException($"Plan '{planHandle}' was not found.");
        }

        if (plan.RequiresPaymentMethod)
        {
            throw new MaxioValidationException(
                $"Plan '{planHandle}' requires a payment method, which subscribing through this API does not currently support.");
        }

        var customer = await FindOrCreateCustomerAsync(customerReference, customerEmail, customerFirstName, customerLastName, ct);
        var customerId = customer.Id
            ?? throw new MaxioIntegrationException("Maxio customer was created but no id was returned.");

        var existingSubscriptions = await ListSubscriptionsForCustomerIdAsync(customerId, ct);
        var alreadySubscribed = existingSubscriptions.Any(s =>
            string.Equals(s.PlanHandle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            !TerminalSubscriptionStates.Contains(s.State));
        if (alreadySubscribed)
        {
            throw new DuplicateException($"Customer '{customerReference}' already has an active subscription to plan '{planHandle}'.");
        }

        using var budget = LinkBudget(ct);

        try
        {
            var created = await _client.Subscriptions.CreateSubscription(
                body: new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = planHandle,
                        CustomerReference = customerReference,
                        // These plans are configured with no payment method required (see task spec),
                        // but Maxio's default "automatic" collection still tries to charge a card for
                        // the first invoice immediately on create. "Remittance" defers collection to an
                        // out-of-band invoice instead, which is what actually lets creation succeed with
                        // no payment profile on file (confirmed against the sandbox).
                        PaymentCollectionMethod = CollectionMethod.Remittance
                    }
                },
                ct: budget.Token);

            var subscription = created.Subscription
                ?? throw new MaxioIntegrationException("Maxio subscription was created but no subscription data was returned.");

            return MapSubscription(subscription, plan);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                throw new MaxioValidationException(errorList.Errors);
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                throw new MaxioIntegrationException($"Unable to create subscription (HTTP {(int)raw.StatusCode}): {raw.ReadAsString()}");
            }

            throw new MaxioIntegrationException("Unable to create subscription due to an unexpected billing provider error.");
        }
        catch (JsonException ex)
        {
            throw new MaxioIntegrationException("The billing provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioIntegrationException("The billing provider is currently unavailable.", ex);
        }
    }

    public async Task<IReadOnlyList<CustomerSubscriptionModel>> ListSubscriptionsForCustomerAsync(string customerReference, CancellationToken ct)
    {
        var customer = await TryReadCustomerByReferenceAsync(customerReference, ct);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscriptionModel>();
        }

        var customerId = customer.Id
            ?? throw new MaxioIntegrationException("Maxio customer record is missing its id.");

        return await ListSubscriptionsForCustomerIdAsync(customerId, ct);
    }

    private async Task<Customer> FindOrCreateCustomerAsync(
        string reference, string email, string firstName, string lastName, CancellationToken ct)
    {
        var existing = await TryReadCustomerByReferenceAsync(reference, ct);
        if (existing is not null)
        {
            return existing;
        }

        using var budget = LinkBudget(ct);

        try
        {
            var created = await _client.Customers.CreateCustomer(
                body: new MaxioAdvancedBilling.Models.CreateCustomerRequest
                {
                    Customer = new MaxioAdvancedBilling.Models.CreateCustomer
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = email,
                        Reference = reference
                    }
                },
                ct: budget.Token);

            return created.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // CustomerErrorResponse1's typed payload can't carry a real validation reason (its
            // Errors fields are unrelated to customer validation) — any create-customer failure
            // here is treated as a possible create/create race and resolved by re-reading the
            // customer (a harmless GET), per dotnet-error-handling's reconciliation pattern,
            // before surfacing it as a real error.
            var reLookup = await TryReadCustomerByReferenceAsync(reference, ct);
            if (reLookup is not null)
            {
                return reLookup;
            }

            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                // Not a race (re-lookup above found nothing) — the 422 reflects a real rejection
                // of the submitted details (e.g. invalid email); surface it as caller-actionable.
                throw new MaxioValidationException($"The billing provider rejected the customer details for '{email}'.");
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                throw new MaxioIntegrationException($"Unable to create Maxio customer (HTTP {(int)raw.StatusCode}): {raw.ReadAsString()}");
            }

            throw new MaxioIntegrationException("Unable to create Maxio customer due to an unexpected billing provider error.");
        }
        catch (JsonException ex)
        {
            throw new MaxioIntegrationException("The billing provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioIntegrationException("The billing provider is currently unavailable.", ex);
        }
    }

    private async Task<Customer?> TryReadCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        using var budget = LinkBudget(ct);

        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference: reference, ct: budget.Token);
            return response.Customer;
        }
        catch (SdkException<RawError> ex)
        {
            // Maxio's "no customer for this reference" status is undocumented in the SDK —
            // only 404 is treated as "not found"; every other status is a real error.
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            throw new MaxioIntegrationException($"Failed to look up Maxio customer (HTTP {(int)ex.Error.StatusCode}): {ex.Error.ReadAsString()}");
        }
        catch (JsonException ex)
        {
            throw new MaxioIntegrationException("The billing provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioIntegrationException("The billing provider is currently unavailable.", ex);
        }
    }

    private async Task<IReadOnlyList<CustomerSubscriptionModel>> ListSubscriptionsForCustomerIdAsync(int customerId, CancellationToken ct)
    {
        using var budget = LinkBudget(ct);

        try
        {
            var response = await _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: budget.Token);
            return response
                .Where(item => item.Subscription is not null)
                .Select(item => MapSubscription(item.Subscription!, plan: null))
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw new MaxioIntegrationException($"Failed to list subscriptions (HTTP {(int)ex.Error.StatusCode}): {ex.Error.ReadAsString()}");
        }
        catch (JsonException ex)
        {
            throw new MaxioIntegrationException("The billing provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioIntegrationException("The billing provider is currently unavailable.", ex);
        }
    }

    private static SubscriptionPlanModel MapPlan(Product product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        PriceInCents = product.PriceInCents ?? 0,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit?.Value,
        // RequestCreditCard is Maxio's soft prompt (shown on Maxio-hosted pages, not enforced via
        // the API); only RequireCreditCard actually blocks creating a subscription with no
        // payment profile, so only that flag gates the check in SubscribeAsync.
        RequiresPaymentMethod = product.RequireCreditCard ?? false
    };

    private static CustomerSubscriptionModel MapSubscription(Subscription subscription, SubscriptionPlanModel? plan)
    {
        var priceInCents = subscription.ProductPriceInCents
            ?? subscription.CurrentBillingAmountInCents
            ?? plan?.PriceInCents
            ?? 0;

        return new CustomerSubscriptionModel
        {
            Id = subscription.Id ?? 0,
            State = subscription.State?.Value ?? string.Empty,
            PlanHandle = subscription.Product?.Handle ?? plan?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? plan?.Name ?? string.Empty,
            PriceInCents = priceInCents,
            NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt
        };
    }

    private static CancellationTokenSource LinkBudget(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return cts;
    }
}
