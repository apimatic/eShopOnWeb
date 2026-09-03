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

public sealed class ProductPricePoints
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal ProductPricePoints(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Archive Product Price Point
    /// </summary>
    /// <param name="productId">The id or handle of the product. When using the handle, it must be prefixed with <c>handle:</c>. Example: <c>123</c> for an integer ID, or <c>handle:example-product-handle</c> for a string handle.</param>
    /// <param name="pricePointId">The id or handle of the price point. When using the handle, it must be prefixed with <c>handle:</c>. Example: <c>123</c> for an integer ID, or <c>handle:example-product-price-point-handle</c> for a string handle.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ProductPricePointResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ArchiveProductPricePointError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Archives a product price point.
    /// </remarks>
    public Task<ProductPricePointResponse> ArchiveProductPricePoint(ProductIdModel productId,
        PricePointIdModel pricePointId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/products/{product_id}/price_points/{price_point_id}.json"),
            [new TemplateParam("product_id", productId), new TemplateParam("price_point_id", pricePointId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            JsonResponse.Create<ProductPricePointResponse>(),
            ArchiveProductPricePointErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Bulk Create Product Price Points
    /// </summary>
    /// <param name="productId">The Advanced Billing id of the product to which the price points belong</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="BulkCreateProductPricePointsResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="BulkCreateProductPricePointsError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates multiple product price points in one request.
    /// </remarks>
    public Task<BulkCreateProductPricePointsResponse> BulkCreateProductPricePoints(int productId,
        BulkCreateProductPricePointsRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/products/{product_id}/price_points/bulk.json"),
            [new TemplateParam("product_id", productId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<BulkCreateProductPricePointsResponse>(),
            BulkCreateProductPricePointsErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Create Product Currency Prices
    /// </summary>
    /// <param name="productPricePointId">The Advanced Billing id of the product price point</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="CurrencyPricesResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateProductCurrencyPricesError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates currency prices for a given currency that has been defined on the site level in your settings.
    /// <para>
    /// When creating currency prices, they need to mirror the structure of your primary pricing. If the product price point defines a trial and/or setup fee, each currency must also define a trial and/or setup fee.
    /// </para>
    /// <para>
    /// Note: Currency Prices are not able to be created for custom product price points.
    /// </para>
    /// </remarks>
    public Task<CurrencyPricesResponse> CreateProductCurrencyPrices(int productPricePointId,
        CreateProductCurrencyPricesRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/product_price_points/{product_price_point_id}/currency_prices.json"),
            [new TemplateParam("product_price_point_id", productPricePointId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<CurrencyPricesResponse>(),
            CreateProductCurrencyPricesErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Create Product Price Point
    /// </summary>
    /// <param name="productId">The id or handle of the product. When using the handle, it must be prefixed with <c>handle:</c></param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ProductPricePointResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateProductPricePointError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates a Product Price Point. See the <see href="https://maxio.zendesk.com/hc/en-us/articles/24261111947789-Product-Price-Points">Product Price Point</see> documentation for details.
    /// </remarks>
    public Task<ProductPricePointResponse> CreateProductPricePoint(ProductIdModel productId,
        CreateProductPricePointRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/products/{product_id}/price_points.json"),
            [new TemplateParam("product_id", productId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<ProductPricePointResponse>(),
            CreateProductPricePointErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List All Products Price Points
    /// </summary>
    /// <param name="direction">Controls the order in which results are returned. Use in query <c>direction=asc</c>.</param>
    /// <param name="filter">Filter to use for List PricePoints operations</param>
    /// <param name="include">Allows including additional data in the response. Use in query: <c>include=currency_prices</c>.</param>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="perPage">This parameter indicates how many records to fetch in each request. Default value is 20. The maximum allowed values is 200; any per_page value over 200 will be changed to 200. Use in query <c>per_page=200</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListProductPricePointsResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ListAllProductPricePointsError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists Product Price Points belonging to a site.
    /// </remarks>
    public Task<ListProductPricePointsResponse> ListAllProductPricePoints(SortingDirection? direction,
        ListPricePointsFilter? filter,
        ListProductsPricePointsInclude? include,
        int? page = 1,
        int? perPage = 20,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/products_price_points.json"),
            [],
            [new Param("direction", direction),
                new Param("filter", filter),
                new Param("include", include),
                new Param("page", page),
                new Param("per_page", perPage)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListProductPricePointsResponse>(),
            ListAllProductPricePointsErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List Product Price Points
    /// </summary>
    /// <param name="productId">The id or handle of the product. When using the handle, it must be prefixed with <c>handle:</c></param>
    /// <param name="currencyPrices">(Optional) If you have defined multiple currencies at the site level, you can pass ?currency_prices=true to include an array of currency price data in the response. If the product price point is set to use_site_exchange_rate: true, it will return pricing based on the current exchange rate. If the flag is set to false, it will return all of the defined prices for each currency.</param>
    /// <param name="filterType">Use in query: <c>filter[type]=catalog,default</c>.</param>
    /// <param name="archived">Set to include archived price points in the response.</param>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="perPage">This parameter indicates how many records to fetch in each request. Default value is 10. The maximum allowed values is 200; any per_page value over 200 will be changed to 200.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListProductPricePointsResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieves a list of product price points.
    /// </remarks>
    public Task<ListProductPricePointsResponse> ListProductPricePoints(ProductIdModel productId,
        bool? currencyPrices,
        IReadOnlyList<PricePointType>? filterType,
        bool? archived,
        int? page = 1,
        int? perPage = 10,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/products/{product_id}/price_points.json"),
            [new TemplateParam("product_id", productId)],
            [new Param("page", page),
                new Param("per_page", perPage),
                new Param("currency_prices", currencyPrices),
                new Param("filter[type]", filterType),
                new Param("archived", archived)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListProductPricePointsResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Promote Product Price Point to Default
    /// </summary>
    /// <param name="productId">The Advanced Billing id of the product to which the price point belongs</param>
    /// <param name="pricePointId">The Advanced Billing id of the product price point</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ProductResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Sets a product price point as the default for the product.
    /// <para>
    /// Note: Custom product price points cannot be set as the default for a product.
    /// </para>
    /// </remarks>
    public Task<ProductResponse> PromoteProductPricePointToDefault(int productId,
        int pricePointId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/products/{product_id}/price_points/{price_point_id}/default.json"),
            [new TemplateParam("product_id", productId), new TemplateParam("price_point_id", pricePointId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            new HttpMethod("PATCH"),
            EmptyBody.Instance,
            JsonResponse.Create<ProductResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Read Product Price Point
    /// </summary>
    /// <param name="productId">The id or handle of the product. When using the handle, it must be prefixed with <c>handle:</c>. Example: <c>123</c> for an integer ID, or <c>handle:example-product-handle</c> for a string handle.</param>
    /// <param name="pricePointId">The id or handle of the price point. When using the handle, it must be prefixed with <c>handle:</c>. Example: <c>123</c> for an integer ID, or <c>handle:example-product-price-point-handle</c> for a string handle.</param>
    /// <param name="currencyPrices">(Optional) If you have defined multiple currencies at the site level, you can pass ?currency_prices=true to include an array of currency price data in the response. If the product price point is set to use_site_exchange_rate: true, it will return pricing based on the current exchange rate. If the flag is set to false, it will return all of the defined prices for each currency.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ProductPricePointResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Returns details for a specific product price point. You can achieve this by using either the product price point ID or handle.
    /// </remarks>
    public Task<ProductPricePointResponse> ReadProductPricePoint(ProductIdModel productId,
        PricePointIdModel pricePointId,
        bool? currencyPrices,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/products/{product_id}/price_points/{price_point_id}.json"),
            [new TemplateParam("product_id", productId), new TemplateParam("price_point_id", pricePointId)],
            [new Param("currency_prices", currencyPrices)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ProductPricePointResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Unarchive Product Price Point
    /// </summary>
    /// <param name="productId">The Advanced Billing id of the product to which the price point belongs</param>
    /// <param name="pricePointId">The Advanced Billing id of the product price point</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ProductPricePointResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Unarchives an archived product price point.
    /// </remarks>
    public Task<ProductPricePointResponse> UnarchiveProductPricePoint(int productId,
        int pricePointId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/products/{product_id}/price_points/{price_point_id}/unarchive.json"),
            [new TemplateParam("product_id", productId), new TemplateParam("price_point_id", pricePointId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            new HttpMethod("PATCH"),
            EmptyBody.Instance,
            JsonResponse.Create<ProductPricePointResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Update Product Currency Prices
    /// </summary>
    /// <param name="productPricePointId">The Advanced Billing id of the product price point</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="CurrencyPricesResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="UpdateProductCurrencyPricesError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates the <c>price</c>s of currency prices for a given currency that exists on the product price point.
    /// <para>
    /// When updating the pricing, it needs to mirror the structure of your primary pricing. If the product price point defines a trial and/or setup fee, each currency must also define a trial and/or setup fee.
    /// </para>
    /// <para>
    /// Note: Currency Prices cannot be updated for custom product price points.
    /// </para>
    /// </remarks>
    public Task<CurrencyPricesResponse> UpdateProductCurrencyPrices(int productPricePointId,
        UpdateCurrencyPricesRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/product_price_points/{product_price_point_id}/currency_prices.json"),
            [new TemplateParam("product_price_point_id", productPricePointId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Put,
            JsonRequest.Create(body),
            JsonResponse.Create<CurrencyPricesResponse>(),
            UpdateProductCurrencyPricesErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Update Product Price Point
    /// </summary>
    /// <param name="productId">The id or handle of the product. When using the handle, it must be prefixed with <c>handle:</c>. Example: <c>123</c> for an integer ID, or <c>handle:example-product-handle</c> for a string handle.</param>
    /// <param name="pricePointId">The id or handle of the price point. When using the handle, it must be prefixed with <c>handle:</c>. Example: <c>123</c> for an integer ID, or <c>handle:example-product-price-point-handle</c> for a string handle.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ProductPricePointResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates a product price point.
    /// <para>
    /// Note: Custom product price points cannot be updated.
    /// </para>
    /// </remarks>
    public Task<ProductPricePointResponse> UpdateProductPricePoint(ProductIdModel productId,
        PricePointIdModel pricePointId,
        UpdateProductPricePointRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/products/{product_id}/price_points/{price_point_id}.json"),
            [new TemplateParam("product_id", productId), new TemplateParam("price_point_id", pricePointId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Put,
            JsonRequest.Create(body),
            JsonResponse.Create<ProductPricePointResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);
}
