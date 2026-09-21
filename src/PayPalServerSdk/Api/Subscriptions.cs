using System;
using System.Collections.Generic;
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
    /// <param name="id">The ID of the plan.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ActivateBillingPlanError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Activates a plan, by ID.
    /// </remarks>
    public Task ActivateBillingPlan(string id,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v1/billing/plans/{id}/activate"),
            [new TemplateParam("id", id)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            EmptyBody.Instance,
            VoidResponse.Instance,
            ActivateBillingPlanErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);

    /// <summary>
    /// Activate subscription
    /// </summary>
    /// <param name="id">The ID of the subscription.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ActivateSubscriptionError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Activates the subscription.
    /// </remarks>
    public Task ActivateSubscription(string id,
        ActivateSubscriptionRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v1/billing/subscriptions/{id}/activate"),
            [new TemplateParam("id", id)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            VoidResponse.Instance,
            ActivateSubscriptionErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);

    /// <summary>
    /// Cancel subscription
    /// </summary>
    /// <param name="id">The ID of the subscription.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CancelSubscriptionError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Cancels the subscription.
    /// </remarks>
    public Task CancelSubscription(string id,
        CancelSubscriptionRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v1/billing/subscriptions/{id}/cancel"),
            [new TemplateParam("id", id)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            VoidResponse.Instance,
            CancelSubscriptionErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);

    /// <summary>
    /// Capture authorized payment on subscription
    /// </summary>
    /// <param name="id">The ID of the subscription.</param>
    /// <param name="payPalRequestId">The server stores keys for 72 hours.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SubscriptionTransactionDetails"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CaptureSubscriptionError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Captures an authorized payment from the subscriber on the subscription.
    /// </remarks>
    public Task<SubscriptionTransactionDetails> CaptureSubscription(string id,
        string? payPalRequestId,
        CaptureSubscriptionRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v1/billing/subscriptions/{id}/capture"),
            [new TemplateParam("id", id)],
            [],
            [new HeaderParam("PayPal-Request-Id", payPalRequestId),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<SubscriptionTransactionDetails>(),
            CaptureSubscriptionErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);

    /// <summary>
    /// Create plan
    /// </summary>
    /// <param name="payPalRequestId">The server stores keys for 72 hours.</param>
    /// <param name="body"></param>
    /// <param name="prefer">The preferred server response upon successful completion of the request. Value is: return=minimal. The server returns a minimal response to optimize communication between the API caller and the server. A minimal response includes the id, status and HATEOAS links. return=representation. The server returns a complete resource representation, including the current state of the resource.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="BillingPlan"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateBillingPlanError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates a plan that defines pricing and billing cycle details for subscriptions.
    /// </remarks>
    public Task<BillingPlan> CreateBillingPlan(string? payPalRequestId,
        PlanRequest? body,
        string? prefer = "return=minimal",
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v1/billing/plans"),
            [],
            [],
            [new HeaderParam("Prefer", prefer),
                new HeaderParam("PayPal-Request-Id", payPalRequestId),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<BillingPlan>(),
            CreateBillingPlanErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);

    /// <summary>
    /// Create subscription
    /// </summary>
    /// <param name="payPalRequestId">The server stores keys for 72 hours.</param>
    /// <param name="payPalClientMetadataId">The PayPal Client Metadata Id(CMID) is used to provide device-specific information to PayPal's risk engine. This is crucial for transactions that require device-specific risk assessments. Merchants typically use the Paypal SDK that automatically submits the CMID or they use tools like Fraudnet JS for web or Magnes JS for mobile to generate the CMID on the frontend and then pass it to the API as part of the request headers.</param>
    /// <param name="body"></param>
    /// <param name="prefer">The preferred server response upon successful completion of the request. Value is: return=minimal. The server returns a minimal response to optimize communication between the API caller and the server. A minimal response includes the id, status and HATEOAS links. return=representation. The server returns a complete resource representation, including the current state of the resource.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Subscription"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateSubscriptionError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates a subscription.
    /// </remarks>
    public Task<Subscription> CreateSubscription(string? payPalRequestId,
        string? payPalClientMetadataId,
        CreateSubscriptionRequest? body,
        string? prefer = "return=minimal",
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v1/billing/subscriptions"),
            [],
            [],
            [new HeaderParam("Prefer", prefer),
                new HeaderParam("PayPal-Request-Id", payPalRequestId),
                new HeaderParam("PayPal-Client-Metadata-Id", payPalClientMetadataId),
                new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<Subscription>(),
            CreateSubscriptionErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);

    /// <summary>
    /// Deactivate plan
    /// </summary>
    /// <param name="id">The ID of the plan.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="DeactivateBillingPlanError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Deactivates a plan, by ID.
    /// </remarks>
    public Task DeactivateBillingPlan(string id,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v1/billing/plans/{id}/deactivate"),
            [new TemplateParam("id", id)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            EmptyBody.Instance,
            VoidResponse.Instance,
            DeactivateBillingPlanErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);

    /// <summary>
    /// Show plan details
    /// </summary>
    /// <param name="id">The ID of the plan.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="BillingPlan"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="GetBillingPlanError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Shows details for a plan, by ID.
    /// </remarks>
    public Task<BillingPlan> GetBillingPlan(string id,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v1/billing/plans/{id}"),
            [new TemplateParam("id", id)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<BillingPlan>(),
            GetBillingPlanErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);

    /// <summary>
    /// Show subscription details
    /// </summary>
    /// <param name="id">The ID of the subscription.</param>
    /// <param name="fields">List of fields that are to be returned in the response. Possible value for fields are last_failed_payment and plan.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Subscription"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="GetSubscriptionError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Shows details for a subscription, by ID.
    /// </remarks>
    public Task<Subscription> GetSubscription(string id,
        string? fields,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v1/billing/subscriptions/{id}"),
            [new TemplateParam("id", id)],
            [new Param("fields", fields)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<Subscription>(),
            GetSubscriptionErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);

    /// <summary>
    /// List plans
    /// </summary>
    /// <param name="productId">Filters the response by a Product ID.</param>
    /// <param name="pageSize">The number of items to return in the response.</param>
    /// <param name="page">A non-zero integer which is the start index of the entire list of items to return in the response. The combination of <c>page=1</c> and <c>page_size=20</c> returns the first 20 items. The combination of <c>page=2</c> and <c>page_size=20</c> returns the next 20 items.</param>
    /// <param name="totalRequired">Indicates whether to show the total count in the response.</param>
    /// <param name="prefer">The preferred server response upon successful completion of the request. Value is: return=minimal. The server returns a minimal response to optimize communication between the API caller and the server. A minimal response includes the id, name, description and HATEOAS links. return=representation. The server returns a complete resource representation, including the current state of the resource.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="PlanCollection"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ListBillingPlansError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists billing plans.
    /// </remarks>
    public Task<PlanCollection> ListBillingPlans(string? productId,
        int? pageSize = 10,
        int? page = 1,
        bool? totalRequired = false,
        string? prefer = "return=minimal",
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v1/billing/plans"),
            [],
            [new Param("product_id", productId),
                new Param("page_size", pageSize),
                new Param("page", page),
                new Param("total_required", totalRequired)],
            [new HeaderParam("Prefer", prefer)],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<PlanCollection>(),
            ListBillingPlansErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);

    /// <summary>
    /// List transactions for subscription
    /// </summary>
    /// <param name="id">The ID of the subscription.</param>
    /// <param name="startTime">The start time of the range of transactions to list.</param>
    /// <param name="endTime">The end time of the range of transactions to list.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="TransactionsList"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ListSubscriptionTransactionsError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists transactions for a subscription.
    /// </remarks>
    public Task<TransactionsList> ListSubscriptionTransactions(string id,
        string startTime,
        string endTime,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v1/billing/subscriptions/{id}/transactions"),
            [new TemplateParam("id", id)],
            [new Param("start_time", startTime), new Param("end_time", endTime)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<TransactionsList>(),
            ListSubscriptionTransactionsErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);

    /// <summary>
    /// List subscriptions
    /// </summary>
    /// <param name="planIds">Filters the response by list of plan IDs. Filter supports upto 70 plan IDs. URLs should not exceed a length of 2000 characters.</param>
    /// <param name="statuses">Filters the response by list of subscription statuses.</param>
    /// <param name="createdAfter">Filters the response by subscription creation start time for a range of subscriptions.</param>
    /// <param name="createdBefore">Filters the response by subscription creation end time for a range of subscriptions.</param>
    /// <param name="statusUpdatedBefore">Filters the response by status update start time for a range of subscriptions.</param>
    /// <param name="statusUpdatedAfter">Filters the response by status update end time for a range of subscriptions.</param>
    /// <param name="filter">Filter the response using complex expressions that could use comparison operators like ge, gt, le, lt and logical operators such as 'and' and 'or'.</param>
    /// <param name="customerIds">Filters the response by comma separated vault customer IDs (FSS subscriptions only).</param>
    /// <param name="pageSize">The number of items to return in the response.</param>
    /// <param name="page">A non-zero integer which is the start index of the entire list of items to return in the response. The combination of <c>page=1</c> and <c>page_size=20</c> returns the first 20 items. The combination of <c>page=2</c> and <c>page_size=20</c> returns the next 20 items.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SubscriptionCollection"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ListSubscriptionsError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// List all subscriptions for merchant account.
    /// </remarks>
    public Task<SubscriptionCollection> ListSubscriptions(string? planIds,
        string? statuses,
        string? createdAfter,
        string? createdBefore,
        string? statusUpdatedBefore,
        string? statusUpdatedAfter,
        string? filter,
        IReadOnlyList<string>? customerIds,
        int? pageSize = 10,
        int? page = 1,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v1/billing/subscriptions"),
            [],
            [new Param("plan_ids", planIds),
                new Param("statuses", statuses),
                new Param("created_after", createdAfter),
                new Param("created_before", createdBefore),
                new Param("status_updated_before", statusUpdatedBefore),
                new Param("status_updated_after", statusUpdatedAfter),
                new Param("filter", filter),
                new Param("page_size", pageSize),
                new Param("page", page),
                new Param("customer_ids", customerIds)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<SubscriptionCollection>(),
            ListSubscriptionsErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);

    /// <summary>
    /// Update plan
    /// </summary>
    /// <param name="id">The ID of the plan.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="PatchBillingPlanError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates a plan with the <c>CREATED</c> or <c>ACTIVE</c> status. For an <c>INACTIVE</c> plan, you can make only status updates. You can patch these attributes and objects: Attribute or object Operations description replace payment_preferences.auto_bill_outstanding replace taxes.percentage replace payment_preferences.payment_failure_threshold replace payment_preferences.setup_fee replace payment_preferences.setup_fee_failure_action replace name replace
    /// </remarks>
    public Task PatchBillingPlan(string id,
        IReadOnlyList<Patch>? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v1/billing/plans/{id}"),
            [new TemplateParam("id", id)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            new HttpMethod("PATCH"),
            JsonRequest.Create(body),
            VoidResponse.Instance,
            PatchBillingPlanErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);

    /// <summary>
    /// Update subscription
    /// </summary>
    /// <param name="id">The ID for the subscription.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="PatchSubscriptionError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates a subscription which could be in ACTIVE or SUSPENDED status. You can override plan level default attributes by providing customised values for plan path in the patch request. You cannot update attributes that have already completed (Example - trial cycles can’t be updated if completed). Once overridden, changes to plan resource will not impact subscription. Any price update will not impact billing cycles within next 10 days (Applicable only for subscriptions funded by PayPal account). Following are the fields eligible for patch. Attribute or object Operations billing_info.outstanding_balance replace custom_id add,replace plan.billing_cycles[@sequence==n]. pricing_scheme.fixed_price add,replace plan.billing_cycles[@sequence==n]. pricing_scheme.tiers replace plan.billing_cycles[@sequence==n]. total_cycles replace plan.payment_preferences. auto_bill_outstanding replace plan.payment_preferences. payment_failure_threshold replace plan.taxes.inclusive add,replace plan.taxes.percentage add,replace shipping_amount add,replace start_time replace subscriber.shipping_address add,replace subscriber.payment_source (for subscriptions funded by card payments) replace
    /// </remarks>
    public Task PatchSubscription(string id,
        IReadOnlyList<Patch>? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v1/billing/subscriptions/{id}"),
            [new TemplateParam("id", id)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            new HttpMethod("PATCH"),
            JsonRequest.Create(body),
            VoidResponse.Instance,
            PatchSubscriptionErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);

    /// <summary>
    /// Revise plan or quantity of subscription
    /// </summary>
    /// <param name="id">The ID of the subscription.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ModifySubscriptionResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ReviseSubscriptionError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates the quantity of the product or service in a subscription. You can also use this method to switch the plan and update the <c>shipping_amount</c>, <c>shipping_address</c> values for the subscription. This type of update requires the buyer's consent.
    /// </remarks>
    public Task<ModifySubscriptionResponse> ReviseSubscription(string id,
        ModifySubscriptionRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v1/billing/subscriptions/{id}/revise"),
            [new TemplateParam("id", id)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<ModifySubscriptionResponse>(),
            ReviseSubscriptionErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);

    /// <summary>
    /// Suspend subscription
    /// </summary>
    /// <param name="id">The ID of the subscription.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="SuspendSubscriptionError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Suspends the subscription.
    /// </remarks>
    public Task SuspendSubscription(string id,
        SuspendSubscription? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v1/billing/subscriptions/{id}/suspend"),
            [new TemplateParam("id", id)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            VoidResponse.Instance,
            SuspendSubscriptionErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);

    /// <summary>
    /// Update pricing
    /// </summary>
    /// <param name="id">The ID for the plan.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="UpdateBillingPlanPricingSchemesError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates pricing for a plan. For example, you can update a regular billing cycle from $5 per month to $7 per month.
    /// </remarks>
    public Task UpdateBillingPlanPricingSchemes(string id,
        UpdatePricingSchemesRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/v1/billing/plans/{id}/update-pricing-schemes"),
            [new TemplateParam("id", id)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            VoidResponse.Instance,
            UpdateBillingPlanPricingSchemesErrorResponse.Instance,
            [_auth.Oauth2],
            requestOptions,
            ct);
}
