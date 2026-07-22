using System;
using System.Collections.Generic;
using System.Globalization;
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
using MaxioAdvancedBilling.Models.AnyOf;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;

// The provider SDK ships its own MeteredComponent (a create-request model). This file is the only
// place both namespaces are in scope, so the domain type is pinned explicitly here.
using MeteredComponent = Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.MeteredComponent;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The single integration point with Maxio Advanced Billing. Nothing else in eShopOnWeb talks to the
/// billing provider: this class translates the provider's SDK types into ApplicationCore types and its
/// errors into <see cref="BillingProviderException"/>, so neither the SDK nor its exceptions ever cross
/// into the domain.
/// </summary>
/// <remarks>
/// Money crosses this boundary in two magnitudes and they are not interchangeable: products,
/// subscriptions and migration previews are denominated in cents, while a component's unit price is a
/// decimal string in dollars. Everything leaves this class in dollars as <see cref="decimal"/>.
/// </remarks>
public class MaxioBillingClient : IBillingClient
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioCatalogCache _catalog;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioBillingClient> _logger;

    public MaxioBillingClient(MaxioAdvancedBillingClient client,
        MaxioCatalogCache catalog,
        MaxioSettings settings,
        IAppLogger<MaxioBillingClient> logger)
    {
        _client = client;
        _catalog = catalog;
        _settings = settings;
        _logger = logger;
    }

    public Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
        => GuardTransportAsync(nameof(ListPlansAsync), async () =>
        {
            var familyId = await ResolveProductFamilyIdAsync(cancellationToken);
            var plans = new List<SubscriptionPlan>();

            const int pageSize = 100;
            var page = 1;
            while (true)
            {
                IReadOnlyList<ProductResponse> batch;
                try
                {
                    batch = await _client.ProductFamilies.ListProductsForProductFamily(
                        familyId.ToString(CultureInfo.InvariantCulture),
                        dateField: null,
                        filter: null,
                        startDate: null,
                        endDate: null,
                        startDatetime: null,
                        endDatetime: null,
                        includeArchived: false,
                        include: null,
                        page: page,
                        perPage: pageSize,
                        ct: cancellationToken);
                }
                catch (SdkException<ListProductsForProductFamilyError> ex)
                {
                    if (ex.Error.TryGetString(out var notFound))
                    {
                        throw new BillingConfigurationException(nameof(ListPlansAsync),
                            $"product family '{_settings.ProductFamilyHandle}' could not be listed: {notFound}");
                    }

                    throw Rejected(nameof(ListPlansAsync), ex.Error.TryGetRawError(out var raw) ? raw : null);
                }

                plans.AddRange(batch
                    .Select(p => MapPlan(p.Product))
                    .Where(p => !p.IsArchived));

                if (batch.Count < pageSize)
                {
                    break;
                }

                page++;
            }

            return (IReadOnlyCollection<SubscriptionPlan>)plans;
        }, cancellationToken);

    public Task<SubscriptionPlan> GetPlanAsync(string planHandle, CancellationToken cancellationToken = default)
        => GuardTransportAsync(nameof(GetPlanAsync), async () =>
        {
            if (string.IsNullOrWhiteSpace(planHandle))
            {
                throw new BillingConfigurationException(nameof(GetPlanAsync), "no plan handle was supplied");
            }

            Product product;
            try
            {
                var response = await _client.Products.ReadProductByHandle(planHandle, ct: cancellationToken);
                product = response.Product;
            }
            catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                throw new BillingConfigurationException(nameof(GetPlanAsync),
                    $"plan handle '{planHandle}' does not resolve on this site. Re-seed the billing catalog or correct the configured handle.");
            }
            catch (SdkException<RawError> ex)
            {
                throw Rejected(nameof(GetPlanAsync), ex.Error);
            }

            var plan = MapPlan(product);

            if (!string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle) &&
                !string.IsNullOrWhiteSpace(plan.ProductFamilyHandle) &&
                !string.Equals(plan.ProductFamilyHandle, _settings.ProductFamilyHandle, StringComparison.Ordinal))
            {
                throw new BillingConfigurationException(nameof(GetPlanAsync),
                    $"plan '{planHandle}' belongs to product family '{plan.ProductFamilyHandle}', not the configured '{_settings.ProductFamilyHandle}'.");
            }

            if (plan.IsArchived)
            {
                throw new BillingConfigurationException(nameof(GetPlanAsync),
                    $"plan '{planHandle}' is archived and cannot be subscribed to.");
            }

            return plan;
        }, cancellationToken);

    public Task<MeteredComponent> GetMeteredComponentAsync(CancellationToken cancellationToken = default)
        => GuardTransportAsync(nameof(GetMeteredComponentAsync),
            () => _catalog.GetMeteredComponentAsync(ResolveMeteredComponentAsync, cancellationToken),
            cancellationToken);

    public Task<BillingCustomer> EnsureCustomerAsync(string reference,
        string firstName,
        string lastName,
        string email,
        CancellationToken cancellationToken = default)
        => GuardTransportAsync(nameof(EnsureCustomerAsync), async () =>
        {
            var existing = await FindCustomerByReferenceAsync(reference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            try
            {
                var created = await _client.Customers.CreateCustomer(
                    new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = firstName,
                            LastName = lastName,
                            Email = email,
                            Reference = reference
                        }
                    },
                    ct: cancellationToken);

                return MapCustomer(created.Customer);
            }
            catch (Exception ex) when (ex is SdkException<CreateCustomerError> or JsonException)
            {
                // Another writer may have created the same reference between the lookup and the create.
                // The generated 422 payload cannot carry a duplicate-reference message — and a body it
                // cannot model at all surfaces as a JsonException rather than a typed error — so the
                // control flow is decided by re-running the lookup, never by parsing any text.
                var raced = await FindCustomerByReferenceAsync(reference, cancellationToken);
                if (raced is not null)
                {
                    return raced;
                }

                if (ex is SdkException<CreateCustomerError> typedFailure)
                {
                    if (typedFailure.Error.TryGetCustomerErrorResponse1(out var typed))
                    {
                        throw new BillingProviderException(nameof(EnsureCustomerAsync), DescribeCustomerErrors(typed), 422);
                    }

                    throw Rejected(nameof(EnsureCustomerAsync), typedFailure.Error.TryGetRawError(out var raw) ? raw : null);
                }

                throw new BillingProviderException(nameof(EnsureCustomerAsync),
                    "the customer record was rejected and the provider's reason could not be interpreted", 422, ex);
            }
        }, cancellationToken);

    public Task<CustomerSubscription> CreateSubscriptionAsync(string customerReference,
        string planHandle,
        CancellationToken cancellationToken = default)
        => GuardTransportAsync(nameof(CreateSubscriptionAsync), async () =>
        {
            try
            {
                var response = await _client.Subscriptions.CreateSubscription(
                    new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = planHandle,
                            CustomerReference = customerReference,
                            // No payment profile is captured, so the subscription must be invoiced
                            // rather than auto-collected. Left to the site default it would attempt an
                            // immediate charge and be refused for having no payment method on file.
                            PaymentCollectionMethod = CollectionMethod.Remittance
                        }
                    },
                    ct: cancellationToken);

                return MapSubscription(response.Subscription, nameof(CreateSubscriptionAsync));
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                if (ex.Error.TryGetErrorListResponse1(out var errors))
                {
                    throw new BillingProviderException(nameof(CreateSubscriptionAsync), Describe(errors), 422);
                }

                throw Rejected(nameof(CreateSubscriptionAsync), ex.Error.TryGetRawError(out var raw) ? raw : null);
            }
        }, cancellationToken);

    public Task<IReadOnlyCollection<CustomerSubscription>> ListSubscriptionsAsync(string customerReference,
        CancellationToken cancellationToken = default)
        => GuardTransportAsync(nameof(ListSubscriptionsAsync), async () =>
        {
            var customer = await FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (customer is null)
            {
                // A customer who has never subscribed simply has no subscriptions.
                return (IReadOnlyCollection<CustomerSubscription>)Array.Empty<CustomerSubscription>();
            }

            IReadOnlyList<SubscriptionResponse> subscriptions;
            try
            {
                subscriptions = await _client.Customers.ListCustomerSubscriptions(customer.Id, ct: cancellationToken);
            }
            catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return Array.Empty<CustomerSubscription>();
            }
            catch (SdkException<RawError> ex)
            {
                throw Rejected(nameof(ListSubscriptionsAsync), ex.Error);
            }

            return subscriptions
                .Select(s => s.Subscription)
                .Where(s => s is not null)
                .Select(s => MapSubscription(s!))
                .ToList();
        }, cancellationToken);

    public Task<CustomerSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
        => GuardTransportAsync(nameof(GetSubscriptionAsync), async () =>
        {
            try
            {
                var response = await _client.Subscriptions.ReadSubscription(subscriptionId, include: null, ct: cancellationToken);
                return response.Subscription is null ? null : MapSubscription(response.Subscription);
            }
            catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            catch (SdkException<RawError> ex)
            {
                throw Rejected(nameof(GetSubscriptionAsync), ex.Error);
            }
        }, cancellationToken);

    public Task<UsageReceipt> RecordUsageAsync(int subscriptionId,
        int quantity,
        string? memo,
        CancellationToken cancellationToken = default)
        => GuardTransportAsync(nameof(RecordUsageAsync), async () =>
        {
            var component = await _catalog.GetMeteredComponentAsync(ResolveMeteredComponentAsync, cancellationToken);

            try
            {
                var response = await _client.SubscriptionComponents.CreateUsage(
                    SubscriptionIdOrReference.Int(subscriptionId),
                    ComponentIdModel.Int(component.Id),
                    new CreateUsageRequest
                    {
                        Usage = new CreateUsage
                        {
                            Quantity = quantity,
                            Memo = memo
                        }
                    },
                    ct: cancellationToken);

                var usage = response.Usage;

                return new UsageReceipt(usage.Id ?? 0, ReadQuantity(usage.Quantity) ?? quantity)
                {
                    Memo = usage.Memo,
                    RecordedAt = usage.CreatedAt,
                    ComponentHandle = usage.ComponentHandle ?? component.Handle
                };
            }
            catch (SdkException<CreateUsageError> ex)
            {
                if (ex.Error.TryGetErrorListResponse1(out var errors))
                {
                    throw new BillingProviderException(nameof(RecordUsageAsync), Describe(errors), 422);
                }

                throw Rejected(nameof(RecordUsageAsync), ex.Error.TryGetRawError(out var raw) ? raw : null);
            }
        }, cancellationToken);

    public Task<int?> GetPeriodToDateUsageAsync(int subscriptionId, CancellationToken cancellationToken = default)
        => GuardTransportAsync(nameof(GetPeriodToDateUsageAsync), async () =>
        {
            var component = await _catalog.GetMeteredComponentAsync(ResolveMeteredComponentAsync, cancellationToken);

            try
            {
                var response = await _client.SubscriptionComponents.ReadSubscriptionComponent(
                    subscriptionId, component.Id, ct: cancellationToken);

                return response.Component?.UnitBalance;
            }
            catch (SdkException<ReadSubscriptionComponentError> ex)
            {
                // The component simply carries no balance on this subscription yet.
                if (ex.Error.TryGetNoContent(out _))
                {
                    return null;
                }

                throw Rejected(nameof(GetPeriodToDateUsageAsync), ex.Error.TryGetRawError(out var raw) ? raw : null);
            }
        }, cancellationToken);

    public Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default)
        => GuardTransportAsync(nameof(PreviewPlanChangeAsync), async () =>
        {
            var current = await GetSubscriptionAsync(subscriptionId, cancellationToken)
                ?? throw new SubscriptionNotFoundException(subscriptionId);
            var targetPlan = await GetPlanAsync(targetPlanHandle, cancellationToken);

            var basePreview = new PlanChangePreview(
                subscriptionId,
                current.PlanHandle ?? string.Empty,
                targetPlanHandle,
                timing)
            {
                TargetPlanPrice = targetPlan.Price
            };

            if (timing == PlanChangeTiming.AtNextRenewal)
            {
                // The provider prices an at-renewal change at the next period boundary and exposes no
                // proration preview for that path, so nothing is prorated and nothing is charged now.
                return basePreview with { EffectiveAt = current.NextBillingDate };
            }

            try
            {
                var response = await _client.SubscriptionProducts.PreviewSubscriptionProductMigration(
                    subscriptionId,
                    new SubscriptionMigrationPreviewRequest
                    {
                        Migration = new SubscriptionMigrationPreviewOptions
                        {
                            ProductHandle = targetPlanHandle
                        }
                    },
                    ct: cancellationToken);

                var migration = response.Migration;

                return basePreview with
                {
                    ProratedAdjustment = FromCents(migration.ProratedAdjustmentInCents),
                    Charge = FromCents(migration.ChargeInCents),
                    PaymentDue = FromCents(migration.PaymentDueInCents),
                    CreditApplied = FromCents(migration.CreditAppliedInCents)
                };
            }
            catch (SdkException<PreviewSubscriptionProductMigrationError> ex)
            {
                if (ex.Error.TryGetErrorListResponse1(out var errors))
                {
                    throw new BillingProviderException(nameof(PreviewPlanChangeAsync), Describe(errors), 422);
                }

                throw Rejected(nameof(PreviewPlanChangeAsync), ex.Error.TryGetRawError(out var raw) ? raw : null);
            }
        }, cancellationToken);

    public Task<CustomerSubscription> ChangePlanAsync(int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default)
        => GuardTransportAsync(nameof(ChangePlanAsync), async () =>
        {
            if (timing == PlanChangeTiming.AtNextRenewal)
            {
                try
                {
                    var scheduled = await _client.Subscriptions.UpdateSubscription(
                        subscriptionId,
                        new UpdateSubscriptionRequest
                        {
                            Subscription = new UpdateSubscription
                            {
                                ProductHandle = targetPlanHandle,
                                ProductChangeDelayed = true
                            }
                        },
                        ct: cancellationToken);

                    return MapSubscription(scheduled.Subscription, nameof(ChangePlanAsync));
                }
                catch (SdkException<UpdateSubscriptionError> ex)
                {
                    if (ex.Error.TryGetErrorListResponse1(out var errors))
                    {
                        throw new BillingProviderException(nameof(ChangePlanAsync), Describe(errors), 422);
                    }

                    throw Rejected(nameof(ChangePlanAsync), ex.Error.TryGetRawError(out var raw) ? raw : null);
                }
            }

            try
            {
                var migrated = await _client.SubscriptionProducts.MigrateSubscriptionProduct(
                    subscriptionId,
                    new SubscriptionProductMigrationRequest
                    {
                        Migration = new SubscriptionProductMigration
                        {
                            ProductHandle = targetPlanHandle
                        }
                    },
                    ct: cancellationToken);

                return MapSubscription(migrated.Subscription, nameof(ChangePlanAsync));
            }
            catch (SdkException<MigrateSubscriptionProductError> ex)
            {
                if (ex.Error.TryGetErrorListResponse1(out var errors))
                {
                    throw new BillingProviderException(nameof(ChangePlanAsync), Describe(errors), 422);
                }

                throw Rejected(nameof(ChangePlanAsync), ex.Error.TryGetRawError(out var raw) ? raw : null);
            }
        }, cancellationToken);

    public Task<CustomerSubscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
        => GuardTransportAsync(nameof(PauseSubscriptionAsync), async () =>
        {
            try
            {
                var response = await _client.SubscriptionStatus.PauseSubscription(subscriptionId, body: null, ct: cancellationToken);
                return MapSubscription(response.Subscription, nameof(PauseSubscriptionAsync));
            }
            catch (SdkException<PauseSubscriptionError> ex)
            {
                if (ex.Error.TryGetErrorListResponse1(out var errors))
                {
                    throw new BillingProviderException(nameof(PauseSubscriptionAsync), Describe(errors), 422);
                }

                throw Rejected(nameof(PauseSubscriptionAsync), ex.Error.TryGetRawError(out var raw) ? raw : null);
            }
        }, cancellationToken);

    public Task<CustomerSubscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
        => GuardTransportAsync(nameof(ResumeSubscriptionAsync), async () =>
        {
            try
            {
                var response = await _client.SubscriptionStatus.ResumeSubscription(
                    subscriptionId, calendarBillingResumptionCharge: null, ct: cancellationToken);

                return MapSubscription(response.Subscription, nameof(ResumeSubscriptionAsync));
            }
            catch (SdkException<ResumeSubscriptionError> ex)
            {
                if (ex.Error.TryGetErrorListResponse1(out var errors))
                {
                    throw new BillingProviderException(nameof(ResumeSubscriptionAsync), Describe(errors), 422);
                }

                throw Rejected(nameof(ResumeSubscriptionAsync), ex.Error.TryGetRawError(out var raw) ? raw : null);
            }
        }, cancellationToken);

    public Task<CustomerSubscription> CancelSubscriptionAsync(int subscriptionId,
        CancellationTiming timing,
        string? reason,
        CancellationToken cancellationToken = default)
        => GuardTransportAsync(nameof(CancelSubscriptionAsync), async () =>
        {
            var request = new CancellationRequest
            {
                Subscription = new CancellationOptions
                {
                    CancellationMessage = reason
                }
            };

            if (timing == CancellationTiming.EndOfPeriod)
            {
                try
                {
                    // This endpoint answers with a message only, so the caller's view of the
                    // subscription is refreshed from the provider afterwards.
                    await _client.SubscriptionStatus.InitiateDelayedCancellation(subscriptionId, request, ct: cancellationToken);
                }
                catch (SdkException<InitiateDelayedCancellationError> ex)
                {
                    if (ex.Error.TryGetNoContent(out var missing))
                    {
                        throw Rejected(nameof(CancelSubscriptionAsync), missing);
                    }

                    if (ex.Error.TryGetErrorListResponse1(out var errors))
                    {
                        throw new BillingProviderException(nameof(CancelSubscriptionAsync), Describe(errors), 422);
                    }

                    throw Rejected(nameof(CancelSubscriptionAsync), ex.Error.TryGetRawError(out var raw) ? raw : null);
                }

                return await GetSubscriptionAsync(subscriptionId, cancellationToken)
                    ?? throw new SubscriptionNotFoundException(subscriptionId);
            }

            try
            {
                var response = await _client.SubscriptionStatus.CancelSubscription(subscriptionId, request, ct: cancellationToken);
                return MapSubscription(response.Subscription, nameof(CancelSubscriptionAsync));
            }
            catch (SdkException<CancelSubscriptionApiError> ex)
            {
                if (ex.Error.TryGetNoContent(out var missing))
                {
                    throw Rejected(nameof(CancelSubscriptionAsync), missing);
                }

                if (ex.Error.TryGetCancelSubscriptionErrorResponse(out var typed))
                {
                    throw new BillingProviderException(nameof(CancelSubscriptionAsync), Describe(typed), 422);
                }

                throw Rejected(nameof(CancelSubscriptionAsync), ex.Error.TryGetRawError(out var raw) ? raw : null);
            }
        }, cancellationToken);

    public Task<CustomerSubscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
        => GuardTransportAsync(nameof(ReactivateSubscriptionAsync), async () =>
        {
            var current = await GetSubscriptionAsync(subscriptionId, cancellationToken)
                ?? throw new SubscriptionNotFoundException(subscriptionId);

            // A subscription that is merely pending cancellation is rescued by revoking the schedule;
            // reactivation proper is only for one that has already ended.
            if (current.CancelAtEndOfPeriod)
            {
                try
                {
                    await _client.SubscriptionStatus.CancelDelayedCancellation(subscriptionId, ct: cancellationToken);
                }
                catch (SdkException<CancelDelayedCancellationError> ex)
                {
                    if (!ex.Error.TryGetNoContent(out _))
                    {
                        throw Rejected(nameof(ReactivateSubscriptionAsync), ex.Error.TryGetRawError(out var raw) ? raw : null);
                    }
                }

                var refreshed = await GetSubscriptionAsync(subscriptionId, cancellationToken)
                    ?? throw new SubscriptionNotFoundException(subscriptionId);

                if (refreshed.IsBillable)
                {
                    return refreshed;
                }
            }

            try
            {
                var response = await _client.SubscriptionStatus.ReactivateSubscription(subscriptionId, body: null, ct: cancellationToken);
                return MapSubscription(response.Subscription, nameof(ReactivateSubscriptionAsync));
            }
            catch (SdkException<ReactivateSubscriptionError> ex)
            {
                if (ex.Error.TryGetErrorListResponse1(out var errors))
                {
                    throw new BillingProviderException(nameof(ReactivateSubscriptionAsync), Describe(errors), 422);
                }

                throw Rejected(nameof(ReactivateSubscriptionAsync), ex.Error.TryGetRawError(out var raw) ? raw : null);
            }
        }, cancellationToken);

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
        => await _catalog.GetProductFamilyIdAsync(async ct =>
        {
            IReadOnlyList<ProductFamilyResponse> families;
            try
            {
                families = await _client.ProductFamilies.ListProductFamilies(
                    dateField: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    ct: ct);
            }
            catch (SdkException<RawError> ex)
            {
                throw Rejected("ResolveProductFamily", ex.Error);
            }

            var match = families
                .Select(f => f.ProductFamily)
                .FirstOrDefault(f => f is not null &&
                    string.Equals(f.Handle, _settings.ProductFamilyHandle, StringComparison.Ordinal));

            if (match?.Id is null)
            {
                throw new BillingConfigurationException("ResolveProductFamily",
                    $"product family handle '{_settings.ProductFamilyHandle}' does not exist on this site. Seed the billing catalog before using the subscription features.");
            }

            return match.Id.Value;
        }, cancellationToken);

    private async Task<MeteredComponent> ResolveMeteredComponentAsync(CancellationToken cancellationToken)
    {
        Component component;
        try
        {
            var response = await _client.Components.FindComponent(_settings.MeteredComponentHandle, ct: cancellationToken);
            component = response.Component;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            throw new BillingConfigurationException(nameof(GetMeteredComponentAsync),
                $"metered component handle '{_settings.MeteredComponentHandle}' does not resolve on this site. Seed the billing catalog before recording usage.");
        }
        catch (SdkException<RawError> ex)
        {
            throw Rejected(nameof(GetMeteredComponentAsync), ex.Error);
        }

        var mapped = MapComponent(component);

        if (!mapped.IsMetered)
        {
            throw new BillingConfigurationException(nameof(GetMeteredComponentAsync),
                $"component '{mapped.Handle}' is of kind '{mapped.Kind}', not metered. A component's kind cannot be converted in place — archive it and recreate it as metered.");
        }

        if (!string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle) &&
            !string.IsNullOrWhiteSpace(component.ProductFamilyHandle) &&
            !string.Equals(component.ProductFamilyHandle, _settings.ProductFamilyHandle, StringComparison.Ordinal))
        {
            throw new BillingConfigurationException(nameof(GetMeteredComponentAsync),
                $"component '{mapped.Handle}' lives on product family '{component.ProductFamilyHandle}', not the configured '{_settings.ProductFamilyHandle}', so it is not available to these plans.");
        }

        if (mapped.IsArchived)
        {
            throw new BillingConfigurationException(nameof(GetMeteredComponentAsync),
                $"component '{mapped.Handle}' is archived and can no longer accrue usage.");
        }

        _logger.LogInformation("Resolved metered component {0} (id {1}) at {2} per unit.",
            mapped.Handle, mapped.Id, mapped.UnitPrice.ToString("C4", CultureInfo.InvariantCulture));

        return mapped;
    }

    private async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: cancellationToken);
            return MapCustomer(response.Customer);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw Rejected(nameof(EnsureCustomerAsync), ex.Error);
        }
    }

    private static SubscriptionPlan MapPlan(Product product) =>
        new(product.Id ?? 0,
            product.Handle ?? string.Empty,
            product.Name ?? product.Handle ?? "Unnamed plan",
            FromCents(product.PriceInCents),
            product.Interval ?? 0,
            product.IntervalUnit?.Value ?? string.Empty)
        {
            Description = product.Description,
            ProductFamilyHandle = product.ProductFamily?.Handle,
            IsArchived = product.ArchivedAt is not null,
            RequiresPaymentMethod = product.RequireCreditCard ?? false
        };

    private static BillingCustomer MapCustomer(Customer customer) =>
        new(customer.Id ?? 0, customer.Reference ?? string.Empty)
        {
            FirstName = customer.FirstName,
            LastName = customer.LastName,
            Email = customer.Email
        };

    private static MeteredComponent MapComponent(Component component)
    {
        var unitPrice = ParseDollars(component.UnitPrice) ?? FromCents(component.PricePerUnitInCents);

        return new MeteredComponent(
            component.Id ?? 0,
            component.Handle ?? string.Empty,
            component.Name ?? component.Handle ?? "Unnamed component",
            component.Kind?.Value ?? "unknown",
            component.Kind == ComponentKind.MeteredComponent,
            unitPrice)
        {
            UnitName = component.UnitName,
            PricingScheme = component.PricingScheme?.Value,
            IsArchived = component.Archived ?? component.ArchivedAt is not null
        };
    }

    private static CustomerSubscription MapSubscription(Subscription subscription) =>
        new(subscription.Id ?? 0, MapState(subscription.State))
        {
            ProviderState = subscription.State?.Value,
            PlanHandle = subscription.Product?.Handle,
            PlanName = subscription.Product?.Name,
            PlanPrice = FromCents(subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents),
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod ?? false,
            DelayedCancelAt = subscription.DelayedCancelAt,
            ScheduledPlanHandle = subscription.NextProductHandle,
            CustomerId = subscription.Customer?.Id,
            CustomerReference = subscription.Customer?.Reference ?? subscription.Reference
        };

    /// <summary>
    /// Unwraps a nullable subscription envelope. A success status with no subscription body means the
    /// provider accepted the call but told us nothing usable, which is a provider fault, not a domain
    /// outcome.
    /// </summary>
    private static CustomerSubscription MapSubscription(Subscription? subscription, string operation)
    {
        if (subscription is null)
        {
            throw new BillingProviderException(operation, "the provider returned no subscription in its response");
        }

        return MapSubscription(subscription);
    }

    private static SubscriptionLifecycleState MapState(SubscriptionState? state)
    {
        var wire = state?.Value;

        return wire switch
        {
            "pending" or "awaiting_signup" => SubscriptionLifecycleState.Pending,
            "trialing" => SubscriptionLifecycleState.Trialing,
            "active" or "assessing" => SubscriptionLifecycleState.Active,
            "on_hold" or "paused" => SubscriptionLifecycleState.Paused,
            "past_due" => SubscriptionLifecycleState.PastDue,
            "suspended" => SubscriptionLifecycleState.Suspended,
            "canceled" => SubscriptionLifecycleState.Canceled,
            "expired" => SubscriptionLifecycleState.Expired,
            "trial_ended" => SubscriptionLifecycleState.TrialEnded,
            "unpaid" => SubscriptionLifecycleState.Unpaid,
            "failed_to_create" or "soft_failure" => SubscriptionLifecycleState.Failed,
            _ => SubscriptionLifecycleState.Unknown
        };
    }

    private static decimal? ReadQuantity(Quantity1? quantity)
    {
        if (quantity is null)
        {
            return null;
        }

        if (quantity.TryGetInt(out var whole))
        {
            return whole;
        }

        if (quantity.TryGetString(out var text) &&
            decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    /// <summary>Converts a minor-unit amount to whole currency units. Never uses floating point.</summary>
    private static decimal FromCents(long? amountInCents) => (amountInCents ?? 0L) / 100m;

    /// <summary>Parses a provider amount that is already denominated in whole currency units.</summary>
    private static decimal? ParseDollars(string? amount)
        => decimal.TryParse(amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static string Describe(ErrorListResponse1 errors)
        => errors.Errors.Count == 0 ? "no detail was provided" : string.Join("; ", errors.Errors);

    private static string Describe(CancelSubscriptionErrorResponse response)
    {
        if (response.TryGetErrorListResponse1(out var list))
        {
            return Describe(list);
        }

        if (response.TryGetSingleErrorResponse1(out var single))
        {
            return single.Error;
        }

        return "no detail was provided";
    }

    private static string DescribeCustomerErrors(CustomerErrorResponse1 response)
    {
        var messages = new List<string>();
        messages.AddRange(response.Errors?.PerPage ?? Enumerable.Empty<string>());
        messages.AddRange(response.Errors?.PricePoint ?? Enumerable.Empty<string>());

        return messages.Count == 0 ? "the customer record was rejected" : string.Join("; ", messages);
    }

    private static BillingProviderException Rejected(string operation, RawError? error)
    {
        if (error is null)
        {
            return new BillingProviderException(operation, "the provider returned an error with no readable detail");
        }

        return new BillingProviderException(operation, DescribeBody(error), (int)error.StatusCode);
    }

    private static string DescribeBody(RawError error)
    {
        try
        {
            var body = error.ReadAsString();
            return string.IsNullOrWhiteSpace(body)
                ? $"HTTP {(int)error.StatusCode}"
                : body.Trim();
        }
        catch (Exception)
        {
            // The body is not readable as text; the status is still meaningful.
            return $"HTTP {(int)error.StatusCode}";
        }
    }

    /// <summary>
    /// Converts transport-level failures into the single failure type the domain understands, so a
    /// network fault is never mistaken for a provider rejection and no SDK exception escapes this class.
    /// </summary>
    private static async Task<T> GuardTransportAsync<T>(string operation,
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            return await action();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            throw new BillingUnavailableException(operation, ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new BillingUnavailableException(operation, ex);
        }
        catch (JsonException ex)
        {
            // The provider answered with a body the generated models cannot represent. That is a
            // provider-side failure, not a bug in the caller, and it must not escape as a raw
            // serialization error.
            throw new BillingProviderException(operation,
                "the provider returned a response this build could not interpret", null, ex);
        }
    }
}
