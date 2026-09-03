using System;
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
using Maxio.Models.Enums;

namespace Maxio.Api;

public sealed class Insights
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Insights(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// List MRR Movements
    /// </summary>
    /// <param name="subscriptionId">(Optional) Filter results by subscription.</param>
    /// <param name="direction">Controls the order in which results are returned. Use in query <c>direction=asc</c>.</param>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="perPage">This parameter indicates how many records to fetch in each request. Default value is 10. The maximum allowed values is 50; any per_page value over 50 will be changed to 50. Use in query <c>per_page=20</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListMrrResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists your site's MRR movements.
    /// <para>
    /// ## Understanding MRR movements
    /// </para>
    /// <para>
    /// This endpoint will aid in accessing your site's <see href="https://maxio.zendesk.com/hc/en-us/articles/24285894587021-MRR-Analytics">MRR Report</see> data.
    /// </para>
    /// <para>
    /// Whenever a subscription event occurs that causes your site's MRR to change (such as a signup or upgrade), we record an MRR movement. These records are accessible via the MRR Movements endpoint.
    /// </para>
    /// <para>
    /// Each MRR Movement belongs to a subscription and contains a timestamp, category, and an amount. <c>line_items</c> represent the subscription's product configuration at the time of the movement.
    /// </para>
    /// <para>
    /// ### Plan &amp; Usage Breakouts
    /// </para>
    /// <para>
    /// In the MRR Report UI, we support a setting to <see href="https://maxio.zendesk.com/hc/en-us/articles/24285894587021-MRR-Analytics#displaying-component-based-metered-usage-in-mrr">include or exclude</see> usage revenue. In the MRR APIs, responses include <c>plan</c> and <c>usage</c> breakouts.
    /// </para>
    /// <para>
    /// Plan includes revenue from:
    /// * Products
    /// * Quantity-Based Components
    /// * On/Off Components
    /// </para>
    /// <para>
    /// Usage includes revenue from:
    /// * Metered Components
    /// * Prepaid Usage Components
    /// </para>
    /// </remarks>
    public Task<ListMrrResponse> ListMrrMovements(int? subscriptionId,
        SortingDirection? direction,
        int? page = 1,
        int? perPage = 10,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/mrr_movements.json"),
            [],
            [new Param("subscription_id", subscriptionId),
                new Param("page", page),
                new Param("per_page", perPage),
                new Param("direction", direction)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListMrrResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List MRR per subscription
    /// </summary>
    /// <param name="filter">Filter to use for List MRR per subscription operation</param>
    /// <param name="atTime">Submit a timestamp in ISO8601 format to request MRR for a historic time. Use in query: <c>at_time=2022-01-10T10:00:00-05:00</c>.</param>
    /// <param name="direction">Controls the order in which results are returned. Records are ordered by subscription_id in ascending order by default. Use in query <c>direction=desc</c>.</param>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="perPage">This parameter indicates how many records to fetch in each request. Default value is 20. The maximum allowed values is 200; any per_page value over 200 will be changed to 200. Use in query <c>per_page=200</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SubscriptionMrrResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ListMrrPerSubscriptionError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists your site's current MRR, including plan and usage breakouts split per subscription.
    /// </remarks>
    public Task<SubscriptionMrrResponse> ListMrrPerSubscription(ListMrrFilter? filter,
        string? atTime,
        Direction? direction,
        int? page = 1,
        int? perPage = 20,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/subscriptions_mrr.json"),
            [],
            [new Param("filter", filter),
                new Param("at_time", atTime),
                new Param("page", page),
                new Param("per_page", perPage),
                new Param("direction", direction)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<SubscriptionMrrResponse>(),
            ListMrrPerSubscriptionErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Read MRR
    /// </summary>
    /// <param name="atTime">submit a timestamp in ISO8601 format to request MRR for a historic time.</param>
    /// <param name="subscriptionId">submit the id of a subscription in order to limit results.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="MrrResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Returns your site's current MRR, including plan and usage breakouts.
    /// </remarks>
    public Task<MrrResponse> ReadMrr(DateTimeOffset? atTime,
        int? subscriptionId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/mrr.json"),
            [],
            [new Param("at_time", atTime?.ToIso8601()), new Param("subscription_id", subscriptionId)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<MrrResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Read Site Stats
    /// </summary>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SiteSummary"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Returns basic site-level stats. This API call only answers with JSON responses. An XML version is not provided.
    /// <para>
    /// ## Stats Documentation
    /// </para>
    /// <para>
    /// There currently is not a complimentary matching set of documentation that compliments this endpoint. However, each Site's dashboard will reflect the summary of information provided in the Stats response.
    /// </para>
    /// <code>
    /// https://subdomain.chargify.com/dashboard
    /// </code>
    /// </remarks>
    public Task<SiteSummary> ReadSiteStats(RequestOptions? requestOptions = null, CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/stats.json"),
            [],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<SiteSummary>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);
}
