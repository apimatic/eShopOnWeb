using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Maxio.Core;
using Maxio.Core.Authentication;
using Maxio.Core.Exceptions;
using Maxio.Core.Models;
using Maxio.Core.Request;
using Maxio.Core.Response;
using Maxio.Errors;
using Maxio.Models;

namespace Maxio.Api;

public sealed class EventsBasedBillingSegments
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal EventsBasedBillingSegments(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Bulk Create Segments
    /// </summary>
    /// <param name="componentId">ID or Handle for the Component</param>
    /// <param name="pricePointId">ID or Handle for the Price Point belonging to the Component</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListSegmentsResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="BulkCreateSegmentsError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates multiple segments in one request. The array of segments can contain up to <c>2000</c> records.
    /// <para>
    /// If any of the records contain an error the whole request would fail and none of the requested segments get created. The error response contains a message for only the one segment that failed validation, with the corresponding index in the array.
    /// </para>
    /// <para>
    /// You may specify component and/or price point by using either the numeric ID or the <c>handle:gold</c> syntax.
    /// </para>
    /// </remarks>
    public Task<ListSegmentsResponse> BulkCreateSegments(string componentId,
        string pricePointId,
        BulkCreateSegments? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/components/{component_id}/price_points/{price_point_id}/segments/bulk.json"),
            [new TemplateParam("component_id", componentId), new TemplateParam("price_point_id", pricePointId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<ListSegmentsResponse>(),
            BulkCreateSegmentsErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Bulk Update Segments
    /// </summary>
    /// <param name="componentId">ID or Handle for the Component</param>
    /// <param name="pricePointId">ID or Handle for the Price Point belonging to the Component</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListSegmentsResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="BulkUpdateSegmentsError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates multiple segments in one request. The array of segments can contain up to <c>1000</c> records.
    /// <para>
    /// If any of the records contain an error the whole request would fail and none of the requested segments get updated. The error response contains a message for only the one segment that failed validation, with the corresponding index in the array.
    /// </para>
    /// <para>
    /// You may specify component and/or price point by using either the numeric ID or the <c>handle:gold</c> syntax.
    /// </para>
    /// </remarks>
    public Task<ListSegmentsResponse> BulkUpdateSegments(string componentId,
        string pricePointId,
        BulkUpdateSegments? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/components/{component_id}/price_points/{price_point_id}/segments/bulk.json"),
            [new TemplateParam("component_id", componentId), new TemplateParam("price_point_id", pricePointId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Put,
            JsonRequest.Create(body),
            JsonResponse.Create<ListSegmentsResponse>(),
            BulkUpdateSegmentsErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Create Single Segment
    /// </summary>
    /// <param name="componentId">ID or Handle for the Component</param>
    /// <param name="pricePointId">ID or Handle for the Price Point belonging to the Component</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SegmentResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateSegmentError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates a new segment for a component with a segmented metric. It allows you to specify properties to bill upon and prices for each Segment. You can only pass as many "property_values" as the related Metric has segmenting properties defined.
    /// <para>
    /// You may specify component and/or price point by using either the numeric ID or the <c>handle:gold</c> syntax.
    /// </para>
    /// </remarks>
    public Task<SegmentResponse> CreateSegment(string componentId,
        string pricePointId,
        CreateSegmentRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/components/{component_id}/price_points/{price_point_id}/segments.json"),
            [new TemplateParam("component_id", componentId), new TemplateParam("price_point_id", pricePointId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<SegmentResponse>(),
            CreateSegmentErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Delete Single Segment
    /// </summary>
    /// <param name="componentId">ID or Handle of the Component</param>
    /// <param name="pricePointId">ID or Handle of the Price Point belonging to the Component</param>
    /// <param name="id">The ID of the Segment</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="DeleteSegmentError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Deletes a segment with the specified ID.
    /// <para>
    /// You may specify component and/or price point by using either the numeric ID or the <c>handle:gold</c> syntax.
    /// </para>
    /// </remarks>
    public Task DeleteSegment(string componentId,
        string pricePointId,
        double id,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/components/{component_id}/price_points/{price_point_id}/segments/{id}.json"),
            [new TemplateParam("component_id", componentId),
                new TemplateParam("price_point_id", pricePointId),
                new TemplateParam("id", id)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            VoidResponse.Instance,
            DeleteSegmentErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List Segments for a Price Point
    /// </summary>
    /// <param name="componentId">ID or Handle for the Component</param>
    /// <param name="pricePointId">ID or Handle for the Price Point belonging to the Component</param>
    /// <param name="filter">Filter to use for List Segments for a Price Point operation</param>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="perPage">This parameter indicates how many records to fetch in each request. Default value is 30. The maximum allowed values is 200; any per_page value over 200 will be changed to 200. Use in query <c>per_page=200</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListSegmentsResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="ListSegmentsForPricePointError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists segments created for a given price point, in order of creation.
    /// <para>
    /// You can pass <c>page</c> and <c>per_page</c> parameters in order to access all of the segments. By default it will return <c>30</c> records. You can set <c>per_page</c> to <c>200</c> at most.
    /// </para>
    /// <para>
    /// You may specify component and/or price point by using either the numeric ID or the <c>handle:gold</c> syntax.
    /// </para>
    /// </remarks>
    public Task<ListSegmentsResponse> ListSegmentsForPricePoint(string componentId,
        string pricePointId,
        ListSegmentsFilter? filter,
        int? page = 1,
        int? perPage = 30,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/components/{component_id}/price_points/{price_point_id}/segments.json"),
            [new TemplateParam("component_id", componentId), new TemplateParam("price_point_id", pricePointId)],
            [new Param("page", page), new Param("per_page", perPage), new Param("filter", filter)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListSegmentsResponse>(),
            ListSegmentsForPricePointErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Update Single Segment
    /// </summary>
    /// <param name="componentId">ID or Handle of the Component</param>
    /// <param name="pricePointId">ID or Handle of the Price Point belonging to the Component</param>
    /// <param name="id">The ID of the Segment</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="SegmentResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="UpdateSegmentError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates a single segment for a component with a segmented metric. It allows you to update the pricing for the segment.
    /// <para>
    /// You may specify component and/or price point by using either the numeric ID or the <c>handle:gold</c> syntax.
    /// </para>
    /// </remarks>
    public Task<SegmentResponse> UpdateSegment(string componentId,
        string pricePointId,
        double id,
        UpdateSegmentRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/components/{component_id}/price_points/{price_point_id}/segments/{id}.json"),
            [new TemplateParam("component_id", componentId),
                new TemplateParam("price_point_id", pricePointId),
                new TemplateParam("id", id)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Put,
            JsonRequest.Create(body),
            JsonResponse.Create<SegmentResponse>(),
            UpdateSegmentErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);
}
