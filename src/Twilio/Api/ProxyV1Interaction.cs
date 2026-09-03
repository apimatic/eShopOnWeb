using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Twilio.Core;
using Twilio.Core.ErrorResponse;
using Twilio.Core.Exceptions;
using Twilio.Core.Models;
using Twilio.Core.Request;
using Twilio.Core.Response;
using Twilio.Models;

namespace Twilio.Api;

public sealed class ProxyV1Interaction
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal ProxyV1Interaction(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Delete a specific Interaction.
    /// </summary>
    /// <param name="serviceSid">The SID of the parent <see href="https://www.twilio.com/docs/proxy/api/service">Service</see> of the resource to delete.</param>
    /// <param name="sessionSid">The SID of the parent <see href="https://www.twilio.com/docs/proxy/api/session">Session</see> of the resource to delete.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the Interaction resource to delete.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delete a specific Interaction.
    /// </remarks>
    public Task DeleteInteraction(string serviceSid,
        string sessionSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default10("/v1/Services/{ServiceSid}/Sessions/{SessionSid}/Interactions/{Sid}"),
            [new TemplateParam("ServiceSid", serviceSid),
                new TemplateParam("SessionSid", sessionSid),
                new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            VoidResponse.Instance,
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of Interactions for a given <see href="https://www.twilio.com/docs/proxy/api/session">Session</see>.
    /// </summary>
    /// <param name="serviceSid">The SID of the parent <see href="https://www.twilio.com/docs/proxy/api/service">Service</see> of the resource to fetch.</param>
    /// <param name="sessionSid">The SID of the parent <see href="https://www.twilio.com/docs/proxy/api/session">Session</see> of the resource to fetch.</param>
    /// <param name="sid">The Twilio-provided string that uniquely identifies the Interaction resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ProxyV1ServiceSessionInteraction"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of Interactions for a given <see href="https://www.twilio.com/docs/proxy/api/session">Session</see>.
    /// </remarks>
    public Task<ProxyV1ServiceSessionInteraction> FetchInteraction(string serviceSid,
        string sessionSid,
        string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default10("/v1/Services/{ServiceSid}/Sessions/{SessionSid}/Interactions/{Sid}"),
            [new TemplateParam("ServiceSid", serviceSid),
                new TemplateParam("SessionSid", sessionSid),
                new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ProxyV1ServiceSessionInteraction>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Retrieve a list of all Interactions for a Session. A maximum of 100 records will be returned per page.
    /// </summary>
    /// <param name="serviceSid">The SID of the parent <see href="https://www.twilio.com/docs/proxy/api/service">Service</see> to read the resources from.</param>
    /// <param name="sessionSid">The SID of the parent <see href="https://www.twilio.com/docs/proxy/api/session">Session</see> to read the resources from.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListInteractionResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of all Interactions for a Session. A maximum of 100 records will be returned per page.
    /// </remarks>
    public Task<ListInteractionResponse> ListInteraction(string serviceSid,
        string sessionSid,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default10("/v1/Services/{ServiceSid}/Sessions/{SessionSid}/Interactions"),
            [new TemplateParam("ServiceSid", serviceSid), new TemplateParam("SessionSid", sessionSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListInteractionResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
