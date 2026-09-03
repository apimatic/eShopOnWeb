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

namespace Maxio.Api;

public sealed class SalesCommissions
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal SalesCommissions(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// List Sales Commission Settings
    /// </summary>
    /// <param name="sellerId">The Chargify id of your seller account</param>
    /// <param name="liveMode">This parameter indicates if records should be fetched from live mode sites. Default value is true.</param>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="perPage">This parameter indicates how many records to fetch in each request. Default value is 100.</param>
    /// <param name="authorization">For authorization use user API key. See details <see href="https://developers.chargify.com/docs/developer-docs/ZG9jOjMyNzk5NTg0-2020-04-20-new-api-authentication">here</see>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="IReadOnlyList{T}"/> of <see cref="SaleRepSettings"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists subscriptions with associated sales reps.
    /// <para>
    /// ## Modified Authentication Process
    /// </para>
    /// <para>
    /// The Sales Commission API differs from other Chargify API endpoints. This resource is associated with the seller itself. Up to now all available resources were at the level of the site, therefore creating the API Key per site was a sufficient solution. To share resources at the seller level, a new authentication method was introduced, which is user authentication. Creating an API Key for a user is a required step to correctly use the Sales Commission API, more details <see href="https://developers.chargify.com/docs/developer-docs/ZG9jOjMyNzk5NTg0-2020-04-20-new-api-authentication">here</see>.
    /// </para>
    /// <para>
    /// Access to the Sales Commission API endpoints is available to users with financial access, where the seller has the Advanced Analytics component enabled. For further information on getting access to Advanced Analytics contact Maxio support.
    /// </para>
    /// <para>
    /// &gt; Note: The request is at seller level, it means <c>&lt;&lt;subdomain&gt;&gt;</c> variable will be replaced by <c>app</c>.
    /// </para>
    /// </remarks>
    public Task<IReadOnlyList<SaleRepSettings>> ListSalesCommissionSettings(string sellerId,
        bool? liveMode,
        int? page = 1,
        int? perPage = 100,
        string? authorization = "Bearer <<apiKey>>",
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/sellers/{seller_id}/sales_commission_settings.json"),
            [new TemplateParam("seller_id", sellerId)],
            [new Param("live_mode", liveMode), new Param("page", page), new Param("per_page", perPage)],
            [new HeaderParam("Authorization", authorization)],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<IReadOnlyList<SaleRepSettings>>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List Sales Reps
    /// </summary>
    /// <param name="sellerId">The Chargify id of your seller account</param>
    /// <param name="liveMode">This parameter indicates if records should be fetched from live mode sites. Default value is true.</param>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="perPage">This parameter indicates how many records to fetch in each request. Default value is 100.</param>
    /// <param name="authorization">For authorization use user API key. See details <see href="https://developers.chargify.com/docs/developer-docs/ZG9jOjMyNzk5NTg0-2020-04-20-new-api-authentication">here</see>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="IReadOnlyList{T}"/> of <see cref="ListSaleRepItem"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists sales reps with details.
    /// <para>
    /// ## Modified Authentication Process
    /// </para>
    /// <para>
    /// The Sales Commission API differs from other Chargify API endpoints. This resource is associated with the seller itself. Up to now all available resources were at the level of the site, therefore creating the API Key per site was a sufficient solution. To share resources at the seller level, a new authentication method was introduced, which is user authentication. Creating an API Key for a user is a required step to correctly use the Sales Commission API, more details <see href="https://developers.chargify.com/docs/developer-docs/ZG9jOjMyNzk5NTg0-2020-04-20-new-api-authentication">here</see>.
    /// </para>
    /// <para>
    /// Access to the Sales Commission API endpoints is available to users with financial access, where the seller has the Advanced Analytics component enabled. For further information on getting access to Advanced Analytics contact Maxio support.
    /// </para>
    /// <para>
    /// &gt; Note: The request is at seller level, it means <c>&lt;&lt;subdomain&gt;&gt;</c> variable will be replaced by <c>app</c>.
    /// </para>
    /// </remarks>
    public Task<IReadOnlyList<ListSaleRepItem>> ListSalesReps(string sellerId,
        bool? liveMode,
        int? page = 1,
        int? perPage = 100,
        string? authorization = "Bearer <<apiKey>>",
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/sellers/{seller_id}/sales_reps.json"),
            [new TemplateParam("seller_id", sellerId)],
            [new Param("live_mode", liveMode), new Param("page", page), new Param("per_page", perPage)],
            [new HeaderParam("Authorization", authorization)],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<IReadOnlyList<ListSaleRepItem>>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Read Sales Rep
    /// </summary>
    /// <param name="sellerId">The Chargify id of your seller account</param>
    /// <param name="salesRepId">The Advanced Billing id of sales rep.</param>
    /// <param name="liveMode">This parameter indicates if records should be fetched from live mode sites. Default value is true.</param>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="perPage">This parameter indicates how many records to fetch in each request. Default value is 100.</param>
    /// <param name="authorization">For authorization use user API key. See details <see href="https://developers.chargify.com/docs/developer-docs/ZG9jOjMyNzk5NTg0-2020-04-20-new-api-authentication">here</see>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SaleRep"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Returns a sales rep and attached subscription details.
    /// <para>
    /// ## Modified Authentication Process
    /// </para>
    /// <para>
    /// The Sales Commission API differs from other Chargify API endpoints. This resource is associated with the seller itself. Up to now all available resources were at the level of the site, therefore creating the API Key per site was a sufficient solution. To share resources at the seller level, a new authentication method was introduced, which is user authentication. Creating an API Key for a user is a required step to correctly use the Sales Commission API, more details <see href="https://developers.chargify.com/docs/developer-docs/ZG9jOjMyNzk5NTg0-2020-04-20-new-api-authentication">here</see>.
    /// </para>
    /// <para>
    /// Access to the Sales Commission API endpoints is available to users with financial access, where the seller has the Advanced Analytics component enabled. For further information on getting access to Advanced Analytics contact Maxio support.
    /// </para>
    /// <para>
    /// &gt; Note: The request is at seller level, it means <c>&lt;&lt;subdomain&gt;&gt;</c> variable will be replaced by <c>app</c>.
    /// </para>
    /// </remarks>
    public Task<SaleRep> ReadSalesRep(string sellerId,
        string salesRepId,
        bool? liveMode,
        int? page = 1,
        int? perPage = 100,
        string? authorization = "Bearer <<apiKey>>",
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/sellers/{seller_id}/sales_reps/{sales_rep_id}.json"),
            [new TemplateParam("seller_id", sellerId), new TemplateParam("sales_rep_id", salesRepId)],
            [new Param("live_mode", liveMode), new Param("page", page), new Param("per_page", perPage)],
            [new HeaderParam("Authorization", authorization)],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<SaleRep>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);
}
