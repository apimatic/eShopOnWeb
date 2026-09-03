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
using Maxio.Models.Enums;

namespace Maxio.Api;

public sealed class WebhooksApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal WebhooksApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create Endpoint
    /// </summary>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="EndpointResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateEndpointError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates an endpoint and assigns a list of webhook subscriptions (events) to it.
    /// See the <see href="page:introduction/webhooks/webhooks-reference#events">Webhooks Reference</see> page for available events.
    /// </remarks>
    public Task<EndpointResponse> CreateEndpoint(CreateOrUpdateEndpointRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/endpoints.json"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<EndpointResponse>(),
            CreateEndpointErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Enable Webhooks
    /// </summary>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="EnableWebhooksResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Enables webhooks for your site.
    /// </remarks>
    public Task<EnableWebhooksResponse> EnableWebhooks(EnableWebhooksRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/webhooks/settings.json"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Put,
            JsonRequest.Create(body),
            JsonResponse.Create<EnableWebhooksResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List Endpoints
    /// </summary>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="IReadOnlyList{T}"/> of <see cref="Endpoint"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Lists endpoints configured for a site.
    /// </remarks>
    public Task<IReadOnlyList<Endpoint>> ListEndpoints(RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/endpoints.json"),
            [],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<IReadOnlyList<Endpoint>>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// List Webhooks
    /// </summary>
    /// <param name="status">Webhooks with matching status would be returned.</param>
    /// <param name="sinceDate">Format YYYY-MM-DD. Returns Webhooks with the created_at date greater than or equal to the one specified.</param>
    /// <param name="untilDate">Format YYYY-MM-DD. Returns Webhooks with the created_at date less than or equal to the one specified.</param>
    /// <param name="order">The order in which the Webhooks are returned.</param>
    /// <param name="subscription">The Advanced Billing id of a subscription you'd like to filter for</param>
    /// <param name="page">Result records are organized in pages. By default, the first page of results is displayed. The page parameter specifies a page number of results to fetch. You can start navigating through the pages to consume the results. You do this by passing in a page parameter. Retrieve the next page by adding ?page=2 to the query string. If there are no results to return, then an empty result set will be returned. Use in query <c>page=1</c>.</param>
    /// <param name="perPage">This parameter indicates how many records to fetch in each request. Default value is 20. The maximum allowed values is 200; any per_page value over 200 will be changed to 200. Use in query <c>per_page=200</c>.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="IReadOnlyList{T}"/> of <see cref="WebhookResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieves a list of webhooks.  You can pass query parameters if you want to filter webhooks. See the <see href="page:introduction/webhooks/webhooks">Webhooks</see> documentation for more information.
    /// </remarks>
    public Task<IReadOnlyList<WebhookResponse>> ListWebhooks(WebhookStatus? status,
        string? sinceDate,
        string? untilDate,
        WebhookOrder? order,
        int? subscription,
        int? page = 1,
        int? perPage = 20,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/webhooks.json"),
            [],
            [new Param("status", status),
                new Param("since_date", sinceDate),
                new Param("until_date", untilDate),
                new Param("page", page),
                new Param("per_page", perPage),
                new Param("order", order),
                new Param("subscription", subscription)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<IReadOnlyList<WebhookResponse>>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Replay Webhooks
    /// </summary>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ReplayWebhooksResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Replays webhooks. Posting to this endpoint does not immediately resend the webhooks. They are added to a queue and sent as soon as possible, depending on available system resources. You can submit an array of up to 1000 webhook IDs in the replay request.
    /// </remarks>
    public Task<ReplayWebhooksResponse> ReplayWebhooks(ReplayWebhooksRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/webhooks/replay.json"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<ReplayWebhooksResponse>(),
            RawErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);

    /// <summary>
    /// Update Endpoint
    /// </summary>
    /// <param name="endpointId">The Advanced Billing id for the endpoint that should be updated</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="EndpointResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="UpdateEndpointError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Updates an Endpoint. You can change the <c>url</c> of your endpoint or the list of <c>webhook_subscriptions</c> to which you are subscribed. See the <see href="page:introduction/webhooks/webhooks-reference#events">Webhooks Reference</see> page for available events.
    /// <para>
    /// Always send a complete list of events to which you want to subscribe. Sending a PUT request for an existing endpoint with an empty list of <c>webhook_subscriptions</c> will unsubscribe all events.
    /// </para>
    /// <para>
    /// If you want to unsubscribe from a specific event, send a list of <c>webhook_subscriptions</c> without the specific event key.
    /// </para>
    /// </remarks>
    public Task<EndpointResponse> UpdateEndpoint(int endpointId,
        CreateOrUpdateEndpointRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Production("/endpoints/{endpoint_id}.json"),
            [new TemplateParam("endpoint_id", endpointId)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Put,
            JsonRequest.Create(body),
            JsonResponse.Create<EndpointResponse>(),
            UpdateEndpointErrorResponse.Instance,
            [new AuthSchemeAny(_auth.BasicAuth, _auth.BearerAuth)],
            requestOptions,
            ct);
}
