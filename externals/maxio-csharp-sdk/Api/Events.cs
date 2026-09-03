using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Maxio.Core;
using Maxio.Core.Authentication;
using Maxio.Core.ErrorResponse;
using Maxio.Core.Exceptions;
using Maxio.Core.Models;
using Maxio.Core.Request;
using Maxio.Core.Response;
using Maxio.Models;
using Maxio.Models.Enums;

namespace Maxio.Api;

public sealed class Events
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Events(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// List Events
    /// </summary>
    /// <param name="sinceId">Returns events with an id greater than or equal to the one specified.</param>
    /// <param name="maxId">Returns events with an id less than or equal to the one specified.</param>
    /// <param name="direction">The sort direction of the returned events.</param>
    /// <param name="filter">You can pass multiple event keys after comma. Use in query <c>filter=signup_success,payment_success</c>.</param>
    /// <param name="dateField">The type of filter you would like to apply to your search.</param>
    /// <param name="startDate">The start date (format YYYY-MM-DD) with which to filter the date_field. Returns components with a timestamp at or after midnight (12:00:00 AM) in your site’s time zone on the date specified.</param>
    /// <param name="endDate">The end date (format YYYY-MM-DD) with which to filter the date_field. Returns components with a timestamp up to and including 11:59:59PM in your site’s time zone on the date specified.</param>
    /// <param name="startDatetime">The start date and time (format YYYY-MM-DD HH:MM:SS) with which to filter the date_field. Returns components with a timestamp at or after exact time provided in query. You can specify timezone in query - otherwise your site's time zone will be used. If provided, this parameter will be used instead of start_date.</param>
    /// <param name="endDatetime">The end date and time (format YYYY-MM-DD HH:MM:SS) with which to filter the date_field. Returns components with a timestamp at or before exact time provided in query. You can specify timezone in query - otherwise your site's time zone will be used. If provided, this parameter will be used instead of end_date.</param>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="perPage">This parameter indicates how many records to fetch in each request. Default value is 20. The maximum allowed values is 200; any per_page value over 200 will be changed to 200. Use in query <c>per_page=200</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="IReadOnlyList{T}"/> of <see cref="EventResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists events for a site.
    /// <para>
    /// ## Events Intro
    /// </para>
    /// <para>
    /// Advanced Billing Events include various activity that happens around a Site. This information is <b>especially</b> useful to track down issues that arise when subscriptions are not created due to errors.
    /// </para>
    /// <para>
    /// Within the Advanced Billing UI, "Events" are referred to as "Site Activity".  See the <see href="https://maxio.zendesk.com/hc/en-us/articles/24250671733517-Site-Activity">Site Activity</see> article in the product documentation for details on how to record view Events / Site Activty in the Advanced Billing UI.
    /// </para>
    /// <para>
    /// If you’re using the <see href="page:help/announcements/2026-announcements#new-catalog-experience-and-terminology">enhanced Catalog experience</see>, you’ll see updated naming in webhook events and messages.
    /// </para>
    /// <para>
    /// Event name changes:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>subscription_product_change → subscription_plan_change</description></item>
    ///   <item><description>component_allocation_change → allocation_change</description></item>
    ///   <item><description>component_billing_date_change → product_billing_date_change</description></item>
    /// </list>
    /// <para>
    /// Message updates:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>“Plan changed on Subscription from previous plan to new plan”</description></item>
    ///   <item><description>“Successful payment for allocation changes to Product on Subscription”</description></item>
    ///   <item><description>“Failed payment for allocation changes to Product on Subscription”</description></item>
    /// </list>
    /// <para>
    /// ## List Events for a Site
    /// </para>
    /// <para>
    /// This method will retrieve a list of events for a site. Use query string filters to narrow down results. You may use the <c>key</c> filter as part of your query string to narrow down results.
    /// </para>
    /// <para>
    /// ### Legacy Filters
    /// </para>
    /// <para>
    /// The following keys are no longer supported.
    /// </para>
    /// <list type="bullet">
    ///   <item><description><c>payment_failure_recreated</c></description></item>
    ///   <item><description><c>payment_success_recreated</c></description></item>
    ///   <item><description><c>renewal_failure_recreated</c></description></item>
    ///   <item><description><c>renewal_success_recreated</c></description></item>
    ///   <item><description><c>zferral_revenue_post_failure</c> - (Specific to the deprecated Zferral integration)</description></item>
    ///   <item><description><c>zferral_revenue_post_success</c> - (Specific to the deprecated Zferral integration)</description></item>
    /// </list>
    /// <para>
    /// ## Event Key
    /// The event type is identified by the key property. You can check supported keys <see href="$m/Event%20Key">here</see>.
    /// </para>
    /// <para>
    /// ## Event Specific Data
    /// </para>
    /// <para>
    /// Different event types may include additional data in <c>event_specific_data</c> property.
    /// While some events share the same schema for <c>event_specific_data</c>, others may not include it at all.
    /// For precise mappings from key to event_specific_data, refer to <see href="$m/Event">Event</see>.
    /// </para>
    /// <example>
    /// Here’s an example event for the <c>subscription_product_change</c> event:
    /// <code>
    /// {
    ///     "event": {
    ///         "id": 351,
    ///         "key": "subscription_product_change",
    ///         "message": "Product changed on Mark Alan's subscription from 'Basic' to 'Pro'",
    ///         "subscription_id": 205,
    ///         "event_specific_data": {
    ///             "new_product_id": 3,
    ///             "previous_product_id": 2
    ///         },
    ///         "created_at": "2012-01-30T10:43:31-05:00"
    ///     }
    /// }
    /// </code>
    /// <para>
    /// Here’s an example event for the <c>subscription_state_change</c> event:
    /// </para>
    /// <code>
    ///  {
    ///      "event": {
    ///          "id": 353,
    ///          "key": "subscription_state_change",
    ///          "message": "State changed on Mark Alan's subscription to Pro from trialing to active",
    ///          "subscription_id": 205,
    ///          "event_specific_data": {
    ///              "new_subscription_state": "active",
    ///              "previous_subscription_state": "trialing"
    ///          },
    ///          "created_at": "2012-01-30T10:43:33-05:00"
    ///      }
    ///  }
    /// </code>
    /// </example>
    /// </remarks>
    public Task<IReadOnlyList<EventResponse>> ListEvents(long? sinceId,
        long? maxId,
        Direction? direction,
        IReadOnlyList<EventKey>? filter,
        ListEventsDateField? dateField,
        string? startDate,
        string? endDate,
        string? startDatetime,
        string? endDatetime,
        int? page = 1,
        int? perPage = 20,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/events.json"),
            [],
            [new Param("page", page),
                new Param("per_page", perPage),
                new Param("since_id", sinceId),
                new Param("max_id", maxId),
                new Param("direction", direction),
                new Param("filter", filter),
                new Param("date_field", dateField),
                new Param("start_date", startDate),
                new Param("end_date", endDate),
                new Param("start_datetime", startDatetime),
                new Param("end_datetime", endDatetime)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<IReadOnlyList<EventResponse>>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List Events for Subscription
    /// </summary>
    /// <param name="subscriptionId">The Chargify id of the subscription.</param>
    /// <param name="sinceId">Returns events with an id greater than or equal to the one specified.</param>
    /// <param name="maxId">Returns events with an id less than or equal to the one specified.</param>
    /// <param name="direction">The sort direction of the returned events.</param>
    /// <param name="filter">You can pass multiple event keys after comma. Use in query <c>filter=signup_success,payment_success</c>.</param>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="perPage">This parameter indicates how many records to fetch in each request. Default value is 20. The maximum allowed values is 200; any per_page value over 200 will be changed to 200. Use in query <c>per_page=200</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="IReadOnlyList{T}"/> of <see cref="EventResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists events for a subscription.
    /// <para>
    /// ## Event Key
    /// The event type is identified by the key property. You can check supported keys <see href="$m/Event%20Key">here</see>.
    /// </para>
    /// <para>
    /// If you’re using the <see href="page:help/announcements/2026-announcements#new-catalog-experience-and-terminology">enhanced Catalog experience</see>, you’ll see updated naming in webhook events and messages.
    /// </para>
    /// <para>
    /// Event name changes:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>subscription_product_change → subscription_plan_change</description></item>
    ///   <item><description>component_allocation_change → allocation_change</description></item>
    ///   <item><description>component_billing_date_change → product_billing_date_change</description></item>
    /// </list>
    /// <para>
    /// Message updates:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>“Successful payment for allocation changes to Product on Subscription”</description></item>
    ///   <item><description>“Failed payment for allocation changes to Product on Subscription”</description></item>
    ///   <item><description>“Plan changed on Subscription from previous plan to new plan”</description></item>
    /// </list>
    /// <para>
    /// ## Event Specific Data
    /// </para>
    /// <para>
    /// Different event types may include additional data in <c>event_specific_data</c> property.
    /// While some events share the same schema for <c>event_specific_data</c>, others may not include it at all.
    /// For precise mappings from key to event_specific_data, refer to <see href="$m/Event">Event</see>.
    /// </para>
    /// </remarks>
    public Task<IReadOnlyList<EventResponse>> ListSubscriptionEvents(int subscriptionId,
        long? sinceId,
        long? maxId,
        Direction? direction,
        IReadOnlyList<EventKey>? filter,
        int? page = 1,
        int? perPage = 20,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions/{subscription_id}/events.json"),
            [new TemplateParam("subscription_id", subscriptionId)],
            [new Param("page", page),
                new Param("per_page", perPage),
                new Param("since_id", sinceId),
                new Param("max_id", maxId),
                new Param("direction", direction),
                new Param("filter", filter)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<IReadOnlyList<EventResponse>>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Read Total Event Count
    /// </summary>
    /// <param name="sinceId">Returns events with an id greater than or equal to the one specified.</param>
    /// <param name="maxId">Returns events with an id less than or equal to the one specified.</param>
    /// <param name="direction">The sort direction of the returned events.</param>
    /// <param name="filter">You can pass multiple event keys after comma. Use in query <c>filter=signup_success,payment_success</c>.</param>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="perPage">This parameter indicates how many records to fetch in each request. Default value is 20. The maximum allowed values is 200; any per_page value over 200 will be changed to 200. Use in query <c>per_page=200</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="CountResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Returns the total count of events for a given site.
    /// <para>
    /// If you’re using the <see href="page:help/announcements/2026-announcements#new-catalog-experience-and-terminology">enhanced Catalog experience</see>, you’ll see updated naming in webhook events and messages.
    /// </para>
    /// <para>
    /// Event name changes:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>subscription_product_change → subscription_plan_change</description></item>
    ///   <item><description>component_allocation_change → allocation_change</description></item>
    ///   <item><description>component_billing_date_change → product_billing_date_change</description></item>
    /// </list>
    /// <para>
    /// Message updates:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>“Successful payment for allocation changes to Product on Subscription”</description></item>
    ///   <item><description>“Failed payment for allocation changes to Product on Subscription”</description></item>
    ///   <item><description>“Plan changed on Subscription from previous plan to new plan”</description></item>
    /// </list>
    /// </remarks>
    public Task<CountResponse> ReadEventsCount(long? sinceId,
        long? maxId,
        Direction? direction,
        IReadOnlyList<EventKey>? filter,
        int? page = 1,
        int? perPage = 20,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/events/count.json"),
            [],
            [new Param("page", page),
                new Param("per_page", perPage),
                new Param("since_id", sinceId),
                new Param("max_id", maxId),
                new Param("direction", direction),
                new Param("filter", filter)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<CountResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);
}
