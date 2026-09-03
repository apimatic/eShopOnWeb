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
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Api;

public sealed class InsightsV1Metric
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal InsightsV1Metric(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Get a list of Call Metrics for a Call.
    /// </summary>
    /// <param name="callSid">The unique SID identifier of the Call.</param>
    /// <param name="edge">The Edge of this Metric. One of <c>unknown_edge</c>, <c>carrier_edge</c>, <c>sip_edge</c>, <c>sdk_edge</c> or <c>client_edge</c>.</param>
    /// <param name="direction">The Direction of this Metric. One of <c>unknown</c>, <c>inbound</c>, <c>outbound</c> or <c>both</c>.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListMetricResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Get a list of Call Metrics for a Call.
    /// </remarks>
    public Task<ListMetricResponse> ListMetric(string callSid,
        MetricEnumTwilioEdge? edge,
        MetricEnumStreamDirection? direction,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default14("/v1/Voice/{CallSid}/Metrics"),
            [new TemplateParam("CallSid", callSid)],
            [new Param("Edge", edge),
                new Param("Direction", direction),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListMetricResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
