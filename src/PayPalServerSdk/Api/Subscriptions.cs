using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PayPalServerSdk.Core;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Core.Request;
using PayPalServerSdk.Core.Response;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Requests.Subscriptions;

namespace PayPalServerSdk.Api;

/// <summary>
/// Use the <c>/subscriptions</c> resource to create, update, retrieve, and cancel subscriptions and their associated plans.
/// </summary>
public sealed class Subscriptions
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Subscriptions(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Activate plan
    /// </summary>
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="ActivateBillingPlanError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Activates a plan, by ID.
    /// </remarks>
    public Task ActivateBillingPlan(ActivateBillingPlanRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v1/billing/plans/{id}/activate"),
            [new TemplateParam("id", request.Id)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            EmptyBody.Instance,
            VoidResponse.Instance,
            ActivateBillingPlanError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);

    /// <summary>
    /// Activate subscription
    /// </summary>
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="ActivateSubscriptionError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Activates the subscription.
    /// </remarks>
    public Task ActivateSubscription(ActivateSubscriptionOperationRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v1/billing/subscriptions/{id}/activate"),
            [new TemplateParam("id", request.Id)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(request.Body),
            VoidResponse.Instance,
            ActivateSubscriptionError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);

    /// <summary>
    /// Cancel subscription
    /// </summary>
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="CancelSubscriptionError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Cancels the subscription.
    /// </remarks>
    public Task CancelSubscription(CancelSubscriptionOperationRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v1/billing/subscriptions/{id}/cancel"),
            [new TemplateParam("id", request.Id)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(request.Body),
            VoidResponse.Instance,
            CancelSubscriptionError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);

    /// <summary>
    /// Capture authorized payment on subscription
    /// </summary>
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SubscriptionTransactionDetails"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="CaptureSubscriptionError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Captures an authorized payment from the subscriber on the subscription.
    /// </remarks>
    public Task<SubscriptionTransactionDetails> CaptureSubscription(CaptureSubscriptionOperationRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v1/billing/subscriptions/{id}/capture"),
            [new TemplateParam("id", request.Id)],
            [],
            [new HeaderParam("PayPal-Request-Id", request.PayPalRequestId),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(request.Body),
            JsonResponse.Create<SubscriptionTransactionDetails>(),
            CaptureSubscriptionError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);

    /// <summary>
    /// Create plan
    /// </summary>
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="BillingPlan"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="CreateBillingPlanError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates a plan that defines pricing and billing cycle details for subscriptions.
    /// </remarks>
    public Task<BillingPlan> CreateBillingPlan(CreateBillingPlanRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v1/billing/plans"),
            [],
            [],
            [new HeaderParam("Prefer", request.Prefer),
                new HeaderParam("PayPal-Request-Id", request.PayPalRequestId),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(request.Body),
            JsonResponse.Create<BillingPlan>(),
            CreateBillingPlanError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);

    /// <summary>
    /// Create subscription
    /// </summary>
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Subscription"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="CreateSubscriptionError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates a subscription.
    /// </remarks>
    public Task<Subscription> CreateSubscription(CreateSubscriptionOperationRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v1/billing/subscriptions"),
            [],
            [],
            [new HeaderParam("Prefer", request.Prefer),
                new HeaderParam("PayPal-Request-Id", request.PayPalRequestId),
                new HeaderParam("PayPal-Client-Metadata-Id", request.PayPalClientMetadataId),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(request.Body),
            JsonResponse.Create<Subscription>(),
            CreateSubscriptionError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);

    /// <summary>
    /// Deactivate plan
    /// </summary>
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="DeactivateBillingPlanError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Deactivates a plan, by ID.
    /// </remarks>
    public Task DeactivateBillingPlan(DeactivateBillingPlanRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v1/billing/plans/{id}/deactivate"),
            [new TemplateParam("id", request.Id)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            EmptyBody.Instance,
            VoidResponse.Instance,
            DeactivateBillingPlanError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);

    /// <summary>
    /// Show plan details
    /// </summary>
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="BillingPlan"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="GetBillingPlanError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Shows details for a plan, by ID.
    /// </remarks>
    public Task<BillingPlan> GetBillingPlan(GetBillingPlanRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v1/billing/plans/{id}"),
            [new TemplateParam("id", request.Id)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<BillingPlan>(),
            GetBillingPlanError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);

    /// <summary>
    /// Show subscription details
    /// </summary>
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Subscription"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="GetSubscriptionError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Shows details for a subscription, by ID.
    /// </remarks>
    public Task<Subscription> GetSubscription(GetSubscriptionRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v1/billing/subscriptions/{id}"),
            [new TemplateParam("id", request.Id)],
            [new Param("fields", request.Fields)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<Subscription>(),
            GetSubscriptionError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);

    /// <summary>
    /// List plans
    /// </summary>
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="PlanCollection"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="ListBillingPlansError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists billing plans.
    /// </remarks>
    public Task<PlanCollection> ListBillingPlans(ListBillingPlansRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v1/billing/plans"),
            [],
            [new Param("product_id", request.ProductId),
                new Param("page_size", request.PageSize),
                new Param("page", request.Page),
                new Param("total_required", request.TotalRequired)],
            [new HeaderParam("Prefer", request.Prefer)],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<PlanCollection>(),
            ListBillingPlansError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);

    /// <summary>
    /// List transactions for subscription
    /// </summary>
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="TransactionsList"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="ListSubscriptionTransactionsError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists transactions for a subscription.
    /// </remarks>
    public Task<TransactionsList> ListSubscriptionTransactions(ListSubscriptionTransactionsRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v1/billing/subscriptions/{id}/transactions"),
            [new TemplateParam("id", request.Id)],
            [new Param("start_time", request.StartTime), new Param("end_time", request.EndTime)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<TransactionsList>(),
            ListSubscriptionTransactionsError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);

    /// <summary>
    /// List subscriptions
    /// </summary>
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SubscriptionCollection"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="ListSubscriptionsError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// List all subscriptions for merchant account.
    /// </remarks>
    public Task<SubscriptionCollection> ListSubscriptions(ListSubscriptionsRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v1/billing/subscriptions"),
            [],
            [new Param("plan_ids", request.PlanIds),
                new Param("statuses", request.Statuses),
                new Param("created_after", request.CreatedAfter),
                new Param("created_before", request.CreatedBefore),
                new Param("status_updated_before", request.StatusUpdatedBefore),
                new Param("status_updated_after", request.StatusUpdatedAfter),
                new Param("filter", request.Filter),
                new Param("page_size", request.PageSize),
                new Param("page", request.Page),
                new Param("customer_ids", request.CustomerIds)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<SubscriptionCollection>(),
            ListSubscriptionsError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);

    /// <summary>
    /// Update plan
    /// </summary>
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="PatchBillingPlanError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates a plan with the <c>CREATED</c> or <c>ACTIVE</c> status. For an <c>INACTIVE</c> plan, you can make only status updates. You can patch these attributes and objects: Attribute or object Operations description replace payment_preferences.auto_bill_outstanding replace taxes.percentage replace payment_preferences.payment_failure_threshold replace payment_preferences.setup_fee replace payment_preferences.setup_fee_failure_action replace name replace
    /// </remarks>
    public Task PatchBillingPlan(PatchBillingPlanRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v1/billing/plans/{id}"),
            [new TemplateParam("id", request.Id)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            new HttpMethod("PATCH"),
            JsonRequest.Create(request.Body),
            VoidResponse.Instance,
            PatchBillingPlanError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);

    /// <summary>
    /// Update subscription
    /// </summary>
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="PatchSubscriptionError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates a subscription which could be in ACTIVE or SUSPENDED status. You can override plan level default attributes by providing customised values for plan path in the patch request. You cannot update attributes that have already completed (Example - trial cycles can’t be updated if completed). Once overridden, changes to plan resource will not impact subscription. Any price update will not impact billing cycles within next 10 days (Applicable only for subscriptions funded by PayPal account). Following are the fields eligible for patch. Attribute or object Operations billing_info.outstanding_balance replace custom_id add,replace plan.billing_cycles[@sequence==n]. pricing_scheme.fixed_price add,replace plan.billing_cycles[@sequence==n]. pricing_scheme.tiers replace plan.billing_cycles[@sequence==n]. total_cycles replace plan.payment_preferences. auto_bill_outstanding replace plan.payment_preferences. payment_failure_threshold replace plan.taxes.inclusive add,replace plan.taxes.percentage add,replace shipping_amount add,replace start_time replace subscriber.shipping_address add,replace subscriber.payment_source (for subscriptions funded by card payments) replace
    /// </remarks>
    public Task PatchSubscription(PatchSubscriptionRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v1/billing/subscriptions/{id}"),
            [new TemplateParam("id", request.Id)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            new HttpMethod("PATCH"),
            JsonRequest.Create(request.Body),
            VoidResponse.Instance,
            PatchSubscriptionError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);

    /// <summary>
    /// Revise plan or quantity of subscription
    /// </summary>
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ModifySubscriptionResponse"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="ReviseSubscriptionError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates the quantity of the product or service in a subscription. You can also use this method to switch the plan and update the <c>shipping_amount</c>, <c>shipping_address</c> values for the subscription. This type of update requires the buyer's consent.
    /// </remarks>
    public Task<ModifySubscriptionResponse> ReviseSubscription(ReviseSubscriptionRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v1/billing/subscriptions/{id}/revise"),
            [new TemplateParam("id", request.Id)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(request.Body),
            JsonResponse.Create<ModifySubscriptionResponse>(),
            ReviseSubscriptionError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);

    /// <summary>
    /// Suspend subscription
    /// </summary>
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="SuspendSubscriptionError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Suspends the subscription.
    /// </remarks>
    public Task SuspendSubscription(SuspendSubscriptionRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v1/billing/subscriptions/{id}/suspend"),
            [new TemplateParam("id", request.Id)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(request.Body),
            VoidResponse.Instance,
            SuspendSubscriptionError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);

    /// <summary>
    /// Update pricing
    /// </summary>
    /// <param name="request">The operation's inputs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="ApiException{TError}"> of <see cref="UpdateBillingPlanPricingSchemesError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates pricing for a plan. For example, you can update a regular billing cycle from $5 per month to $7 per month.
    /// </remarks>
    public Task UpdateBillingPlanPricingSchemes(UpdateBillingPlanPricingSchemesRequest request,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        _rawClient.Execute(_server.Default("/v1/billing/plans/{id}/update-pricing-schemes"),
            [new TemplateParam("id", request.Id)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(request.Body),
            VoidResponse.Instance,
            UpdateBillingPlanPricingSchemesError.Response,
            [_auth.Oauth2],
            requestOptions,
            cancellationToken);
}
