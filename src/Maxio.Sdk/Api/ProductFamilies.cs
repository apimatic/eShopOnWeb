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

public sealed class ProductFamilies
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal ProductFamilies(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create Product Family
    /// </summary>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ProductFamilyResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateProductFamilyError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates a Product Family within your Advanced Billing site. Create a Product Family to act as a container for your products, components, and coupons.
    /// <para>
    /// Full documentation on how Product Families operate within the Advanced Billing UI can be located <see href="https://maxio.zendesk.com/hc/en-us/articles/24261098936205-Product-Families">here</see>.
    /// </para>
    /// </remarks>
    public Task<ProductFamilyResponse> CreateProductFamily(CreateProductFamilyRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/product_families.json"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<ProductFamilyResponse>(),
            CreateProductFamilyErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List Product Families
    /// </summary>
    /// <param name="dateField">The type of filter you would like to apply to your search. Use in query: <c>date_field=created_at</c>.</param>
    /// <param name="startDate">The start date (format YYYY-MM-DD) with which to filter the date_field. Returns products with a timestamp at or after midnight (12:00:00 AM) in your site’s time zone on the date specified.</param>
    /// <param name="endDate">The end date (format YYYY-MM-DD) with which to filter the date_field. Returns products with a timestamp up to and including 11:59:59PM in your site’s time zone on the date specified.</param>
    /// <param name="startDatetime">The start date and time (format YYYY-MM-DD HH:MM:SS) with which to filter the date_field. Returns products with a timestamp at or after exact time provided in query. You can specify timezone in query - otherwise your site's time zone will be used. If provided, this parameter will be used instead of start_date.</param>
    /// <param name="endDatetime">The end date and time (format YYYY-MM-DD HH:MM:SS) with which to filter the date_field. Returns products with a timestamp at or before exact time provided in query. You can specify timezone in query - otherwise your site's time zone will be used. If provided, this parameter will be used instead of end_date.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="IReadOnlyList{T}"/> of <see cref="ProductFamilyResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists Product Families for a site.
    /// </remarks>
    public Task<IReadOnlyList<ProductFamilyResponse>> ListProductFamilies(BasicDateField? dateField,
        DateTimeOffset? startDate,
        DateTimeOffset? endDate,
        DateTimeOffset? startDatetime,
        DateTimeOffset? endDatetime,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/product_families.json"),
            [],
            [new Param("date_field", dateField),
                new Param("start_date", startDate?.ToDate()),
                new Param("end_date", endDate?.ToDate()),
                new Param("start_datetime", startDatetime?.ToIso8601()),
                new Param("end_datetime", endDatetime?.ToIso8601())],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<IReadOnlyList<ProductFamilyResponse>>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List Products for Product Family
    /// </summary>
    /// <param name="productFamilyId">Either the product family's id or its handle prefixed with <c>handle:</c></param>
    /// <param name="dateField">The type of filter you would like to apply to your search. Use in query: <c>date_field=created_at</c>.</param>
    /// <param name="filter">Filter to use for List Products operations</param>
    /// <param name="startDate">The start date (format YYYY-MM-DD) with which to filter the date_field. Returns products with a timestamp at or after midnight (12:00:00 AM) in your site’s time zone on the date specified.</param>
    /// <param name="endDate">The end date (format YYYY-MM-DD) with which to filter the date_field. Returns products with a timestamp up to and including 11:59:59PM in your site’s time zone on the date specified.</param>
    /// <param name="startDatetime">The start date and time (format YYYY-MM-DD HH:MM:SS) with which to filter the date_field. Returns products with a timestamp at or after exact time provided in query. You can specify timezone in query - otherwise your site's time zone will be used. If provided, this parameter will be used instead of start_date.</param>
    /// <param name="endDatetime">The end date and time (format YYYY-MM-DD HH:MM:SS) with which to filter the date_field. Returns products with a timestamp at or before exact time provided in query. You can specify timezone in query - otherwise your site's time zone will be used. If provided, this parameter will be used instead of end_date.</param>
    /// <param name="includeArchived">Include archived products.</param>
    /// <param name="include">Allows including additional data in the response. Use in query <c>include=prepaid_product_price_point</c>.</param>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="perPage">This parameter indicates how many records to fetch in each request. Default value is 20. The maximum allowed values is 200; any per_page value over 200 will be changed to 200. Use in query <c>per_page=200</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="IReadOnlyList{T}"/> of <see cref="ProductResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ListProductsForProductFamilyError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieves a list of Products belonging to a Product Family.
    /// </remarks>
    public Task<IReadOnlyList<ProductResponse>> ListProductsForProductFamily(string productFamilyId,
        BasicDateField? dateField,
        ListProductsFilter? filter,
        DateTimeOffset? startDate,
        DateTimeOffset? endDate,
        DateTimeOffset? startDatetime,
        DateTimeOffset? endDatetime,
        bool? includeArchived,
        ListProductsInclude? include,
        int? page = 1,
        int? perPage = 20,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/product_families/{product_family_id}/products.json"),
            [new TemplateParam("product_family_id", productFamilyId)],
            [new Param("page", page),
                new Param("per_page", perPage),
                new Param("date_field", dateField),
                new Param("filter", filter),
                new Param("start_date", startDate?.ToDate()),
                new Param("end_date", endDate?.ToDate()),
                new Param("start_datetime", startDatetime?.ToIso8601()),
                new Param("end_datetime", endDatetime?.ToIso8601()),
                new Param("include_archived", includeArchived),
                new Param("include", include)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<IReadOnlyList<ProductResponse>>(),
            ListProductsForProductFamilyErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Read Product Family
    /// </summary>
    /// <param name="id">The Advanced Billing id of the product family</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ProductFamilyResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieves a Product Family via the <c>product_family_id</c>. The response will contain a Product Family object.
    /// <para>
    /// The product family can be specified either with the id number, or with the <c>handle:my-family</c> format.
    /// </para>
    /// </remarks>
    public Task<ProductFamilyResponse> ReadProductFamily(int id,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/product_families/{id}.json"),
            [new TemplateParam("id", id)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ProductFamilyResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);
}
