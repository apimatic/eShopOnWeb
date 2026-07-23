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
using MaxioAdvancedBilling.Models.AnyOf;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using DomainMeteredComponent = Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.MeteredComponent;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The single integration point with Maxio Advanced Billing. Nothing else in eShopOnWeb talks to
/// the provider: every Maxio type, wire name and error shape is confined to this class and its
/// mapper.
/// </summary>
/// <remarks>
/// <para>
/// The outbound target server is resolved once, from <see cref="MaxioSettings.ResolveBaseUrl"/>, so
/// pointing the identical build at production, a dev/sandbox tenant, or a local mock server is a
/// configuration change and never a code change. An explicit <c>Maxio:BaseUrl</c> always wins over
/// the subdomain-derived host.
/// </para>
/// <para>
/// Every failure — an SDK error response, an unreachable host, or a timeout — leaves this class as a
/// <see cref="BillingProviderException"/>, so callers never see a provider-specific exception.
/// </para>
/// </remarks>
public class MaxioBillingClient : IBillingClient
{
    /// <summary>Prefix the provider requires when addressing an entity by handle instead of by id.</summary>
    private const string HandlePrefix = "handle:";

    private const string UnknownFailure = "The billing provider rejected the request.";

    /// <summary>Upper bound on plan pages walked, so a provider paging bug cannot loop forever.</summary>
    private const int MaxPlanPages = 20;

    private const int PlanPageSize = 200;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioBillingClient> _logger;

    public MaxioBillingClient(HttpClient httpClient,
        IOptions<MaxioSettings> settings,
        IAppLogger<MaxioBillingClient> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        _client = new MaxioAdvancedBillingClient(httpClient, MaxioClientOptionsFactory.Create(_settings));
    }

    public async Task<IReadOnlyCollection<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var plans = new List<BillingPlan>();

        for (var page = 1; page <= MaxPlanPages; page++)
        {
            var pageNumber = page;
            var responses = string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle)
                ? await InvokeAsync<IReadOnlyList<ProductResponse>, RawError>("ListPlans",
                    () => _client.Products.ListProducts(
                        dateField: null,
                        filter: null,
                        endDate: null,
                        endDatetime: null,
                        startDate: null,
                        startDatetime: null,
                        includeArchived: false,
                        include: null,
                        page: pageNumber,
                        perPage: PlanPageSize,
                        ct: cancellationToken),
                    DescribeRaw,
                    cancellationToken)
                : await InvokeAsync<IReadOnlyList<ProductResponse>, ListProductsForProductFamilyError>("ListPlans",
                    () => _client.ProductFamilies.ListProductsForProductFamily(
                        productFamilyId: HandlePrefix + _settings.ProductFamilyHandle,
                        dateField: null,
                        filter: null,
                        startDate: null,
                        endDate: null,
                        startDatetime: null,
                        endDatetime: null,
                        includeArchived: false,
                        include: null,
                        page: pageNumber,
                        perPage: PlanPageSize,
                        ct: cancellationToken),
                    error => error.TryGetString(out var body)
                        ? new ProviderFailure(body, HttpStatusCode.NotFound)
                        : DescribeApiError(error),
                    cancellationToken);

            if (responses is null || responses.Count == 0)
            {
                break;
            }

            plans.AddRange(responses
                .Select(r => r.Product)
                .Where(p => p is not null && !p.ArchivedAt.HasValue)
                .Select(p => MaxioModelMapper.ToBillingPlan(p!)));

            if (responses.Count < PlanPageSize)
            {
                break;
            }
        }

        return plans;
    }

    public async Task<BillingPlan?> FindPlanByHandleAsync(string handle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            return null;
        }

        try
        {
            var response = await InvokeAsync<ProductResponse, RawError>("FindPlanByHandle",
                () => _client.Products.ReadProductByHandle(handle, ct: cancellationToken),
                DescribeRaw,
                cancellationToken);

            return response.Product is null ? null : MaxioModelMapper.ToBillingPlan(response.Product);
        }
        catch (BillingProviderException ex) when (IsAbsent(ex.StatusCode))
        {
            return null;
        }
    }

    public async Task<DomainMeteredComponent?> FindComponentByHandleAsync(string handle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            return null;
        }

        Component? component;
        try
        {
            var response = await InvokeAsync<ComponentResponse, RawError>("FindComponentByHandle",
                () => _client.Components.FindComponent(handle, ct: cancellationToken),
                DescribeRaw,
                cancellationToken);

            component = response.Component;
        }
        catch (BillingProviderException ex) when (IsAbsent(ex.StatusCode))
        {
            return null;
        }

        if (component is null)
        {
            return null;
        }

        // A component with the right handle on the wrong family is not available to this
        // integration's subscriptions, so it must not be treated as a match.
        if (!string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle) &&
            !string.IsNullOrWhiteSpace(component.ProductFamilyHandle) &&
            !string.Equals(component.ProductFamilyHandle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new BillingConfigurationException(
                $"Component '{handle}' exists but belongs to product family " +
                $"'{component.ProductFamilyHandle}', not the configured '{_settings.ProductFamilyHandle}'. " +
                "Recreate it on the correct family (UC0).");
        }

        return MaxioModelMapper.ToMeteredComponent(component);
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        try
        {
            var response = await InvokeAsync<CustomerResponse, RawError>("FindCustomerByReference",
                () => _client.Customers.ReadCustomerByReference(reference, ct: cancellationToken),
                DescribeRaw,
                cancellationToken);

            // A success that carries no customer means the reference is unused.
            return response.Customer is null ? null : MaxioModelMapper.ToBillingCustomer(response.Customer);
        }
        catch (BillingProviderException ex) when (IsAbsent(ex.StatusCode))
        {
            _logger.LogInformation(
                "No billing customer exists for reference '{0}' (provider replied {1}); a new one will be created.",
                reference, ex.StatusCode?.ToString() ?? "no status");
            return null;
        }
    }

    public async Task<BillingCustomer> CreateCustomerAsync(string reference,
        string email,
        string firstName,
        string lastName,
        CancellationToken cancellationToken = default)
    {
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        var response = await InvokeAsync<CustomerResponse, CreateCustomerError>("CreateCustomer",
            () => _client.Customers.CreateCustomer(body, ct: cancellationToken),
            error => error.TryGetCustomerErrorResponse1(out var payload)
                ? new ProviderFailure(Describe(payload), HttpStatusCode.UnprocessableEntity)
                : DescribeApiError(error),
            cancellationToken);

        return MaxioModelMapper.ToBillingCustomer(response.Customer);
    }

    public async Task<CustomerSubscription> CreateSubscriptionAsync(int customerId,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                CustomerId = customerId,
                ProductHandle = planHandle,
                // Bills the customer rather than auto-charging a card, so enrollment succeeds with no
                // payment profile on file.
                PaymentCollectionMethod = ResolveCollectionMethod()
            }
        };

        var response = await InvokeAsync<SubscriptionResponse, CreateSubscriptionError>("CreateSubscription",
            () => _client.Subscriptions.CreateSubscription(body, ct: cancellationToken),
            DescribeErrorList,
            cancellationToken);

        return RequireSubscription(response, "CreateSubscription");
    }

    public async Task<IReadOnlyCollection<CustomerSubscription>> ListSubscriptionsForCustomerAsync(int customerId,
        CancellationToken cancellationToken = default)
    {
        var responses = await InvokeAsync<IReadOnlyList<SubscriptionResponse>, RawError>("ListSubscriptionsForCustomer",
            () => _client.Customers.ListCustomerSubscriptions(customerId, ct: cancellationToken),
            DescribeRaw,
            cancellationToken);

        if (responses is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        return responses
            .Select(r => r.Subscription)
            .Where(s => s is not null)
            .Select(s => MaxioModelMapper.ToCustomerSubscription(s!))
            .ToList();
    }

    public async Task<CustomerSubscription?> GetSubscriptionAsync(int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await InvokeAsync<SubscriptionResponse, RawError>("GetSubscription",
                () => _client.Subscriptions.ReadSubscription(subscriptionId, include: null, ct: cancellationToken),
                DescribeRaw,
                cancellationToken);

            return response.Subscription is null
                ? null
                : MaxioModelMapper.ToCustomerSubscription(response.Subscription);
        }
        catch (BillingProviderException ex) when (IsAbsent(ex.StatusCode))
        {
            return null;
        }
    }

    public async Task<UsageRecord> RecordUsageAsync(int subscriptionId,
        string componentHandle,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(componentHandle))
        {
            throw new BillingConfigurationException(
                "No metered component handle was supplied, so usage cannot be addressed at the provider.");
        }

        var body = new CreateUsageRequest
        {
            Usage = new CreateUsage
            {
                Quantity = (double)quantity,
                Memo = memo
            }
        };

        var response = await InvokeAsync<UsageResponse, CreateUsageError>("RecordUsage",
            () => _client.SubscriptionComponents.CreateUsage(
                SubscriptionIdOrReference.Int(subscriptionId),
                ComponentIdModel.String(HandlePrefix + componentHandle),
                body,
                ct: cancellationToken),
            DescribeErrorList,
            cancellationToken);

        return MaxioModelMapper.ToUsageRecord(response.Usage);
    }

    public async Task<ComponentUsageSummary?> GetComponentUsageAsync(int subscriptionId,
        DomainMeteredComponent component,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await InvokeAsync<SubscriptionComponentResponse, ReadSubscriptionComponentError>("GetComponentUsage",
                () => _client.SubscriptionComponents.ReadSubscriptionComponent(subscriptionId, component.Id, ct: cancellationToken),
                error => error.TryGetNoContent(out var missing)
                    ? new ProviderFailure(ReadBody(missing), HttpStatusCode.NotFound)
                    : DescribeApiError(error),
                cancellationToken);

            return response.Component is null
                ? null
                : MaxioModelMapper.ToUsageSummary(response.Component, component.PricePerUnitInCents);
        }
        catch (BillingProviderException ex) when (IsAbsent(ex.StatusCode))
        {
            return null;
        }
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId,
        string currentPlanHandle,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default)
    {
        // A change deferred to the next renewal is applied without proration, so there is nothing
        // for the provider to prorate: the preview is the new plan's price from the next period.
        if (timing == PlanChangeTiming.AtNextRenewal)
        {
            var target = await FindPlanByHandleAsync(targetPlanHandle, cancellationToken);
            if (target is null)
            {
                throw new BillingConfigurationException(
                    $"The target plan handle '{targetPlanHandle}' does not resolve at the billing provider.");
            }

            return new PlanChangePreview(currentPlanHandle,
                targetPlanHandle,
                PlanChangeTiming.AtNextRenewal,
                proratedAdjustmentInCents: 0L,
                chargeInCents: target.PriceInCents,
                paymentDueInCents: 0L,
                creditAppliedInCents: 0L);
        }

        var body = new SubscriptionMigrationPreviewRequest
        {
            Migration = new SubscriptionMigrationPreviewOptions
            {
                ProductHandle = targetPlanHandle
            }
        };

        var response = await InvokeAsync<SubscriptionMigrationPreviewResponse, PreviewSubscriptionProductMigrationError>(
            "PreviewPlanChange",
            () => _client.SubscriptionProducts.PreviewSubscriptionProductMigration(subscriptionId, body, ct: cancellationToken),
            DescribeErrorList,
            cancellationToken);

        var migration = response.Migration;

        return new PlanChangePreview(currentPlanHandle,
            targetPlanHandle,
            PlanChangeTiming.Immediate,
            proratedAdjustmentInCents: migration.ProratedAdjustmentInCents ?? 0L,
            chargeInCents: migration.ChargeInCents ?? 0L,
            paymentDueInCents: migration.PaymentDueInCents ?? 0L,
            creditAppliedInCents: migration.CreditAppliedInCents ?? 0L);
    }

    public async Task<CustomerSubscription> ChangePlanAsync(int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default)
    {
        if (timing == PlanChangeTiming.AtNextRenewal)
        {
            var deferred = new UpdateSubscriptionRequest
            {
                Subscription = new UpdateSubscription
                {
                    ProductHandle = targetPlanHandle,
                    ProductChangeDelayed = true
                }
            };

            var deferredResponse = await InvokeAsync<SubscriptionResponse, UpdateSubscriptionError>("ChangePlanAtRenewal",
                () => _client.Subscriptions.UpdateSubscription(subscriptionId, deferred, ct: cancellationToken),
                DescribeErrorList,
                cancellationToken);

            return RequireSubscription(deferredResponse, "ChangePlanAtRenewal");
        }

        var body = new SubscriptionProductMigrationRequest
        {
            Migration = new SubscriptionProductMigration
            {
                ProductHandle = targetPlanHandle
            }
        };

        var response = await InvokeAsync<SubscriptionResponse, MigrateSubscriptionProductError>("ChangePlanNow",
            () => _client.SubscriptionProducts.MigrateSubscriptionProduct(subscriptionId, body, ct: cancellationToken),
            DescribeErrorList,
            cancellationToken);

        return RequireSubscription(response, "ChangePlanNow");
    }

    public async Task<CustomerSubscription> PauseSubscriptionAsync(int subscriptionId,
        DateTimeOffset? automaticallyResumeAt,
        CancellationToken cancellationToken = default)
    {
        var body = automaticallyResumeAt.HasValue
            ? new PauseRequest { Hold = new AutoResume { AutomaticallyResumeAt = automaticallyResumeAt.Value } }
            : null;

        var response = await InvokeAsync<SubscriptionResponse, PauseSubscriptionError>("PauseSubscription",
            () => _client.SubscriptionStatus.PauseSubscription(subscriptionId, body, ct: cancellationToken),
            DescribeErrorList,
            cancellationToken);

        return RequireSubscription(response, "PauseSubscription");
    }

    public async Task<CustomerSubscription> ResumeSubscriptionAsync(int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var response = await InvokeAsync<SubscriptionResponse, ResumeSubscriptionError>("ResumeSubscription",
            () => _client.SubscriptionStatus.ResumeSubscription(subscriptionId,
                calendarBillingResumptionCharge: null,
                ct: cancellationToken),
            DescribeErrorList,
            cancellationToken);

        return RequireSubscription(response, "ResumeSubscription");
    }

    public async Task<CustomerSubscription> CancelSubscriptionAsync(int subscriptionId,
        CancellationTiming timing,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var body = new CancellationRequest
        {
            Subscription = new CancellationOptions
            {
                CancellationMessage = reason
            }
        };

        if (timing == CancellationTiming.EndOfPeriod)
        {
            // The delayed-cancel endpoint only returns a message, so the resulting state has to be
            // read back from the subscription itself.
            await InvokeAsync<DelayedCancellationResponse, InitiateDelayedCancellationError>("CancelSubscriptionAtPeriodEnd",
                () => _client.SubscriptionStatus.InitiateDelayedCancellation(subscriptionId, body, ct: cancellationToken),
                error => error.TryGetNoContent(out var missing)
                    ? new ProviderFailure(ReadBody(missing), HttpStatusCode.NotFound)
                    : DescribeErrorList(error),
                cancellationToken);

            var refreshed = await GetSubscriptionAsync(subscriptionId, cancellationToken);
            if (refreshed is null)
            {
                throw new BillingProviderException("CancelSubscriptionAtPeriodEnd",
                    "The end-of-period cancellation was accepted but the subscription could no longer be read back.",
                    HttpStatusCode.NotFound);
            }

            return refreshed;
        }

        var response = await InvokeAsync<SubscriptionResponse, CancelSubscriptionApiError>("CancelSubscription",
            () => _client.SubscriptionStatus.CancelSubscription(subscriptionId, body, ct: cancellationToken),
            error =>
            {
                if (error.TryGetNoContent(out var missing))
                {
                    return new ProviderFailure(ReadBody(missing), HttpStatusCode.NotFound);
                }

                return error.TryGetCancelSubscriptionErrorResponse(out var payload)
                    ? new ProviderFailure(Describe(payload), HttpStatusCode.UnprocessableEntity)
                    : DescribeApiError(error);
            },
            cancellationToken);

        return RequireSubscription(response, "CancelSubscription");
    }

    public async Task<CustomerSubscription> ReactivateSubscriptionAsync(int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var response = await InvokeAsync<SubscriptionResponse, ReactivateSubscriptionError>("ReactivateSubscription",
            () => _client.SubscriptionStatus.ReactivateSubscription(subscriptionId, body: null, ct: cancellationToken),
            DescribeErrorList,
            cancellationToken);

        return RequireSubscription(response, "ReactivateSubscription");
    }

    /// <summary>
    /// Resolves the configured payment collection method. The value is a provider enum built from its
    /// wire string, so a site on a different billing architecture is a configuration change.
    /// </summary>
    private CollectionMethod ResolveCollectionMethod()
    {
        var configured = _settings.PaymentCollectionMethod;

        return string.IsNullOrWhiteSpace(configured)
            ? CollectionMethod.FromValue(MaxioSettings.DefaultPaymentCollectionMethod)
            : CollectionMethod.FromValue(configured.Trim().ToLowerInvariant());
    }

    /// <summary>
    /// Unwraps a subscription envelope whose payload the provider declares as optional. A missing
    /// payload after a successful write is a provider contract violation, not a domain outcome.
    /// </summary>
    private static CustomerSubscription RequireSubscription(SubscriptionResponse response, string operation)
    {
        if (response.Subscription is null)
        {
            throw new BillingProviderException(operation,
                "The billing provider accepted the request but returned no subscription.");
        }

        return MaxioModelMapper.ToCustomerSubscription(response.Subscription);
    }

    /// <summary>
    /// Runs one provider call, converting the SDK's typed error, an unreachable host, or a timeout
    /// into a single <see cref="BillingProviderException"/>. A cancellation the caller asked for is
    /// deliberately allowed to propagate unchanged.
    /// </summary>
    private static async Task<TResult> InvokeAsync<TResult, TError>(string operation,
        Func<Task<TResult>> call,
        Func<TError, ProviderFailure> describe,
        CancellationToken cancellationToken)
        where TError : notnull
    {
        try
        {
            return await call();
        }
        catch (SdkException<TError> ex)
        {
            var failure = describe(ex.Error);
            throw new BillingProviderException(operation, failure.Message, failure.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            // The SDK deserialises an error body straight into the operation's declared payload type
            // with no fallback, so a body that does not match escapes as a raw JsonException and the
            // typed exception above is never constructed. The response is gone by then, so the status
            // cannot be recovered — but the failure must still leave this seam as a provider error.
            throw new BillingProviderException(operation,
                "The billing provider returned a response that could not be read.", null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new BillingProviderException(operation, "The billing provider could not be reached.", null, ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BillingProviderException(operation, "The billing provider did not respond in time.", null, ex);
        }
    }

    /// <summary>A provider failure reduced to a message and, when known, an HTTP status.</summary>
    private readonly record struct ProviderFailure(string Message, HttpStatusCode? StatusCode);

    private static ProviderFailure DescribeRaw(RawError raw) => new(ReadBody(raw), raw.StatusCode);

    /// <summary>
    /// The fallback for a typed error whose status-specific accessors did not match: fall through to
    /// the raw body, which the SDK exposes on every typed error.
    /// </summary>
    private static ProviderFailure DescribeApiError(ApiError error)
    {
        return error.TryGetRawError(out var raw) ? DescribeRaw(raw) : new ProviderFailure(UnknownFailure, null);
    }

    /// <summary>
    /// Describes the 422 validation payload that most write operations share, falling back to the
    /// raw body when the typed accessor does not match.
    /// </summary>
    private static ProviderFailure DescribeErrorList<TError>(TError error) where TError : ApiError
    {
        // Each generated error type declares its own accessor, so reflection-free dispatch is done
        // by pattern matching on the concrete type.
        var payload = error switch
        {
            CreateSubscriptionError e => e.TryGetErrorListResponse1(out var p) ? p : null,
            UpdateSubscriptionError e => e.TryGetErrorListResponse1(out var p) ? p : null,
            CreateUsageError e => e.TryGetErrorListResponse1(out var p) ? p : null,
            MigrateSubscriptionProductError e => e.TryGetErrorListResponse1(out var p) ? p : null,
            PreviewSubscriptionProductMigrationError e => e.TryGetErrorListResponse1(out var p) ? p : null,
            PauseSubscriptionError e => e.TryGetErrorListResponse1(out var p) ? p : null,
            ResumeSubscriptionError e => e.TryGetErrorListResponse1(out var p) ? p : null,
            ReactivateSubscriptionError e => e.TryGetErrorListResponse1(out var p) ? p : null,
            InitiateDelayedCancellationError e => e.TryGetErrorListResponse1(out var p) ? p : null,
            _ => null
        };

        return payload is null
            ? DescribeApiError(error)
            : new ProviderFailure(Describe(payload), HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>
    /// Renders a typed error payload without depending on its member shape, so a change to the
    /// generated model cannot turn an error path into a crash.
    /// </summary>
    private static string Describe(object? payload)
    {
        if (payload is null)
        {
            return UnknownFailure;
        }

        try
        {
            var json = JsonSerializer.Serialize(payload);
            return string.IsNullOrWhiteSpace(json) || json == "{}" ? UnknownFailure : json;
        }
        catch (NotSupportedException)
        {
            return UnknownFailure;
        }
        catch (JsonException)
        {
            return UnknownFailure;
        }
    }

    /// <summary>Reads an error body defensively; a body that cannot be read must not mask the failure.</summary>
    private static string ReadBody(RawError raw)
    {
        try
        {
            var body = raw.ReadAsString();
            return string.IsNullOrWhiteSpace(body) ? UnknownFailure : body;
        }
        catch (Exception)
        {
            return UnknownFailure;
        }
    }

    /// <summary>
    /// Decides whether a provider failure means "this entity does not exist" rather than a real
    /// error. Authentication, authorisation and server faults are never treated as absence, so a bad
    /// API key can never be mistaken for an empty catalog.
    /// </summary>
    private static bool IsAbsent(HttpStatusCode? statusCode)
    {
        if (statusCode is null)
        {
            return false;
        }

        if (statusCode == HttpStatusCode.Unauthorized ||
            statusCode == HttpStatusCode.Forbidden ||
            statusCode == HttpStatusCode.TooManyRequests)
        {
            return false;
        }

        var code = (int)statusCode.Value;
        return code >= 400 && code < 500;
    }
}
