using System;
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
using Maxio.Errors;
using Maxio.Models;

namespace Maxio.Api;

public sealed class Offers
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Offers(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Archive Offer
    /// </summary>
    /// <param name="offerId">The Chargify id of the offer</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Archives an existing offer. Please provide an <c>offer_id</c> in order to archive the correct item.
    /// </remarks>
    public Task ArchiveOffer(int offerId, RequestOptions? requestOptions = null, CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/offers/{offer_id}/archive.json"),
            [new TemplateParam("offer_id", offerId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Put,
            EmptyBody.Instance,
            VoidResponse.Instance,
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Create Offer
    /// </summary>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="OfferResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateOfferError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates an offer within your Advanced Billing site.
    /// <para>
    /// ## Documentation
    /// </para>
    /// <para>
    /// Offers allow you to package complicated combinations of products, components and coupons into a convenient package which can then be subscribed to just like products.
    /// </para>
    /// <para>
    /// Once an offer is defined it can be used as an alternative to the product when creating subscriptions.
    /// </para>
    /// <para>
    /// Full documentation on how to use offers in the Advanced Billing UI can be located <see href="https://maxio.zendesk.com/hc/en-us/articles/24261295098637-Offers-Overview">here</see>.
    /// </para>
    /// <para>
    /// ## Using a Product Price Point
    /// </para>
    /// <para>
    /// You can optionally pass in a <c>product_price_point_id</c> that corresponds with the <c>product_id</c> and the offer will use that price point. If a <c>product_price_point_id</c> is not passed in, the product's default price point will be used.
    /// </para>
    /// </remarks>
    public Task<OfferResponse> CreateOffer(CreateOfferRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/offers.json"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<OfferResponse>(),
            CreateOfferErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List Offers
    /// </summary>
    /// <param name="includeArchived">Include archived products. Use in query: <c>include_archived=true</c>.</param>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="perPage">This parameter indicates how many records to fetch in each request. Default value is 20. The maximum allowed values is 200; any per_page value over 200 will be changed to 200. Use in query <c>per_page=200</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListOffersResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ListOffersError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists offers for a site.
    /// </remarks>
    public Task<ListOffersResponse> ListOffers(bool? includeArchived,
        int? page = 1,
        int? perPage = 20,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/offers.json"),
            [],
            [new Param("page", page),
                new Param("per_page", perPage),
                new Param("include_archived", includeArchived)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListOffersResponse>(),
            ListOffersErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Read Offer
    /// </summary>
    /// <param name="offerId">The Chargify id of the offer</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="OfferResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Returns a specific offer's attributes. This is different from listing all offers for a site, as it requires an <c>offer_id</c>.
    /// </remarks>
    public Task<OfferResponse> ReadOffer(int offerId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/offers/{offer_id}.json"),
            [new TemplateParam("offer_id", offerId)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<OfferResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Unarchive Offer
    /// </summary>
    /// <param name="offerId">The Chargify id of the offer</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Unarchives a previously archived offer. Please provide an <c>offer_id</c> in order to unarchive the correct item.
    /// </remarks>
    public Task UnarchiveOffer(int offerId, RequestOptions? requestOptions = null, CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/offers/{offer_id}/unarchive.json"),
            [new TemplateParam("offer_id", offerId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Put,
            EmptyBody.Instance,
            VoidResponse.Instance,
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);
}
