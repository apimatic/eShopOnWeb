using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Servers;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioModels = MaxioAdvancedBilling.Models;
using MaxioEnums = MaxioAdvancedBilling.Models.Enums;
using MaxioUnions = MaxioAdvancedBilling.Models.AnyOf;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The single Infrastructure class that talks to Maxio Advanced Billing, behind
/// <see cref="IBillingClient"/>. Every provider-side failure (typed API error or connectivity
/// failure) is translated into <see cref="BillingProviderException"/> so ApplicationCore only ever
/// sees one failure shape (maxio-plan.md §3).
///
/// The outbound base URL is resolved here, not from the typed <see cref="HttpClient"/>'s
/// BaseAddress: the SDK routes requests from <see cref="MaxioAdvancedBillingClientOptions.Server"/>,
/// so an explicit <see cref="MaxioSettings.BaseUrl"/> override is built into that options object
/// (winning verbatim over the <see cref="MaxioSettings.Subdomain"/>-derived host) rather than onto
/// the HttpClient (maxio-plan.md §2 — the critical correction to plan.md §4.3's original snippet).
/// </summary>
public class MaxioBillingClient : IBillingClient
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioSettings> options)
    {
        _settings = options.Value;

        var isEu = string.Equals(_settings.Environment, "EU", StringComparison.OrdinalIgnoreCase);
        var productionOptions = new ProductionOptions();
        if (isEu)
        {
            if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
            {
                productionOptions.Eu.BaseUrl = _settings.BaseUrl;
            }
            else
            {
                productionOptions.Eu.Site = _settings.Subdomain;
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
            {
                productionOptions.Us.BaseUrl = _settings.BaseUrl;
            }
            else
            {
                productionOptions.Us.Site = _settings.Subdomain;
            }
        }

        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = _settings.ApiKey, Password = "x" },
            Environment = isEu ? ServerEnvironment.Eu : ServerEnvironment.Us,
            Server = new ServerOptions { Production = productionOptions },
        };

        _client = new MaxioAdvancedBillingClient(httpClient, clientOptions);
    }

    public async Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var products = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: _settings.ProductFamilyId.ToString(),
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: false,
                include: null,
                page: 1,
                perPage: 20,
                ct: cancellationToken);

            return products.Where(p => p.Product is not null).Select(p => MapPlan(p.Product!)).ToList();
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var message))
            {
                throw new BillingProviderException($"Failed to list plans for product family {_settings.ProductFamilyId}: {message}", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException($"Failed to list plans for product family {_settings.ProductFamilyId}: {raw.ReadAsString()}", ex);
            }
            throw new BillingProviderException($"Failed to list plans for product family {_settings.ProductFamilyId}.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new BillingProviderException("Failed to reach the billing provider while listing plans.", ex);
        }
    }

    public async Task<BillingPlan> GetPlanAsync(string productHandle, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Products.ReadProductByHandle(apiHandle: productHandle, ct: cancellationToken);
            if (response.Product is null)
            {
                throw new BillingProviderException($"Plan '{productHandle}' was not found.");
            }
            return MapPlan(response.Product);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex, $"read plan '{productHandle}'");
        }
        catch (HttpRequestException ex)
        {
            throw new BillingProviderException($"Failed to reach the billing provider while reading plan '{productHandle}'.", ex);
        }
    }

    public async Task EnsureMeteredComponentIsValidAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Components.FindComponent(handle: _settings.MeteredComponentHandle, ct: cancellationToken);
            var component = response.Component;
            if (component is null || component.Kind != MaxioEnums.ComponentKind.MeteredComponent)
            {
                throw new BillingProviderException(
                    $"Configured metered component '{_settings.MeteredComponentHandle}' does not resolve to a component of Metered kind (actual: {component?.Kind?.Value ?? "not found"}).");
            }
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex, $"validate metered component '{_settings.MeteredComponentHandle}'");
        }
        catch (HttpRequestException ex)
        {
            throw new BillingProviderException($"Failed to reach the billing provider while validating the metered component '{_settings.MeteredComponentHandle}'.", ex);
        }
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference: reference, ct: cancellationToken);
            return response.Customer is null ? null : MapCustomer(response.Customer);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex, $"look up customer '{reference}'");
        }
        catch (HttpRequestException ex)
        {
            throw new BillingProviderException($"Failed to reach the billing provider while looking up customer '{reference}'.", ex);
        }
    }

    public async Task<BillingCustomer> EnsureCustomerAsync(string reference, string email, CancellationToken cancellationToken = default)
    {
        var existing = await FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveCustomerName(reference, email);

        try
        {
            var response = await _client.Customers.CreateCustomer(
                new MaxioModels.CreateCustomerRequest
                {
                    Customer = new MaxioModels.CreateCustomer
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = email,
                        Reference = reference,
                    },
                },
                ct: cancellationToken);

            if (response.Customer is null)
            {
                throw new BillingProviderException($"Customer '{reference}' was not returned after creation.");
            }
            return MapCustomer(response.Customer);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                // Reference must be unique; a 422 here most likely means a concurrent request already
                // created this customer between our read and this write. Re-read to stay idempotent.
                var afterRace = await FindCustomerByReferenceAsync(reference, cancellationToken);
                if (afterRace is not null)
                {
                    return afterRace;
                }
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException($"Failed to create customer '{reference}': {raw.ReadAsString()}", ex);
            }

            throw new BillingProviderException($"Failed to create customer '{reference}'.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new BillingProviderException($"Failed to reach the billing provider while creating customer '{reference}'.", ex);
        }
    }

    public async Task<Subscription?> FindActiveSubscriptionAsync(int customerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: cancellationToken);
            var active = subscriptions
                .Select(s => s.Subscription)
                .FirstOrDefault(s => s is not null &&
                    (s.State == MaxioEnums.SubscriptionState.Active || s.State == MaxioEnums.SubscriptionState.Trialing));

            return active is null ? null : MapSubscription(active);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex, $"list subscriptions for customer {customerId}");
        }
        catch (HttpRequestException ex)
        {
            throw new BillingProviderException($"Failed to reach the billing provider while listing subscriptions for customer {customerId}.", ex);
        }
    }

    public async Task<Subscription?> FindLatestSubscriptionAsync(int customerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: cancellationToken);
            var latest = subscriptions
                .Select(s => s.Subscription)
                .Where(s => s is not null)
                .OrderByDescending(s => s!.Id)
                .FirstOrDefault();

            return latest is null ? null : MapSubscription(latest);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex, $"list subscriptions for customer {customerId}");
        }
        catch (HttpRequestException ex)
        {
            throw new BillingProviderException($"Failed to reach the billing provider while listing subscriptions for customer {customerId}.", ex);
        }
    }

    // The demo products are seeded with RequireCreditCard=false, but Maxio still defaults an
    // uncollected subscription to CollectionMethod.Automatic (auto-charge a card), which fails with
    // no card on file. A non-automatic collection method is required for a truly card-free demo
    // subscription; Remittance is the modern (Relationship Invoicing Architecture) choice, Invoice
    // the legacy (Statements Architecture) one — try Remittance first, fall back to Invoice only if
    // the site rejects it (maxio-plan.md Capability 3 addendum).
    private static readonly MaxioEnums.CollectionMethod[] CollectionMethodsToTry =
    {
        MaxioEnums.CollectionMethod.Remittance,
        MaxioEnums.CollectionMethod.Invoice,
    };

    public async Task<Subscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken = default)
    {
        var action = $"create subscription for customer {customerId} on plan '{productHandle}'";

        for (var i = 0; i < CollectionMethodsToTry.Length; i++)
        {
            try
            {
                var response = await _client.Subscriptions.CreateSubscription(
                    new MaxioModels.CreateSubscriptionRequest
                    {
                        Subscription = new MaxioModels.CreateSubscription
                        {
                            ProductHandle = productHandle,
                            CustomerId = customerId,
                            PaymentCollectionMethod = CollectionMethodsToTry[i],
                        },
                    },
                    ct: cancellationToken);

                return RequireSubscription(response.Subscription, action);
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                var isLastAttempt = i == CollectionMethodsToTry.Length - 1;
                if (!isLastAttempt && ex.Error.TryGetErrorListResponse1(out var rejection) &&
                    rejection.Errors.Any(e => e.Contains("collection method", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (ex.Error.TryGetErrorListResponse1(out var errors))
                {
                    throw new BillingProviderException($"Failed to {action}: {FormatErrors(errors)}", ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw new BillingProviderException($"Failed to {action}: {raw.ReadAsString()}", ex);
                }
                throw new BillingProviderException($"Failed to {action}.", ex);
            }
            catch (HttpRequestException ex)
            {
                throw new BillingProviderException($"Failed to reach the billing provider while creating a subscription for customer {customerId}.", ex);
            }
        }

        throw new BillingProviderException($"Failed to {action}.");
    }

    public async Task<Subscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Subscriptions.ReadSubscription(subscriptionId: subscriptionId, include: null, ct: cancellationToken);
            if (response.Subscription is null)
            {
                throw new SubscriptionNotFoundException($"Subscription {subscriptionId} was not found.");
            }
            return MapSubscription(response.Subscription);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex, $"read subscription {subscriptionId}");
        }
        catch (HttpRequestException ex)
        {
            throw new BillingProviderException($"Failed to reach the billing provider while reading subscription {subscriptionId}.", ex);
        }
    }

    public async Task<BillingUsageBalance> RecordUsageAsync(int subscriptionId, int quantity, string? memo, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.SubscriptionComponents.CreateUsage(
                subscriptionIdOrReference: MaxioUnions.SubscriptionIdOrReference.Int(subscriptionId),
                componentId: MaxioUnions.ComponentIdModel.Int(_settings.MeteredComponentId),
                body: new MaxioModels.CreateUsageRequest { Usage = new MaxioModels.CreateUsage { Quantity = quantity, Memo = memo } },
                ct: cancellationToken);
        }
        catch (SdkException<CreateUsageError> ex)
        {
            var action = $"record usage on subscription {subscriptionId}";
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw new BillingProviderException($"Failed to {action}: {FormatErrors(errors)}", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException($"Failed to {action}: {raw.ReadAsString()}", ex);
            }
            throw new BillingProviderException($"Failed to {action}.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new BillingProviderException($"Failed to reach the billing provider while recording usage on subscription {subscriptionId}.", ex);
        }

        int? periodToDateBalance = null;
        try
        {
            var componentResponse = await _client.SubscriptionComponents.ReadSubscriptionComponent(
                subscriptionId: subscriptionId,
                componentId: _settings.MeteredComponentId,
                ct: cancellationToken);
            periodToDateBalance = componentResponse.Component?.UnitBalance;
        }
        catch (Exception)
        {
            // Usage was already recorded successfully; report the balance as "unavailable" rather
            // than failing the whole operation (UC2 failure scenario: read-back after a successful record).
        }

        return new BillingUsageBalance(subscriptionId, quantity, periodToDateBalance);
    }

    public async Task<BillingProrationPreview> PreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionProducts.PreviewSubscriptionProductMigration(
                subscriptionId,
                new MaxioModels.SubscriptionMigrationPreviewRequest
                {
                    Migration = new MaxioModels.SubscriptionMigrationPreviewOptions { ProductHandle = targetProductHandle },
                },
                ct: cancellationToken);

            var preview = response.Migration;
            return new BillingProrationPreview(
                TargetProductHandle: targetProductHandle,
                AppliesNow: true,
                ProratedAdjustmentInCents: (int)(preview?.ProratedAdjustmentInCents ?? 0),
                ChargeInCents: (int)(preview?.ChargeInCents ?? 0),
                PaymentDueInCents: (int)(preview?.PaymentDueInCents ?? 0),
                CreditAppliedInCents: (int)(preview?.CreditAppliedInCents ?? 0));
        }
        catch (SdkException<PreviewSubscriptionProductMigrationError> ex)
        {
            var action = $"preview plan change for subscription {subscriptionId} to '{targetProductHandle}'";
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw new BillingProviderException($"Failed to {action}: {FormatErrors(errors)}", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException($"Failed to {action}: {raw.ReadAsString()}", ex);
            }
            throw new BillingProviderException($"Failed to {action}.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new BillingProviderException($"Failed to reach the billing provider while previewing a plan change for subscription {subscriptionId}.", ex);
        }
    }

    public async Task<Subscription> MigratePlanNowAsync(int subscriptionId, string targetProductHandle, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionProducts.MigrateSubscriptionProduct(
                subscriptionId,
                new MaxioModels.SubscriptionProductMigrationRequest
                {
                    Migration = new MaxioModels.SubscriptionProductMigration { ProductHandle = targetProductHandle },
                },
                ct: cancellationToken);

            return RequireSubscription(response.Subscription, $"migrate subscription {subscriptionId} to '{targetProductHandle}'");
        }
        catch (SdkException<MigrateSubscriptionProductError> ex)
        {
            var action = $"migrate subscription {subscriptionId} to '{targetProductHandle}'";
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw new BillingProviderException($"Failed to {action}: {FormatErrors(errors)}", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException($"Failed to {action}: {raw.ReadAsString()}", ex);
            }
            throw new BillingProviderException($"Failed to {action}.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new BillingProviderException($"Failed to reach the billing provider while migrating subscription {subscriptionId}.", ex);
        }
    }

    public async Task<Subscription> SchedulePlanChangeAsync(int subscriptionId, string targetProductHandle, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Subscriptions.UpdateSubscription(
                subscriptionId,
                new MaxioModels.UpdateSubscriptionRequest
                {
                    Subscription = new MaxioModels.UpdateSubscription { ProductHandle = targetProductHandle, ProductChangeDelayed = true },
                },
                ct: cancellationToken);

            return RequireSubscription(response.Subscription, $"schedule a plan change for subscription {subscriptionId} to '{targetProductHandle}'");
        }
        catch (SdkException<UpdateSubscriptionError> ex)
        {
            var action = $"schedule a plan change for subscription {subscriptionId} to '{targetProductHandle}'";
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw new BillingProviderException($"Failed to {action}: {FormatErrors(errors)}", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException($"Failed to {action}: {raw.ReadAsString()}", ex);
            }
            throw new BillingProviderException($"Failed to {action}.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new BillingProviderException($"Failed to reach the billing provider while scheduling a plan change for subscription {subscriptionId}.", ex);
        }
    }

    public async Task<Subscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.PauseSubscription(subscriptionId, body: null, ct: cancellationToken);
            return RequireSubscription(response.Subscription, $"pause subscription {subscriptionId}");
        }
        catch (SdkException<PauseSubscriptionError> ex)
        {
            var action = $"pause subscription {subscriptionId}";
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw new BillingProviderException($"Failed to {action}: {FormatErrors(errors)}", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException($"Failed to {action}: {raw.ReadAsString()}", ex);
            }
            throw new BillingProviderException($"Failed to {action}.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new BillingProviderException($"Failed to reach the billing provider while pausing subscription {subscriptionId}.", ex);
        }
    }

    public async Task<Subscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.ResumeSubscription(subscriptionId, calendarBillingResumptionCharge: null, ct: cancellationToken);
            return RequireSubscription(response.Subscription, $"resume subscription {subscriptionId}");
        }
        catch (SdkException<ResumeSubscriptionError> ex)
        {
            var action = $"resume subscription {subscriptionId}";
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw new BillingProviderException($"Failed to {action}: {FormatErrors(errors)}", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException($"Failed to {action}: {raw.ReadAsString()}", ex);
            }
            throw new BillingProviderException($"Failed to {action}.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new BillingProviderException($"Failed to reach the billing provider while resuming subscription {subscriptionId}.", ex);
        }
    }

    public async Task<Subscription> CancelSubscriptionAsync(int subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default)
    {
        var body = new MaxioModels.CancellationRequest
        {
            Subscription = new MaxioModels.CancellationOptions { CancellationMessage = reason },
        };

        if (endOfPeriod)
        {
            try
            {
                await _client.SubscriptionStatus.InitiateDelayedCancellation(subscriptionId, body, ct: cancellationToken);
            }
            catch (SdkException<InitiateDelayedCancellationError> ex)
            {
                var action = $"schedule end-of-period cancellation for subscription {subscriptionId}";
                if (ex.Error.TryGetNoContent(out var notFound))
                {
                    throw new BillingProviderException($"Failed to {action}: {notFound.ReadAsString()}", ex);
                }
                if (ex.Error.TryGetErrorListResponse1(out var errors))
                {
                    throw new BillingProviderException($"Failed to {action}: {FormatErrors(errors)}", ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw new BillingProviderException($"Failed to {action}: {raw.ReadAsString()}", ex);
                }
                throw new BillingProviderException($"Failed to {action}.", ex);
            }
            catch (HttpRequestException ex)
            {
                throw new BillingProviderException($"Failed to reach the billing provider while scheduling end-of-period cancellation for subscription {subscriptionId}.", ex);
            }

            // InitiateDelayedCancellation's response carries only a message, not the subscription — re-read it.
            return await GetSubscriptionAsync(subscriptionId, cancellationToken);
        }

        try
        {
            var response = await _client.SubscriptionStatus.CancelSubscription(subscriptionId, body, ct: cancellationToken);
            return RequireSubscription(response.Subscription, $"cancel subscription {subscriptionId}");
        }
        catch (SdkException<CancelSubscriptionApiError> ex)
        {
            if (ex.Error.TryGetNoContent(out var notFound))
            {
                throw new BillingProviderException($"Failed to cancel subscription {subscriptionId}: {notFound.ReadAsString()}", ex);
            }
            if (ex.Error.TryGetCancelSubscriptionErrorResponse(out var errors))
            {
                throw new BillingProviderException($"Failed to cancel subscription {subscriptionId}: {FormatCancelErrors(errors)}", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException($"Failed to cancel subscription {subscriptionId}: {raw.ReadAsString()}", ex);
            }
            throw new BillingProviderException($"Failed to cancel subscription {subscriptionId}.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new BillingProviderException($"Failed to reach the billing provider while cancelling subscription {subscriptionId}.", ex);
        }
    }

    public async Task<Subscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.ReactivateSubscription(subscriptionId, body: null, ct: cancellationToken);
            return RequireSubscription(response.Subscription, $"reactivate subscription {subscriptionId}");
        }
        catch (SdkException<ReactivateSubscriptionError> ex)
        {
            var action = $"reactivate subscription {subscriptionId}";
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw new BillingProviderException($"Failed to {action}: {FormatErrors(errors)}", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException($"Failed to {action}: {raw.ReadAsString()}", ex);
            }
            throw new BillingProviderException($"Failed to {action}.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new BillingProviderException($"Failed to reach the billing provider while reactivating subscription {subscriptionId}.", ex);
        }
    }

    private static (string FirstName, string LastName) DeriveCustomerName(string reference, string email)
    {
        var source = string.IsNullOrWhiteSpace(email) ? reference : email;
        var atIndex = source.IndexOf('@');
        var firstName = atIndex > 0 ? source[..atIndex] : source;
        return (firstName, "eShopOnWeb");
    }

    private static BillingPlan MapPlan(MaxioModels.Product product) =>
        new(product.Handle ?? string.Empty, product.Name ?? string.Empty, (int)(product.PriceInCents ?? 0), product.IntervalUnit?.Value ?? string.Empty, product.Interval ?? 0);

    private static BillingCustomer MapCustomer(MaxioModels.Customer customer) =>
        new(customer.Id ?? 0, customer.Reference ?? string.Empty, customer.Email ?? string.Empty);

    private static Subscription MapSubscription(MaxioModels.Subscription subscription)
    {
        var product = subscription.Product;
        var customer = subscription.Customer;
        return new Subscription(
            providerSubscriptionId: subscription.Id ?? 0,
            userName: customer?.Reference ?? string.Empty,
            providerCustomerId: customer?.Id ?? 0,
            productHandle: product?.Handle ?? string.Empty,
            productName: product?.Name ?? string.Empty,
            priceInCents: (int)(subscription.ProductPriceInCents ?? product?.PriceInCents ?? 0),
            state: MapState(subscription.State),
            currentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
            nextAssessmentAt: subscription.NextAssessmentAt);
    }

    private static Subscription RequireSubscription(MaxioModels.Subscription? subscription, string action) =>
        subscription is null
            ? throw new BillingProviderException($"Failed to {action}: the billing provider did not return the subscription.")
            : MapSubscription(subscription);

    private static SubscriptionState MapState(MaxioEnums.SubscriptionState? state) => state?.Value switch
    {
        "pending" => SubscriptionState.Pending,
        "awaiting_signup" => SubscriptionState.AwaitingSignup,
        "trialing" => SubscriptionState.Trialing,
        "trial_ended" => SubscriptionState.TrialEnded,
        "assessing" => SubscriptionState.Assessing,
        "active" => SubscriptionState.Active,
        "soft_failure" => SubscriptionState.SoftFailure,
        "past_due" => SubscriptionState.PastDue,
        "suspended" => SubscriptionState.Suspended,
        "canceled" => SubscriptionState.Canceled,
        "expired" => SubscriptionState.Expired,
        "paused" => SubscriptionState.Paused,
        "on_hold" => SubscriptionState.OnHold,
        "unpaid" => SubscriptionState.Unpaid,
        "failed_to_create" => SubscriptionState.FailedToCreate,
        _ => SubscriptionState.Unknown,
    };

    private static BillingProviderException TranslateRawError(SdkException<RawError> ex, string action) =>
        new($"Failed to {action}: HTTP {ex.Error.StatusCode} {ex.Error.ReadAsString()}", ex);

    private static string FormatErrors(MaxioModels.ErrorListResponse1 errors) => string.Join("; ", errors.Errors);

    private static string FormatCancelErrors(MaxioModels.AnyOf.CancelSubscriptionErrorResponse errors)
    {
        if (errors.TryGetErrorListResponse1(out var list))
        {
            return FormatErrors(list);
        }
        if (errors.TryGetSingleErrorResponse1(out var single))
        {
            return single.Error;
        }
        return "unknown error";
    }
}
