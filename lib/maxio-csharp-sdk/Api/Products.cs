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
using Maxio.Models.Enums;

namespace Maxio.Api;

public sealed class Products
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Products(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Archive Product
    /// </summary>
    /// <param name="productId">The Advanced Billing id of the product</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ProductResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ArchiveProductError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Archives the product. All current subscribers will be unaffected; their subscription/purchase will continue to be charged monthly.
    /// <para>
    /// This will restrict the option to chose the product for purchase via the Billing Portal, as well as disable Public Signup Pages for the product.
    /// </para>
    /// </remarks>
    public Task<ProductResponse> ArchiveProduct(int productId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/products/{product_id}.json"),
            [new TemplateParam("product_id", productId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            JsonResponse.Create<ProductResponse>(),
            ArchiveProductErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Create Product
    /// </summary>
    /// <param name="productFamilyId">Either the product family's id or its handle prefixed with <c>handle:</c></param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ProductResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateProductError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates a product in your Advanced Billing site.
    /// <para>
    /// If you have the new <see href="page:help/announcements/2026-announcements#new-catalog-experience-and-terminology">Catalog experience</see> enabled, the <c>auto_create_signup_page</c> parameter is not supported. If <c>auto_create_signup_page</c> is included (with any value) an error is returned.
    /// </para>
    /// <para>
    /// See the following product documentation for more information:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><see href="https://maxio.zendesk.com/hc/en-us/articles/24261090117645-Products-Overview">Products Documentation</see></description></item>
    ///   <item><description><see href="https://maxio.zendesk.com/hc/en-us/articles/24252069837581-Product-Changes-and-Migrations">Changing a Subscription's Product</see></description></item>
    /// </list>
    /// </remarks>
    public Task<ProductResponse> CreateProduct(string productFamilyId,
        CreateOrUpdateProductRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/product_families/{product_family_id}/products.json"),
            [new TemplateParam("product_family_id", productFamilyId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<ProductResponse>(),
            CreateProductErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List Products
    /// </summary>
    /// <param name="dateField">The type of filter you would like to apply to your search. Use in query: <c>date_field=created_at</c>.</param>
    /// <param name="filter">Filter to use for List Products operations</param>
    /// <param name="endDate">The end date (format YYYY-MM-DD) with which to filter the date_field. Returns products with a timestamp up to and including 11:59:59PM in your site’s time zone on the date specified.</param>
    /// <param name="endDatetime">The end date and time (format YYYY-MM-DD HH:MM:SS) with which to filter the date_field. Returns products with a timestamp at or before exact time provided in query. You can specify timezone in query - otherwise your site''s time zone will be used. If provided, this parameter will be used instead of end_date.</param>
    /// <param name="startDate">The start date (format YYYY-MM-DD) with which to filter the date_field. Returns products with a timestamp at or after midnight (12:00:00 AM) in your site’s time zone on the date specified.</param>
    /// <param name="startDatetime">The start date and time (format YYYY-MM-DD HH:MM:SS) with which to filter the date_field. Returns products with a timestamp at or after exact time provided in query. You can specify timezone in query - otherwise your site''s time zone will be used. If provided, this parameter will be used instead of start_date.</param>
    /// <param name="includeArchived">Include archived products. Use in query: <c>include_archived=true</c>.</param>
    /// <param name="include">Allows including additional data in the response. Use in query <c>include=prepaid_product_price_point</c>.</param>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="perPage">This parameter indicates how many records to fetch in each request. Default value is 20. The maximum allowed values is 200; any per_page value over 200 will be changed to 200. Use in query <c>per_page=200</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="IReadOnlyList{T}"/> of <see cref="ProductResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists products belonging to a site.
    /// </remarks>
    public Task<IReadOnlyList<ProductResponse>> ListProducts(BasicDateField? dateField,
        ListProductsFilter? filter,
        DateTimeOffset? endDate,
        DateTimeOffset? endDatetime,
        DateTimeOffset? startDate,
        DateTimeOffset? startDatetime,
        bool? includeArchived,
        ListProductsInclude? include,
        int? page = 1,
        int? perPage = 20,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/products.json"),
            [],
            [new Param("date_field", dateField),
                new Param("filter", filter),
                new Param("end_date", endDate?.ToDate()),
                new Param("end_datetime", endDatetime?.ToIso8601()),
                new Param("start_date", startDate?.ToDate()),
                new Param("start_datetime", startDatetime?.ToIso8601()),
                new Param("page", page),
                new Param("per_page", perPage),
                new Param("include_archived", includeArchived),
                new Param("include", include)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<IReadOnlyList<ProductResponse>>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Read Product
    /// </summary>
    /// <param name="productId">The Advanced Billing id of the product</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ProductResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Reads the current details of a product.
    /// </remarks>
    public Task<ProductResponse> ReadProduct(int productId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/products/{product_id}.json"),
            [new TemplateParam("product_id", productId)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ProductResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Read Product by Handle
    /// </summary>
    /// <param name="apiHandle">The handle of the product</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ProductResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieves a Product object by its <c>api_handle</c>.
    /// </remarks>
    public Task<ProductResponse> ReadProductByHandle(string apiHandle,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/products/handle/{api_handle}.json"),
            [new TemplateParam("api_handle", apiHandle)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ProductResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Update Product
    /// </summary>
    /// <param name="productId">The Advanced Billing id of the product</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ProductResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="UpdateProductError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates aspects of an existing product.
    /// <para>
    /// ### Input Attributes Update Notes
    /// </para>
    /// <list type="bullet">
    ///   <item><description><c>update_return_params</c> The parameters we will append to your <c>update_return_url</c>. See Return URLs and Parameters</description></item>
    /// </list>
    /// <para>
    /// ### Product Price Point
    /// </para>
    /// <para>
    /// Updating a product using this endpoint will create a new price point and set it as the default price point for this product. If you should like to update an existing product price point, that must be done separately.
    /// </para>
    /// </remarks>
    public Task<ProductResponse> UpdateProduct(int productId,
        CreateOrUpdateProductRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/products/{product_id}.json"),
            [new TemplateParam("product_id", productId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Put,
            JsonRequest.Create(body),
            JsonResponse.Create<ProductResponse>(),
            UpdateProductErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);
}
