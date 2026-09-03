using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Maxio.Core;
using Maxio.Core.Authentication;
using Maxio.Core.ErrorResponse;
using Maxio.Core.Exceptions;
using Maxio.Core.Extensions;
using Maxio.Core.Models;
using Maxio.Core.Request;
using Maxio.Core.Response;
using Maxio.Errors;
using Maxio.Models;
using Maxio.Models.AnyOf;
using Maxio.Models.Enums;

namespace Maxio.Api;

public sealed class SubscriptionComponents
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal SubscriptionComponents(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Activate Event-Based Component
    /// </summary>
    /// <param name="subscriptionId">The Advanced Billing id of the subscription</param>
    /// <param name="componentId">The Advanced Billing id of the component</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Activates an event-based component for a single subscription.
    /// <para>
    /// In order to bill your subscribers on your Events data under the Events-Based Billing feature, the components must be activated for the subscriber.
    /// </para>
    /// <para>
    /// Learn more about the role of activation in the <see href="https://maxio.zendesk.com/hc/en-us/articles/24260323329805-Events-Based-Billing-Overview">Events-Based Billing docs</see>.
    /// </para>
    /// <para>
    /// Use this endpoint to activate an event-based component for a single subscription. Activating an event-based component causes Advanced Billing to bill for events when the subscription is renewed.
    /// </para>
    /// <para>
    /// *Note: it is possible to stream events for a subscription at any time, regardless of component activation status. The activation status only determines if the subscription should be billed for event-based component usage at renewal.*
    /// </para>
    /// </remarks>
    public Task ActivateEventBasedComponent(int subscriptionId,
        int componentId,
        ActivateEventBasedComponent? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/event_based_billing/subscriptions/{subscription_id}/components/{component_id}/activate.json"),
            [new TemplateParam("subscription_id", subscriptionId), new TemplateParam("component_id", componentId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            VoidResponse.Instance,
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Allocate Component
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="componentId">The Advanced Billing id of the component</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="AllocationResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="AllocateComponentError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates an allocation, sets the current allocated quantity for the component, and records a memo. Allocations can only be updated for Quantity, On/Off, and Prepaid Components.
    /// <para>
    /// When creating an allocation via the API, you can pass the <c>upgrade_charge</c>, <c>downgrade_credit</c>, and <c>accrue_charge</c> to be applied.
    /// </para>
    /// <para>
    /// &gt; <b>Note:</b> These proration and accrual fields are ignored for Prepaid Components since this component type always generates charges immediately without proration.
    /// </para>
    /// <para>
    /// For information on prorated components and upgrade/downgrade schemes, see <see href="https://maxio.zendesk.com/hc/en-us/articles/24251906165133-Component-Allocations-Proration">Setting Component Allocations.</see>
    /// </para>
    /// <para>
    /// ### Order of Resolution for upgrade_charge and downgrade_credit
    /// </para>
    /// <list type="number">
    ///   <item><description>Per allocation in API call (within a single allocation of the <c>allocations</c> array)</description></item>
    ///   <item><description><see href="https://maxio.zendesk.com/hc/en-us/articles/24251883961485-Component-Allocations-Overview">Component-level default value</see></description></item>
    ///   <item><description>Allocation API call top level (outside of the <c>allocations</c> array)</description></item>
    ///   <item><description><see href="https://maxio.zendesk.com/hc/en-us/articles/24251906165133-Component-Allocations-Proration#proration-schemes">Site-level default value</see></description></item>
    /// </list>
    /// <para>
    /// ### Order of Resolution for accrue charge
    /// </para>
    /// <list type="number">
    ///   <item><description>Allocation API call top level (outside of the <c>allocations</c> array)</description></item>
    ///   <item><description><see href="https://maxio.zendesk.com/hc/en-us/articles/24251906165133-Component-Allocations-Proration#proration-schemes">Site-level default value</see></description></item>
    /// </list>
    /// <para>
    /// &gt; <b>Note:</b> Proration uses the current price of the component as well as the current tax rates. Changes to either may cause the prorated charge/credit to be wrong.
    /// </para>
    /// <para>
    /// For more information, see the <see href="https://maxio.zendesk.com/hc/en-us/articles/24251883961485-Component-Allocations-Overview">Component Allocations</see> product Documentation.
    /// </para>
    /// </remarks>
    public Task<AllocationResponse> AllocateComponent(int subscriptionId,
        int componentId,
        CreateAllocationRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/components/{component_id}/allocations.json"),
            [new TemplateParam("subscription_id", subscriptionId), new TemplateParam("component_id", componentId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<AllocationResponse>(),
            AllocateComponentErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Allocate Components
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="IReadOnlyList{T}"/> of <see cref="AllocationResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="AllocateComponentsError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates multiple allocations, sets the current allocated quantity for each of the components, and records a memo.   A <c>component_id</c> is required for each allocation.
    /// <para>
    /// The charges and/or credits that are created will be rolled up into a single total which is used to determine whether this is an upgrade or a downgrade.
    /// </para>
    /// <para>
    /// ### Order of Resolution for upgrade_charge and downgrade_credit
    /// </para>
    /// <list type="number">
    ///   <item><description>Per allocation in API call (within a single allocation of the <c>allocations</c> array)</description></item>
    ///   <item><description><see href="https://maxio.zendesk.com/hc/en-us/articles/24251883961485-Component-Allocations-Overview">Component-level default value</see></description></item>
    ///   <item><description>Allocation API call top level (outside of the <c>allocations</c> array)</description></item>
    ///   <item><description><see href="https://maxio.zendesk.com/hc/en-us/articles/24251906165133-Component-Allocations-Proration#proration-schemes">Site-level default value</see></description></item>
    /// </list>
    /// <para>
    /// ### Order of Resolution for accrue charge
    /// </para>
    /// <list type="number">
    ///   <item><description>Allocation API call top level (outside of the <c>allocations</c> array)</description></item>
    ///   <item><description><see href="https://maxio.zendesk.com/hc/en-us/articles/24251906165133-Component-Allocations-Proration#proration-schemes">Site-level default value</see></description></item>
    /// </list>
    /// <para>
    /// &gt; <b>Note:</b> Proration uses the current price of the component as well as the current tax rates. Changes to either may cause the prorated charge/credit to be wrong.
    /// </para>
    /// <para>
    /// For more information, see the <see href="https://maxio.zendesk.com/hc/en-us/articles/24251883961485-Component-Allocations-Overview">Component Allocations</see> product documentation.
    /// </para>
    /// </remarks>
    public Task<IReadOnlyList<AllocationResponse>> AllocateComponents(int subscriptionId,
        AllocateComponents? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/allocations.json"),
            [new TemplateParam("subscription_id", subscriptionId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<IReadOnlyList<AllocationResponse>>(),
            AllocateComponentsErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Bulk Event Ingestion
    /// </summary>
    /// <param name="apiHandle">Identifies the Stream for which the events should be published.</param>
    /// <param name="storeUid">If you've attached your own Keen project as an Advanced Billing event data-store, use this parameter to indicate the data-store.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Records a collection of events.
    /// <para>
    /// *Note: this endpoint differs from the standard Chargify API endpoints in that the subdomain will be <c>events</c> and your site subdomain will be included in the URL path.*
    /// </para>
    /// <para>
    /// A maximum of 1000 events can be published in a single request. A 422 will be returned if this limit is exceeded.
    /// </para>
    /// </remarks>
    public Task BulkRecordEvents(string apiHandle,
        string? storeUid,
        IReadOnlyList<EbbEvent>? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Ebb("/events/{api_handle}/bulk.json"),
            [new TemplateParam("api_handle", apiHandle)],
            [new Param("store_uid", storeUid)],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            VoidResponse.Instance,
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Bulk Reset Subscription Components' Price Points
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SubscriptionResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Resets all of a subscription's components to use the current default.
    /// <para>
    /// <b>Note</b>: this will update the price point for all of the subscription's components, even ones that have not been allocated yet.
    /// </para>
    /// </remarks>
    public Task<SubscriptionResponse> BulkResetSubscriptionComponentsPricePoints(int subscriptionId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/price_points/reset.json"),
            [new TemplateParam("subscription_id", subscriptionId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            EmptyBody.Instance,
            JsonResponse.Create<SubscriptionResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Bulk Update Subscription Components' Price Points
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="BulkComponentsPricePointAssignment"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="BulkUpdateSubscriptionComponentsPricePointsError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates the price points on one or more of a subscription's components.
    /// <para>
    /// The <c>price_point</c> key can take either a:
    /// 1. Price point id (integer)
    /// 2. Price point handle (string)
    /// 3. <c>"_default"</c> string, which will reset the price point to the component's current default price point.
    /// </para>
    /// </remarks>
    public Task<BulkComponentsPricePointAssignment> BulkUpdateSubscriptionComponentsPricePoints(int subscriptionId,
        BulkComponentsPricePointAssignment? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/price_points.json"),
            [new TemplateParam("subscription_id", subscriptionId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<BulkComponentsPricePointAssignment>(),
            BulkUpdateSubscriptionComponentsPricePointsErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Create Usage
    /// </summary>
    /// <param name="subscriptionIdOrReference">Either the Advanced Billing subscription ID (integer) or the subscription reference (string). Important: In cases where a numeric string value matches both an existing subscription ID and an existing subscription reference, the system will prioritize the subscription ID lookup. For example, if both subscription ID 123 and subscription reference "123" exist, passing "123" will return the subscription with ID 123.</param>
    /// <param name="componentId">Either the Advanced Billing id for the component or the component's handle prefixed by <c>handle:</c></param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="UsageResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateUsageError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Records an instance of metered or prepaid usage for a subscription.
    /// <para>
    /// You can report metered or prepaid usage to Advanced Billing as often as you wish. You can report usage as it happens or periodically, such as each night or once per billing period.
    /// </para>
    /// <para>
    /// Full documentation on how to create Components in the Advanced Billing UI can be located <see href="https://maxio.zendesk.com/hc/en-us/articles/24261149711501-Create-Edit-and-Archive-Components">here</see>. Additionally, for information on how to record component usage against a subscription, see the following resources:
    /// </para>
    /// <para>
    /// It is not possible to record metered usage for more than one component at a time. Usage should be reported as one API call per component on a single subscription. For example, to record that a subscriber has sent both an SMS Message and an Email, send an API call for each.
    /// </para>
    /// <para>
    /// See the following product documentation articles for more information:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><see href="https://maxio.zendesk.com/hc/en-us/articles/24261149711501-Create-Edit-and-Archive-Components">Create and Manage Components</see></description></item>
    ///   <item><description><see href="https://maxio.zendesk.com/hc/en-us/articles/24251890500109-Reporting-Component-Allocations#reporting-metered-component-usage">Recording Metered Component Usage</see></description></item>
    ///   <item><description><see href="https://maxio.zendesk.com/hc/en-us/articles/24251890500109-Reporting-Component-Allocations#reporting-prepaid-component-status">Reporting Prepaid Component Status</see></description></item>
    /// </list>
    /// <para>
    /// The <c>quantity</c> from usage for each component is accumulated to the <c>unit_balance</c> on the <see href="$e/Subscription%20Components/readSubscriptionComponent">Component Line Item</see> for the subscription.
    /// </para>
    /// <para>
    /// ## Price Point ID usage
    /// </para>
    /// <para>
    /// If you are using price points, for metered and prepaid usage components Advanced Billing gives you the option to specify a price point in your request.
    /// </para>
    /// <para>
    /// You do not need to specify a price point ID. If a price point is not included, the default price point for the component will be used when the usage is recorded.
    /// </para>
    /// <para>
    /// ## Deducting Usage
    /// </para>
    /// <para>
    /// If you need to reverse a previous usage report or otherwise deduct from the current usage balance, you can provide a negative quantity.
    /// </para>
    /// <para>
    /// Example:
    /// </para>
    /// <para>
    /// Previously recorded quantity was 5000:
    /// </para>
    /// <code>
    /// {
    ///   "usage": {
    ///     "quantity": 5000,
    ///     "memo": "Recording 5000 units"
    ///   }
    /// }
    /// </code>
    /// <para>
    /// To reduce the quantity to <c>0</c>, POST the following payload:
    /// </para>
    /// <para>
    /// <code>
    /// {
    ///   "usage": {
    ///     "quantity": -5000,
    ///     "memo": "Deducting 5000 units"
    ///   }
    /// }
    /// </code>
    /// The <c>unit_balance</c> has a floor of <c>0</c>; negative unit balances are never allowed. For example, if the usage balance is 100 and you deduct 200 units, the unit balance would then be <c>0</c>, not <c>-100</c>.
    /// </para>
    /// </remarks>
    public Task<UsageResponse> CreateUsage(SubscriptionIdOrReference subscriptionIdOrReference,
        ComponentIdModel componentId,
        CreateUsageRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id_or_reference}/components/{component_id}/usages.json"),
            [new TemplateParam("subscription_id_or_reference", subscriptionIdOrReference),
                new TemplateParam("component_id", componentId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<UsageResponse>(),
            CreateUsageErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Deactivate Event-Based Component
    /// </summary>
    /// <param name="subscriptionId">The Advanced Billing id of the subscription</param>
    /// <param name="componentId">The Advanced Billing id of the component</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Deactivates an event-based component for a single subscription. Deactivating the event-based component causes Advanced Billing to ignore related events at subscription renewal.
    /// </remarks>
    public Task DeactivateEventBasedComponent(int subscriptionId,
        int componentId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/event_based_billing/subscriptions/{subscription_id}/components/{component_id}/deactivate.json"),
            [new TemplateParam("subscription_id", subscriptionId), new TemplateParam("component_id", componentId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            EmptyBody.Instance,
            VoidResponse.Instance,
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Delete Prepaid Usage Allocation
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="componentId">The Advanced Billing id of the component</param>
    /// <param name="allocationId">The Advanced Billing id of the allocation</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="DeletePrepaidUsageAllocationError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Deletes a prepaid usage allocation.
    /// <para>
    /// Prepaid Usage components are unique in that their allocations are always additive. In order to reduce a subscription's allocated quantity for a prepaid usage component, each allocation must be destroyed individually via this endpoint.
    /// </para>
    /// <para>
    /// ## Credit Scheme
    /// </para>
    /// <para>
    /// By default, destroying an allocation will generate a service credit on the subscription. This behavior can be modified with the optional <c>credit_scheme</c> parameter on this endpoint. The accepted values are:
    /// </para>
    /// <list type="number">
    ///   <item><description><c>none</c>: The allocation will be destroyed and the balances will be updated but no service credit or refund will be created.</description></item>
    ///   <item><description><c>credit</c>: The allocation will be destroyed and the balances will be updated and a service credit will be generated. This is also the default behavior if the <c>credit_scheme</c> param is not passed.</description></item>
    ///   <item><description><c>refund</c>: The allocation will be destroyed and the balances will be updated and a refund will be issued along with a Credit Note.</description></item>
    /// </list>
    /// </remarks>
    public Task DeletePrepaidUsageAllocation(int subscriptionId,
        int componentId,
        int allocationId,
        CreditSchemeRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/components/{component_id}/allocations/{allocation_id}.json"),
            [new TemplateParam("subscription_id", subscriptionId),
                new TemplateParam("component_id", componentId),
                new TemplateParam("allocation_id", allocationId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            JsonRequest.Create(body),
            VoidResponse.Instance,
            DeletePrepaidUsageAllocationErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List Allocations
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="componentId">The Advanced Billing id of the component</param>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="IReadOnlyList{T}"/> of <see cref="AllocationResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ListAllocationsError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists the 50 most recent Allocations, ordered by most recent first.
    /// <para>
    /// ## On/Off Components
    /// </para>
    /// <para>
    /// When a subscription's on/off component has been toggled to on (<c>1</c>) or off (<c>0</c>), usage will be logged in this response.
    /// </para>
    /// </remarks>
    public Task<IReadOnlyList<AllocationResponse>> ListAllocations(int subscriptionId,
        int componentId,
        int? page = 1,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/components/{component_id}/allocations.json"),
            [new TemplateParam("subscription_id", subscriptionId), new TemplateParam("component_id", componentId)],
            [new Param("page", page)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<IReadOnlyList<AllocationResponse>>(),
            ListAllocationsErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List Subscription Components
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="dateField">The type of filter you'd like to apply to your search. Use in query <c>date_field=updated_at</c>.</param>
    /// <param name="direction">Controls the order in which results are returned. Use in query <c>direction=asc</c>.</param>
    /// <param name="filter">Filter to use for List Subscription Components operation</param>
    /// <param name="endDate">The end date (format YYYY-MM-DD) with which to filter the date_field. Returns components with a timestamp up to and including 11:59:59PM in your site’s time zone on the date specified.</param>
    /// <param name="endDatetime">The end date and time (format YYYY-MM-DD HH:MM:SS) with which to filter the date_field. Returns components with a timestamp at or before exact time provided in query. You can specify timezone in query - otherwise your site''s time zone will be used. If provided, this parameter will be used instead of end_date.</param>
    /// <param name="pricePointIds">Allows fetching components allocation only if price point id is present. Use in query <c>price_point_ids=not_null</c>.</param>
    /// <param name="productFamilyIds">Allows fetching components allocation with matching product family id based on provided ids. Use in query <c>product_family_ids=1,2,3</c>.</param>
    /// <param name="sort">The attribute by which to sort. Use in query <c>sort=updated_at</c>.</param>
    /// <param name="startDate">The start date (format YYYY-MM-DD) with which to filter the date_field. Returns components with a timestamp at or after midnight (12:00:00 AM) in your site’s time zone on the date specified.</param>
    /// <param name="startDatetime">The start date and time (format YYYY-MM-DD HH:MM:SS) with which to filter the date_field. Returns components with a timestamp at or after exact time provided in query. You can specify timezone in query - otherwise your site''s time zone will be used. If provided, this parameter will be used instead of start_date.</param>
    /// <param name="include">Allows including additional data in the response. Use in query <c>include=subscription,historic_usages</c>.</param>
    /// <param name="inUse">If in_use is set to true, it returns only components that are currently in use. However, if it's set to false or not provided, it returns all components connected with the subscription.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="IReadOnlyList{T}"/> of <see cref="SubscriptionComponentResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists a subscription's applied components.
    /// <para>
    /// ## Archived Components
    /// </para>
    /// <para>
    /// When requesting to list components for a given subscription, if the subscription contains <b>archived</b> components they will be listed in the server response.
    /// </para>
    /// </remarks>
    public Task<IReadOnlyList<SubscriptionComponentResponse>> ListSubscriptionComponents(int subscriptionId,
        SubscriptionListDateField? dateField,
        SortingDirection? direction,
        ListSubscriptionComponentsFilter? filter,
        string? endDate,
        string? endDatetime,
        IncludeNotNull? pricePointIds,
        IReadOnlyList<int>? productFamilyIds,
        ListSubscriptionComponentsSort? sort,
        string? startDate,
        string? startDatetime,
        IReadOnlyList<ListSubscriptionComponentsInclude>? include,
        bool? inUse,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/components.json"),
            [new TemplateParam("subscription_id", subscriptionId)],
            [new Param("date_field", dateField),
                new Param("direction", direction),
                new Param("filter", filter),
                new Param("end_date", endDate),
                new Param("end_datetime", endDatetime),
                new Param("price_point_ids", pricePointIds),
                new Param("product_family_ids", productFamilyIds),
                new Param("sort", sort),
                new Param("start_date", startDate),
                new Param("start_datetime", startDatetime),
                new Param("include", include),
                new Param("in_use", inUse)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<IReadOnlyList<SubscriptionComponentResponse>>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List Subscription Components for Site
    /// </summary>
    /// <param name="sort">The attribute by which to sort. Use in query: <c>sort=updated_at</c>.</param>
    /// <param name="direction">Controls the order in which results are returned. Use in query <c>direction=asc</c>.</param>
    /// <param name="filter">Filter to use for List Subscription Components For Site operation</param>
    /// <param name="dateField">The type of filter you'd like to apply to your search. Use in query: <c>date_field=updated_at</c>.</param>
    /// <param name="startDate">The start date (format YYYY-MM-DD) with which to filter the date_field. Returns components with a timestamp at or after midnight (12:00:00 AM) in your site’s time zone on the date specified. Use in query <c>start_date=2011-12-15</c>.</param>
    /// <param name="startDatetime">The start date and time (format YYYY-MM-DD HH:MM:SS) with which to filter the date_field. Returns components with a timestamp at or after exact time provided in query. You can specify timezone in query - otherwise your site''s time zone will be used. If provided, this parameter will be used instead of start_date. Use in query <c>start_datetime=2022-07-01 09:00:05</c>.</param>
    /// <param name="endDate">The end date (format YYYY-MM-DD) with which to filter the date_field. Returns components with a timestamp up to and including 11:59:59PM in your site’s time zone on the date specified. Use in query <c>end_date=2011-12-16</c>.</param>
    /// <param name="endDatetime">The end date and time (format YYYY-MM-DD HH:MM:SS) with which to filter the date_field. Returns components with a timestamp at or before exact time provided in query. You can specify timezone in query - otherwise your site''s time zone will be used. If provided, this parameter will be used instead of end_date. Use in query <c>end_datetime=2022-07-01 09:00:05</c>.</param>
    /// <param name="subscriptionIds">Allows fetching components allocation with matching subscription id based on provided ids. Use in query <c>subscription_ids=1,2,3</c>.</param>
    /// <param name="pricePointIds">Allows fetching components allocation only if price point id is present. Use in query <c>price_point_ids=not_null</c>.</param>
    /// <param name="productFamilyIds">Allows fetching components allocation with matching product family id based on provided ids. Use in query <c>product_family_ids=1,2,3</c>.</param>
    /// <param name="include">Allows including additional data in the response. Use in query <c>include=subscription,historic_usages</c>.</param>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="perPage">This parameter indicates how many records to fetch in each request. Default value is 20. The maximum allowed values is 200; any per_page value over 200 will be changed to 200. Use in query <c>per_page=200</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListSubscriptionComponentsResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists components applied to each subscription.
    /// </remarks>
    public Task<ListSubscriptionComponentsResponse> ListSubscriptionComponentsForSite(ListSubscriptionComponentsSort? sort,
        SortingDirection? direction,
        ListSubscriptionComponentsForSiteFilter? filter,
        SubscriptionListDateField? dateField,
        string? startDate,
        string? startDatetime,
        string? endDate,
        string? endDatetime,
        IReadOnlyList<int>? subscriptionIds,
        IncludeNotNull? pricePointIds,
        IReadOnlyList<int>? productFamilyIds,
        ListSubscriptionComponentsInclude? include,
        int? page = 1,
        int? perPage = 20,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions_components.json"),
            [],
            [new Param("page", page),
                new Param("per_page", perPage),
                new Param("sort", sort),
                new Param("direction", direction),
                new Param("filter", filter),
                new Param("date_field", dateField),
                new Param("start_date", startDate),
                new Param("start_datetime", startDatetime),
                new Param("end_date", endDate),
                new Param("end_datetime", endDatetime),
                new Param("subscription_ids", subscriptionIds),
                new Param("price_point_ids", pricePointIds),
                new Param("product_family_ids", productFamilyIds),
                new Param("include", include)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListSubscriptionComponentsResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List Usages
    /// </summary>
    /// <param name="subscriptionIdOrReference">Either the Advanced Billing subscription ID (integer) or the subscription reference (string). Important: In cases where a numeric string value matches both an existing subscription ID and an existing subscription reference, the system will prioritize the subscription ID lookup. For example, if both subscription ID 123 and subscription reference "123" exist, passing "123" will return the subscription with ID 123.</param>
    /// <param name="componentId">Either the Advanced Billing id for the component or the component's handle prefixed by <c>handle:</c></param>
    /// <param name="sinceId">Returns usages with an id greater than or equal to the one specified.</param>
    /// <param name="maxId">Returns usages with an id less than or equal to the one specified.</param>
    /// <param name="sinceDate">Returns usages with a created_at date greater than or equal to midnight (12:00 AM) on the date specified.</param>
    /// <param name="untilDate">Returns usages with a created_at date less than or equal to midnight (12:00 AM) on the date specified.</param>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="perPage">This parameter indicates how many records to fetch in each request. Default value is 20. The maximum allowed values is 200; any per_page value over 200 will be changed to 200. Use in query <c>per_page=200</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="IReadOnlyList{T}"/> of <see cref="UsageResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists usages associated with a subscription for a particular metered component. This will display the previously recorded components for a subscription.
    /// <para>
    /// This endpoint is not compatible with quantity-based components.
    /// </para>
    /// <para>
    /// ## Since Date and Until Date Usage
    /// </para>
    /// <para>
    /// Note: The <c>since_date</c> and <c>until_date</c> attributes each default to midnight on the date specified. For example, in order to list usages for January 20th, you would need to append the following to the URL.
    /// </para>
    /// <code>
    /// ?since_date=2016-01-20&amp;until_date=2016-01-21
    /// </code>
    /// <para>
    /// ## Read Usage by Handle
    /// </para>
    /// <para>
    /// Use this endpoint to read the previously recorded components for a subscription.  You can now specify either the component id (integer) or the component handle prefixed by "handle:" to specify the unique identifier for the component you are working with.
    /// </para>
    /// </remarks>
    public Task<IReadOnlyList<UsageResponse>> ListUsages(SubscriptionIdOrReference subscriptionIdOrReference,
        ComponentIdModel componentId,
        long? sinceId,
        long? maxId,
        DateTimeOffset? sinceDate,
        DateTimeOffset? untilDate,
        int? page = 1,
        int? perPage = 20,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id_or_reference}/components/{component_id}/usages.json"),
            [new TemplateParam("subscription_id_or_reference", subscriptionIdOrReference),
                new TemplateParam("component_id", componentId)],
            [new Param("since_id", sinceId),
                new Param("max_id", maxId),
                new Param("since_date", sinceDate?.ToDate()),
                new Param("until_date", untilDate?.ToDate()),
                new Param("page", page),
                new Param("per_page", perPage)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<IReadOnlyList<UsageResponse>>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Preview Allocations
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="AllocationPreviewResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="PreviewAllocationsError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Previews a potential subscription's <b>quantity-based</b> or <b>on/off</b> component allocation in the middle of the current billing period.  This is useful if you want users to be able to see the effect of a component operation before actually doing it.
    /// <para>
    /// ## Fine-grained Component Control: Use with multiple <c>upgrade_charge</c>s or <c>downgrade_credits</c>
    /// </para>
    /// <para>
    /// When the allocation uses multiple different types of <c>upgrade_charge</c>s or <c>downgrade_credit</c>s, the Allocation is viewed as an Allocation which uses "Fine-Grained Component Control". As a result, the response will not include <c>direction</c> and <c>proration</c> within the <c>allocation_preview</c>, but at the <c>line_items</c> and <c>allocations</c> level respectfully.
    /// </para>
    /// <para>
    /// See example below for Fine-Grained Component Control response.
    /// </para>
    /// </remarks>
    public Task<AllocationPreviewResponse> PreviewAllocations(int subscriptionId,
        PreviewAllocationsRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/allocations/preview.json"),
            [new TemplateParam("subscription_id", subscriptionId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<AllocationPreviewResponse>(),
            PreviewAllocationsErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Read Subscription Component
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="componentId">The Advanced Billing id of the component. Alternatively, the component's handle prefixed by <c>handle:</c></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SubscriptionComponentResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ReadSubscriptionComponentError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Returns information for a specific component on a subscription.
    /// </remarks>
    public Task<SubscriptionComponentResponse> ReadSubscriptionComponent(int subscriptionId,
        int componentId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/components/{component_id}.json"),
            [new TemplateParam("subscription_id", subscriptionId), new TemplateParam("component_id", componentId)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<SubscriptionComponentResponse>(),
            ReadSubscriptionComponentErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Event Ingestion
    /// </summary>
    /// <param name="apiHandle">Identifies the Stream for which the event should be published.</param>
    /// <param name="storeUid">If you've attached your own Keen project as an Advanced Billing event data-store, use this parameter to indicate the data-store.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Records a single event for Events-Based Billing.
    /// <para>
    /// ## Documentation
    /// </para>
    /// <para>
    /// Events-Based Billing is an evolved form of metered billing that is based on data-rich events streamed in real-time from your system to Advanced Billing.
    /// </para>
    /// <para>
    /// These events can then be transformed, enriched, or analyzed to form the computed totals of usage charges billed to your customers.
    /// </para>
    /// <para>
    /// This API allows you to stream events into the Advanced Billing data ingestion engine.
    /// </para>
    /// <para>
    /// Learn more about the feature in general in the <see href="https://maxio.zendesk.com/hc/en-us/articles/24260323329805-Events-Based-Billing-Overview">Events-Based Billing help docs</see>.
    /// </para>
    /// <para>
    /// ## Record Event
    /// </para>
    /// <para>
    /// Use this endpoint to record a single event.
    /// </para>
    /// <para>
    /// *Note: this endpoint differs from the standard Chargify API endpoints in that the URL subdomain will be <c>events</c> and your site subdomain will be included in the URL path. For example:*
    /// </para>
    /// <code>
    /// https://events.chargify.com/my-site-subdomain/events/my-stream-api-handle
    /// </code>
    /// </remarks>
    public Task RecordEvent(string apiHandle,
        string? storeUid,
        EbbEvent? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Ebb("/events/{api_handle}.json"),
            [new TemplateParam("api_handle", apiHandle)],
            [new Param("store_uid", storeUid)],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            VoidResponse.Instance,
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Update Prepaid Usage Allocation Expiration Date
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="componentId">The Advanced Billing id of the component</param>
    /// <param name="allocationId">The Advanced Billing id of the allocation</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="UpdatePrepaidUsageAllocationExpirationDateError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates the expiration date for a prepaid usage allocation. This expiration date can be changed after the fact to allow for extending or shortening the allocation's active window.
    /// <para>
    /// In order to change a prepaid usage allocation's expiration date, a PUT call must be made to the allocation's endpoint with a new expiration date.
    /// </para>
    /// <para>
    /// ## Limitations
    /// </para>
    /// <para>
    /// A few limitations exist when changing an allocation's expiration date:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>An expiration date can only be changed for an allocation that belongs to a price point with expiration interval options explicitly set.</description></item>
    ///   <item><description>An expiration date can be changed towards the future with no limitations.</description></item>
    ///   <item><description>An expiration date can be changed towards the past (essentially expiring it) up to the subscription's current period beginning date.</description></item>
    /// </list>
    /// </remarks>
    public Task UpdatePrepaidUsageAllocationExpirationDate(int subscriptionId,
        int componentId,
        int allocationId,
        UpdateAllocationExpirationDate? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/components/{component_id}/allocations/{allocation_id}.json"),
            [new TemplateParam("subscription_id", subscriptionId),
                new TemplateParam("component_id", componentId),
                new TemplateParam("allocation_id", allocationId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Put,
            JsonRequest.Create(body),
            VoidResponse.Instance,
            UpdatePrepaidUsageAllocationExpirationDateErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);
}
