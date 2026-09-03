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
using Maxio.Models.AnyOf;
using Maxio.Models.Enums;

namespace Maxio.Api;

public sealed class ComponentPricePoints
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal ComponentPricePoints(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Archive Component Price Point
    /// </summary>
    /// <param name="componentId">The id or handle of the component. When using the handle, it must be prefixed with <c>handle:</c>. Example: <c>123</c> for an integer ID, or <c>handle:example-product-handle</c> for a string handle.</param>
    /// <param name="pricePointId">The id or handle of the price point. When using the handle, it must be prefixed with <c>handle:</c>. Example: <c>123</c> for an integer ID, or <c>handle:example-price_point-handle</c> for a string handle.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ComponentPricePointResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ArchiveComponentPricePointError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Archives a component price point. Subscriptions using a price point that has been archived will continue using it until they're moved to another price point.
    /// </remarks>
    public Task<ComponentPricePointResponse> ArchiveComponentPricePoint(ComponentIdModel componentId,
        PricePointIdModel pricePointId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/components/{component_id}/price_points/{price_point_id}.json"),
            [new TemplateParam("component_id", componentId), new TemplateParam("price_point_id", pricePointId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            JsonResponse.Create<ComponentPricePointResponse>(),
            ArchiveComponentPricePointErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Bulk Create Component Price Points
    /// </summary>
    /// <param name="componentId">The Advanced Billing id of the component for which you want to fetch price points.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ComponentPricePointsResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="BulkCreateComponentPricePointsError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates multiple component price points in one request.
    /// </remarks>
    public Task<ComponentPricePointsResponse> BulkCreateComponentPricePoints(string componentId,
        CreateComponentPricePointsRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/components/{component_id}/price_points/bulk.json"),
            [new TemplateParam("component_id", componentId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<ComponentPricePointsResponse>(),
            BulkCreateComponentPricePointsErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Clone Component Price Point
    /// </summary>
    /// <param name="componentId">The id or handle of the component. When using the handle, it must be prefixed with <c>handle:</c>. Example: <c>123</c> for an integer ID, or <c>handle:example-product-handle</c> for a string handle.</param>
    /// <param name="pricePointId">The id or handle of the price point. When using the handle, it must be prefixed with <c>handle:</c>. Example: <c>123</c> for an integer ID, or <c>handle:example-price_point-handle</c> for a string handle.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ComponentPricePointCurrencyOverageResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CloneComponentPricePointError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Clones a component price point. Custom price points (tied to a specific subscription) cannot be cloned. The following attributes are copied from the source price point:
    /// - Pricing scheme
    /// - All price tiers (with starting/ending quantities and unit prices)
    /// - Tax included setting
    /// - Currency prices (if definitive pricing is set)
    /// - Overage pricing (for prepaid usage components)
    /// - Interval settings (if multi-frequency is enabled)
    /// - Event-based billing segments (if applicable)
    /// </remarks>
    public Task<ComponentPricePointCurrencyOverageResponse> CloneComponentPricePoint(ComponentIdModel componentId,
        PricePointIdModel pricePointId,
        CloneComponentPricePointRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/components/{component_id}/price_points/{price_point_id}/clone.json"),
            [new TemplateParam("component_id", componentId), new TemplateParam("price_point_id", pricePointId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<ComponentPricePointCurrencyOverageResponse>(),
            CloneComponentPricePointErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Create Component Price Point
    /// </summary>
    /// <param name="componentId">The Advanced Billing id of the component</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ComponentPricePointResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateComponentPricePointError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates a price point for an existing component.
    /// </remarks>
    public Task<ComponentPricePointResponse> CreateComponentPricePoint(int componentId,
        CreateComponentPricePointRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/components/{component_id}/price_points.json"),
            [new TemplateParam("component_id", componentId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<ComponentPricePointResponse>(),
            CreateComponentPricePointErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Create Currency Prices
    /// </summary>
    /// <param name="pricePointId">The Advanced Billing id of the price point</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ComponentCurrencyPricesResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateCurrencyPricesError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates currency prices for a given currency defined at the site level.
    /// <para>
    /// When creating currency prices, they need to mirror the structure of your primary pricing. For each price level defined on the component price point, there should be a matching price level created in the given currency.
    /// </para>
    /// <para>
    /// Note: Currency Prices are not able to be created for custom price points.
    /// </para>
    /// </remarks>
    public Task<ComponentCurrencyPricesResponse> CreateCurrencyPrices(int pricePointId,
        CreateCurrencyPricesRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/price_points/{price_point_id}/currency_prices.json"),
            [new TemplateParam("price_point_id", pricePointId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<ComponentCurrencyPricesResponse>(),
            CreateCurrencyPricesErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List All Components Price Points
    /// </summary>
    /// <param name="include">Allows including additional data in the response. Use in query: <c>include=currency_prices</c>.</param>
    /// <param name="direction">Controls the order in which results are returned. Use in query <c>direction=asc</c>.</param>
    /// <param name="filter">Filter to use for List PricePoints operations</param>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="perPage">This parameter indicates how many records to fetch in each request. Default value is 20. The maximum allowed values is 200; any per_page value over 200 will be changed to 200. Use in query <c>per_page=200</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListComponentsPricePointsResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ListAllComponentPricePointsError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists all component price points belonging to a site.
    /// </remarks>
    public Task<ListComponentsPricePointsResponse> ListAllComponentPricePoints(ListComponentsPricePointsInclude? include,
        SortingDirection? direction,
        ListPricePointsFilter? filter,
        int? page = 1,
        int? perPage = 20,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/components_price_points.json"),
            [],
            [new Param("include", include),
                new Param("page", page),
                new Param("per_page", perPage),
                new Param("direction", direction),
                new Param("filter", filter)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListComponentsPricePointsResponse>(),
            ListAllComponentPricePointsErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List Component Price Points
    /// </summary>
    /// <param name="componentId">The Advanced Billing id of the component</param>
    /// <param name="currencyPrices">Include an array of currency price data.</param>
    /// <param name="filterType">Use in query: <c>filter[type]=catalog,default</c>.</param>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="perPage">This parameter indicates how many records to fetch in each request. Default value is 20. The maximum allowed values is 200; any per_page value over 200 will be changed to 200. Use in query <c>per_page=200</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ComponentPricePointsResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists the price points associated with a component.
    /// <para>
    /// You may specify the component by using either the numeric id or the <c>handle:gold</c> syntax.
    /// </para>
    /// <para>
    /// If the price point is set to <c>use_site_exchange_rate: true</c>, it will return pricing based on the current exchange rate. If the flag is set to false, it will return all of the defined prices for each currency.
    /// </para>
    /// </remarks>
    public Task<ComponentPricePointsResponse> ListComponentPricePoints(int componentId,
        bool? currencyPrices,
        IReadOnlyList<PricePointType>? filterType,
        int? page = 1,
        int? perPage = 20,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/components/{component_id}/price_points.json"),
            [new TemplateParam("component_id", componentId)],
            [new Param("currency_prices", currencyPrices),
                new Param("page", page),
                new Param("per_page", perPage),
                new Param("filter[type]", filterType)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ComponentPricePointsResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Promote Price Point to Default
    /// </summary>
    /// <param name="componentId">The Advanced Billing id of the component to which the price point belongs</param>
    /// <param name="pricePointId">The Advanced Billing id of the price point</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ComponentResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Sets a new default price point for the component. This new default will apply to all new subscriptions going forward - existing subscriptions will remain on their current price point.
    /// <para>
    /// See <see href="https://maxio.zendesk.com/hc/en-us/articles/24261191737101-Price-Points-Components">Price Points Documentation</see> for more information on price points and moving subscriptions between price points.
    /// </para>
    /// <para>
    /// Note: Custom price points are not able to be set as the default for a component.
    /// </para>
    /// </remarks>
    public Task<ComponentResponse> PromoteComponentPricePointToDefault(int componentId,
        int pricePointId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/components/{component_id}/price_points/{price_point_id}/default.json"),
            [new TemplateParam("component_id", componentId), new TemplateParam("price_point_id", pricePointId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Put,
            EmptyBody.Instance,
            JsonResponse.Create<ComponentResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Read Component Price Point
    /// </summary>
    /// <param name="componentId">The id or handle of the component. When using the handle, it must be prefixed with <c>handle:</c>. Example: <c>123</c> for an integer ID, or <c>handle:example-product-handle</c> for a string handle.</param>
    /// <param name="pricePointId">The id or handle of the price point. When using the handle, it must be prefixed with <c>handle:</c>. Example: <c>123</c> for an integer ID, or <c>handle:example-price_point-handle</c> for a string handle.</param>
    /// <param name="currencyPrices">Include an array of currency price data.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ComponentPricePointCurrencyOverageResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Returns details for a specific component price point. You can achieve this by using either the component price point ID or handle.
    /// </remarks>
    public Task<ComponentPricePointCurrencyOverageResponse> ReadComponentPricePoint(ComponentIdModel componentId,
        PricePointIdModel pricePointId,
        bool? currencyPrices,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/components/{component_id}/price_points/{price_point_id}.json"),
            [new TemplateParam("component_id", componentId), new TemplateParam("price_point_id", pricePointId)],
            [new Param("currency_prices", currencyPrices)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ComponentPricePointCurrencyOverageResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Unarchive Component Price Point
    /// </summary>
    /// <param name="componentId">The Advanced Billing id of the component to which the price point belongs</param>
    /// <param name="pricePointId">The Advanced Billing id of the price point</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ComponentPricePointResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Unarchives a component price point.
    /// </remarks>
    public Task<ComponentPricePointResponse> UnarchiveComponentPricePoint(int componentId,
        int pricePointId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/components/{component_id}/price_points/{price_point_id}/unarchive.json"),
            [new TemplateParam("component_id", componentId), new TemplateParam("price_point_id", pricePointId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Put,
            EmptyBody.Instance,
            JsonResponse.Create<ComponentPricePointResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Update Component Price Point
    /// </summary>
    /// <param name="componentId">The id or handle of the component. When using the handle, it must be prefixed with <c>handle:</c>. Example: <c>123</c> for an integer ID, or <c>handle:example-product-handle</c> for a string handle.</param>
    /// <param name="pricePointId">The id or handle of the price point. When using the handle, it must be prefixed with <c>handle:</c>. Example: <c>123</c> for an integer ID, or <c>handle:example-price_point-handle</c> for a string handle.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ComponentPricePointResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="UpdateComponentPricePointError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates a component price point and its associated prices.
    /// <para>
    /// Passing in a price bracket without an <c>id</c> will attempt to create a new price.
    /// </para>
    /// <para>
    /// Including an <c>id</c> will update the corresponding price, and including the <c>_destroy</c> flag set to true along with the <c>id</c> will remove that price.
    /// </para>
    /// <para>
    /// Note: Custom price points cannot be updated directly. They must be edited through the Subscription.
    /// </para>
    /// </remarks>
    public Task<ComponentPricePointResponse> UpdateComponentPricePoint(ComponentIdModel componentId,
        PricePointIdModel pricePointId,
        UpdateComponentPricePointRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/components/{component_id}/price_points/{price_point_id}.json"),
            [new TemplateParam("component_id", componentId), new TemplateParam("price_point_id", pricePointId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Put,
            JsonRequest.Create(body),
            JsonResponse.Create<ComponentPricePointResponse>(),
            UpdateComponentPricePointErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Update Currency Prices
    /// </summary>
    /// <param name="pricePointId">The Advanced Billing id of the price point</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ComponentCurrencyPricesResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="UpdateCurrencyPricesError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates currency prices for a given currency defined at the site level.
    /// <para>
    /// Note: Currency Prices are not able to be updated for custom price points.
    /// </para>
    /// </remarks>
    public Task<ComponentCurrencyPricesResponse> UpdateCurrencyPrices(int pricePointId,
        UpdateCurrencyPricesRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/price_points/{price_point_id}/currency_prices.json"),
            [new TemplateParam("price_point_id", pricePointId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Put,
            JsonRequest.Create(body),
            JsonResponse.Create<ComponentCurrencyPricesResponse>(),
            UpdateCurrencyPricesErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);
}
