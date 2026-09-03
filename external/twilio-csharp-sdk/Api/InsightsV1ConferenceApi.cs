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

public sealed class InsightsV1ConferenceApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal InsightsV1ConferenceApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Get a specific Conference Summary.
    /// </summary>
    /// <param name="conferenceSid">The unique SID identifier of the Conference.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="InsightsV1Conference"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Get a specific Conference Summary.
    /// </remarks>
    public Task<InsightsV1Conference> FetchConference2(string conferenceSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default14("/v1/Conferences/{ConferenceSid}"),
            [new TemplateParam("ConferenceSid", conferenceSid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<InsightsV1Conference>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Get a list of Conference Summaries.
    /// </summary>
    /// <param name="conferenceSid">The SID of the conference.</param>
    /// <param name="friendlyName">Custom label for the conference resource, up to 64 characters.</param>
    /// <param name="status">Conference status.</param>
    /// <param name="createdAfter">Conferences created after the provided timestamp specified in ISO 8601 format</param>
    /// <param name="createdBefore">Conferences created before the provided timestamp specified in ISO 8601 format.</param>
    /// <param name="mixerRegion">Twilio region where the conference media was mixed.</param>
    /// <param name="tags">Tags applied by Twilio for common potential configuration, quality, or performance issues.</param>
    /// <param name="subaccount">Account SID for the subaccount whose resources you wish to retrieve.</param>
    /// <param name="detectedIssues">Potential configuration, behavior, or performance issues detected during the conference.</param>
    /// <param name="endReason">Conference end reason; e.g. last participant left, modified by API, etc.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListConferenceResponse1"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Get a list of Conference Summaries.
    /// </remarks>
    public Task<ListConferenceResponse1> ListConference2(string? conferenceSid,
        string? friendlyName,
        string? status,
        string? createdAfter,
        string? createdBefore,
        string? mixerRegion,
        string? tags,
        string? subaccount,
        string? detectedIssues,
        string? endReason,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default14("/v1/Conferences"),
            [],
            [new Param("ConferenceSid", conferenceSid),
                new Param("FriendlyName", friendlyName),
                new Param("Status", status),
                new Param("CreatedAfter", createdAfter),
                new Param("CreatedBefore", createdBefore),
                new Param("MixerRegion", mixerRegion),
                new Param("Tags", tags),
                new Param("Subaccount", subaccount),
                new Param("DetectedIssues", detectedIssues),
                new Param("EndReason", endReason),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListConferenceResponse1>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
