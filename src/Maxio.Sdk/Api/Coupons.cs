using System;
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
using Maxio.Errors;
using Maxio.Models;

namespace Maxio.Api;

public sealed class Coupons
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Coupons(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Archive Coupon
    /// </summary>
    /// <param name="productFamilyId">The Advanced Billing id of the product family to which the coupon belongs</param>
    /// <param name="couponId">The Advanced Billing id of the coupon</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="CouponResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Archives a coupon, making it unavailable for future use while remaining active on existing subscriptions.
    /// Archiving makes that Coupon unavailable for future use, but allows it to remain attached and functional on existing Subscriptions that are using it.
    /// The <c>archived_at</c> date and time will be assigned.
    /// </remarks>
    public Task<CouponResponse> ArchiveCoupon(int productFamilyId,
        int couponId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/product_families/{product_family_id}/coupons/{coupon_id}.json"),
            [new TemplateParam("product_family_id", productFamilyId), new TemplateParam("coupon_id", couponId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            JsonResponse.Create<CouponResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Create Coupon
    /// </summary>
    /// <param name="productFamilyId">The Advanced Billing id of the product family to which the coupon belongs</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="CouponResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateCouponError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates a coupon under the specified product family.
    /// <para>
    /// You can create either a flat amount coupon, by specifying <c>amount_in_cents</c>, or percentage coupon by specifying <c>percentage</c>.
    /// </para>
    /// <para>
    /// See <see href="https://maxio.zendesk.com/hc/en-us/articles/24261259337101-Coupons-and-Subscriptions">Apply Coupons to Subscriptions</see> for information on applying a coupon to a subscription in the Advanced Billing UI.
    /// </para>
    /// </remarks>
    public Task<CouponResponse> CreateCoupon(int productFamilyId,
        CouponRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/product_families/{product_family_id}/coupons.json"),
            [new TemplateParam("product_family_id", productFamilyId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<CouponResponse>(),
            CreateCouponErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Create Coupon Subcodes
    /// </summary>
    /// <param name="couponId">The Advanced Billing id of the coupon</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="CouponSubcodesResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates subcodes for an existing coupon.
    /// <para>
    /// ## Coupon Subcodes Intro
    /// </para>
    /// <para>
    /// Coupon Subcodes allow you to create a set of unique codes that allow you to expand the use of one coupon.
    /// </para>
    /// <para>
    /// For example:
    /// </para>
    /// <para>
    /// Master Coupon Code:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>SPRING2020</description></item>
    /// </list>
    /// <para>
    /// Coupon Subcodes:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>SPRING90210</description></item>
    ///   <item><description>DP80302</description></item>
    ///   <item><description>SPRINGBALTIMORE</description></item>
    /// </list>
    /// <para>
    /// Coupon subcodes can be administered in the Admin Interface or via the API.
    /// </para>
    /// <para>
    /// When creating a coupon subcode, you must specify a coupon to attach it to using the coupon_id. Valid coupon subcodes are all capital letters, contain only letters and numbers, and do not have any spaces. Lowercase letters will be capitalized before the subcode is created.
    /// </para>
    /// <para>
    /// ## Coupon Subcodes Documentation
    /// </para>
    /// <para>
    /// Full documentation on how to create coupon subcodes in the Advanced Billing UI can be located <see href="https://maxio.zendesk.com/hc/en-us/articles/24261208729229-Coupon-Codes">here</see>.
    /// </para>
    /// <para>
    /// Additionally, for documentation on how to apply a coupon to a Subscription within the Advanced Billing UI, see our documentation <see href="https://maxio.zendesk.com/hc/en-us/articles/24261259337101-Coupons-and-Subscriptions">here</see>.
    /// </para>
    /// <para>
    /// ## Create Coupon Subcode
    /// </para>
    /// <para>
    /// This request allows you to create specific subcodes underneath an existing coupon code.
    /// </para>
    /// <para>
    /// *Note*: If you are using any of the allowed special characters ("%", "@", "+", "-", "_", and "."), you must encode them for use in the URL.
    /// </para>
    /// <para>
    ///     % to %25
    ///     @ to %40
    ///     + to %2B
    ///     - to %2D
    ///     _ to %5F
    ///     . to %2E
    /// </para>
    /// <para>
    /// So, if the coupon subcode is <c>20%OFF</c>, the URL to delete this coupon subcode would be: <c>https://&lt;subdomain&gt;.chargify.com/coupons/567/codes/20%25OFF.&lt;format&gt;</c>.
    /// </para>
    /// </remarks>
    public Task<CouponSubcodesResponse> CreateCouponSubcodes(int couponId,
        CouponSubcodes? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/coupons/{coupon_id}/codes.json"),
            [new TemplateParam("coupon_id", couponId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<CouponSubcodesResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Create / Update Currency Prices
    /// </summary>
    /// <param name="couponId">The Advanced Billing id of the coupon</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="CouponCurrencyResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateOrUpdateCouponCurrencyPricesError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates and/or updates currency prices for an existing coupon. Multiple prices can be created or updated in a single request but each of the currencies must be defined on the site level already and the coupon must be an amount-based coupon, not percentage.
    /// <para>
    /// Currency pricing for coupons must mirror the setup of the primary coupon pricing - if the primary coupon is percentage based, you will not be able to define pricing in non-primary currencies.
    /// </para>
    /// </remarks>
    public Task<CouponCurrencyResponse> CreateOrUpdateCouponCurrencyPrices(int couponId,
        CouponCurrencyRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/coupons/{coupon_id}/currency_prices.json"),
            [new TemplateParam("coupon_id", couponId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Put,
            JsonRequest.Create(body),
            JsonResponse.Create<CouponCurrencyResponse>(),
            CreateOrUpdateCouponCurrencyPricesErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Delete Coupon Subcode
    /// </summary>
    /// <param name="couponId">The Advanced Billing id of the coupon to which the subcode belongs</param>
    /// <param name="subcode">The subcode of the coupon</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="DeleteCouponSubcodeError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Deletes a specific subcode from a coupon.
    /// <example>
    /// Given a coupon with an ID of 567, and a coupon subcode of 20OFF, the URL to <c>DELETE</c> this coupon subcode would be:
    /// <code>
    /// http://subdomain.chargify.com/coupons/567/codes/20OFF.&lt;format&gt;
    /// </code>
    /// <para>
    /// Note: If you are using any of the allowed special characters (“%”, “@”, “+”, “-”, “_”, and “.”), you must encode them for use in the URL.
    /// </para>
    /// <para>
    /// | Special character | Encoding |
    /// |-------------------|----------|
    /// | %                 | %25      |
    /// | @                 | %40      |
    /// | +                 | %2B      |
    /// | –                 | %2D      |
    /// | _                 | %5F      |
    /// | .                 | %2E      |
    /// </para>
    /// <para>
    /// ## Percent Encoding Example
    /// </para>
    /// <para>
    /// Or if the coupon subcode is 20%OFF, the URL to delete this coupon subcode would be: @https://&lt;subdomain&gt;.chargify.com/coupons/567/codes/20%25OFF.&lt;format&gt;.
    /// </para>
    /// </example>
    /// </remarks>
    public Task DeleteCouponSubcode(int couponId,
        string subcode,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/coupons/{coupon_id}/codes/{subcode}.json"),
            [new TemplateParam("coupon_id", couponId), new TemplateParam("subcode", subcode)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            VoidResponse.Instance,
            DeleteCouponSubcodeErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Find Coupon
    /// </summary>
    /// <param name="productFamilyId">The Advanced Billing id of the product family to which the coupon belongs</param>
    /// <param name="code">The code of the coupon</param>
    /// <param name="currencyPrices">(Optional) If you have defined multiple currencies at the site level, you can pass <c>?currency_prices=true</c> to include an array of currency price data in the response.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="CouponResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Searches for a coupon by code, returning a 404 if no coupon is found. By passing a code parameter, the find will attempt to locate a coupon that matches that code.
    /// <para>
    /// If you have more than one product family and if the coupon you are trying to find does not belong to the default product family in your site, then you will need to specify (either in the url or as a query string param) the product family id.
    /// </para>
    /// </remarks>
    public Task<CouponResponse> FindCoupon(int? productFamilyId,
        string? code,
        bool? currencyPrices,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/coupons/find.json"),
            [],
            [new Param("product_family_id", productFamilyId),
                new Param("code", code),
                new Param("currency_prices", currencyPrices)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<CouponResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List Coupon Subcodes
    /// </summary>
    /// <param name="couponId">The Advanced Billing id of the coupon</param>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="perPage">This parameter indicates how many records to fetch in each request. Default value is 20. The maximum allowed values is 200; any per_page value over 200 will be changed to 200. Use in query <c>per_page=200</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="CouponSubcodes"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists the subcodes attached to a coupon.
    /// </remarks>
    public Task<CouponSubcodes> ListCouponSubcodes(int couponId,
        int? page = 1,
        int? perPage = 20,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/coupons/{coupon_id}/codes.json"),
            [new TemplateParam("coupon_id", couponId)],
            [new Param("page", page), new Param("per_page", perPage)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<CouponSubcodes>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List Coupons
    /// </summary>
    /// <param name="filter">Filter to use for List Coupons operations</param>
    /// <param name="currencyPrices">(Optional) If you have defined multiple currencies at the site level, you can pass <c>?currency_prices=true</c> to include an array of currency price data in the response. Use in query <c>currency_prices=true</c>.</param>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="perPage">This parameter indicates how many records to fetch in each request. Default value is 30. The maximum allowed values is 200; any per_page value over 200 will be changed to 200. Use in query <c>per_page=200</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="IReadOnlyList{T}"/> of <see cref="CouponResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists coupons for a site.
    /// </remarks>
    public Task<IReadOnlyList<CouponResponse>> ListCoupons(ListCouponsFilter? filter,
        bool? currencyPrices,
        int? page = 1,
        int? perPage = 30,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/coupons.json"),
            [],
            [new Param("page", page),
                new Param("per_page", perPage),
                new Param("filter", filter),
                new Param("currency_prices", currencyPrices)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<IReadOnlyList<CouponResponse>>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List Coupons for Product Family
    /// </summary>
    /// <param name="productFamilyId">The Advanced Billing id of the product family to which the coupon belongs</param>
    /// <param name="filter">Filter to use for List Coupons operations</param>
    /// <param name="currencyPrices">(Optional) If you have defined multiple currencies at the site level, you can pass <c>?currency_prices=true</c> to include an array of currency price data in the response. Use in query <c>currency_prices=true</c>.</param>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="perPage">This parameter indicates how many records to fetch in each request. Default value is 30. The maximum allowed values is 200; any per_page value over 200 will be changed to 200. Use in query <c>per_page=200</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="IReadOnlyList{T}"/> of <see cref="CouponResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists coupons for a specific product family in a site.
    /// </remarks>
    public Task<IReadOnlyList<CouponResponse>> ListCouponsForProductFamily(int productFamilyId,
        ListCouponsFilter? filter,
        bool? currencyPrices,
        int? page = 1,
        int? perPage = 30,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/product_families/{product_family_id}/coupons.json"),
            [new TemplateParam("product_family_id", productFamilyId)],
            [new Param("page", page),
                new Param("per_page", perPage),
                new Param("filter", filter),
                new Param("currency_prices", currencyPrices)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<IReadOnlyList<CouponResponse>>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Read Coupon
    /// </summary>
    /// <param name="productFamilyId">The Advanced Billing id of the product family to which the coupon belongs</param>
    /// <param name="couponId">The Advanced Billing id of the coupon</param>
    /// <param name="currencyPrices">(Optional) If you have defined multiple currencies at the site level, you can pass <c>?currency_prices=true</c> to include an array of currency price data in the response.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="CouponResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Returns a coupon by its Advanced Billing-assigned ID. You must identify the Coupon in this call by the ID parameter that Advanced Billing assigns.
    /// If instead you would like to find a Coupon using a Coupon code, see the Coupon Find method.
    /// <para>
    /// If the coupon is set to <c>use_site_exchange_rate: true</c>, it will return pricing based on the current exchange rate. If the flag is set to false, it will return all of the defined prices for each currency.
    /// </para>
    /// </remarks>
    public Task<CouponResponse> ReadCoupon(int productFamilyId,
        int couponId,
        bool? currencyPrices,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/product_families/{product_family_id}/coupons/{coupon_id}.json"),
            [new TemplateParam("product_family_id", productFamilyId), new TemplateParam("coupon_id", couponId)],
            [new Param("currency_prices", currencyPrices)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<CouponResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List Coupon Usages
    /// </summary>
    /// <param name="productFamilyId">The Advanced Billing id of the product family to which the coupon belongs.</param>
    /// <param name="couponId">The Advanced Billing id of the coupon.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="IReadOnlyList{T}"/> of <see cref="CouponUsage"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists coupon usage details, one entry per product.
    /// </remarks>
    public Task<IReadOnlyList<CouponUsage>> ReadCouponUsage(int productFamilyId,
        int couponId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/product_families/{product_family_id}/coupons/{coupon_id}/usage.json"),
            [new TemplateParam("product_family_id", productFamilyId), new TemplateParam("coupon_id", couponId)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<IReadOnlyList<CouponUsage>>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Update Coupon
    /// </summary>
    /// <param name="productFamilyId">The Advanced Billing id of the product family to which the coupon belongs</param>
    /// <param name="couponId">The Advanced Billing id of the coupon</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="CouponResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="UpdateCouponError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates a coupon.
    /// <para>
    /// You can restrict a coupon to only apply to specific products / components by optionally passing in hashes of <c>restricted_products</c> and/or <c>restricted_components</c> in the format:
    /// <c>{ "&lt;product/component_id&gt;": boolean_value }</c>
    /// </para>
    /// </remarks>
    public Task<CouponResponse> UpdateCoupon(int productFamilyId,
        int couponId,
        CouponRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/product_families/{product_family_id}/coupons/{coupon_id}.json"),
            [new TemplateParam("product_family_id", productFamilyId), new TemplateParam("coupon_id", couponId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Put,
            JsonRequest.Create(body),
            JsonResponse.Create<CouponResponse>(),
            UpdateCouponErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Update Coupon Subcodes
    /// </summary>
    /// <param name="couponId">The Advanced Billing id of the coupon</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="CouponSubcodesResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates the subcodes for a coupon, replacing all existing subcodes with the new list.
    /// Send an array of new coupon subcodes.
    /// <para>
    /// <b>Note</b>: All current subcodes for that Coupon will be deleted first, and replaced with the list of subcodes sent to this endpoint.
    /// The response will contain:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>The created subcodes,</description></item>
    /// </list>
    /// <list type="bullet">
    ///   <item><description>Subcodes that were not created because they already exist,</description></item>
    /// </list>
    /// <list type="bullet">
    ///   <item><description>Any subcodes not created because they are invalid.</description></item>
    /// </list>
    /// </remarks>
    public Task<CouponSubcodesResponse> UpdateCouponSubcodes(int couponId,
        CouponSubcodes? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/coupons/{coupon_id}/codes.json"),
            [new TemplateParam("coupon_id", couponId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Put,
            JsonRequest.Create(body),
            JsonResponse.Create<CouponSubcodesResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Validate Coupon
    /// </summary>
    /// <param name="code">The code of the coupon</param>
    /// <param name="productFamilyId">The Advanced Billing id of the product family to which the coupon belongs</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="CouponResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ValidateCouponError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Verifies whether a specific coupon code is valid. This method is useful for validating coupon codes that are entered by a customer. If the coupon is found and is valid, the coupon will be returned with a 200 status code.
    /// <para>
    /// If the coupon is invalid, the status code will be 404 and the response will say why it is invalid. If the coupon is valid, the status code will be 200 and the coupon will be returned. The following reasons for invalidity are supported:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>Coupon not found</description></item>
    ///   <item><description>Coupon is invalid</description></item>
    ///   <item><description>Coupon expired</description></item>
    /// </list>
    /// <para>
    /// If you have more than one product family and if the coupon you are validating does not belong to the first product family in your site, then you will need to specify the product family, either in the url or as a query string param. This can be done by supplying the id or the handle in the <c>handle:my-family</c> format.
    /// </para>
    /// <para>
    /// Eg.
    /// </para>
    /// <code>
    /// https://&lt;subdomain&gt;.chargify.com/product_families/handle:&lt;product_family_handle&gt;/coupons/validate.&lt;format&gt;?code=&lt;coupon_code&gt;
    /// </code>
    /// <para>
    /// Or:
    /// </para>
    /// <code>
    /// https://&lt;subdomain&gt;.chargify.com/coupons/validate.&lt;format&gt;?code=&lt;coupon_code&gt;&amp;product_family_id=&lt;id&gt;
    /// </code>
    /// </remarks>
    public Task<CouponResponse> ValidateCoupon(string code,
        int? productFamilyId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/coupons/validate.json"),
            [],
            [new Param("code", code), new Param("product_family_id", productFamilyId)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<CouponResponse>(),
            ValidateCouponErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);
}
