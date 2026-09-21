using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TwilioSdk.Core;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Request;
using TwilioSdk.Core.Response;
using TwilioSdk.Models;

namespace TwilioSdk.Api;

public sealed class StudioV1StepContext
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal StudioV1StepContext(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Retrieve the context for an Engagement Step.
    /// </summary>
    /// <param name="flowSid">The SID of the Flow with the Step to fetch.</param>
    /// <param name="engagementSid">The SID of the Engagement with the Step to fetch.</param>
    /// <param name="stepSid">The SID of the Step to fetch</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="StudioV1FlowEngagementStepStepContext"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve the context for an Engagement Step.
    /// </remarks>
    public Task<StudioV1FlowEngagementStepStepContext> FetchStepContext(string flowSid,
        string engagementSid,
        string stepSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default11("/v1/Flows/{FlowSid}/Engagements/{EngagementSid}/Steps/{StepSid}/Context"),
            [new TemplateParam("FlowSid", flowSid),
                new TemplateParam("EngagementSid", engagementSid),
                new TemplateParam("StepSid", stepSid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<StudioV1FlowEngagementStepStepContext>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
