using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core;
using FirecrawlApi.Core.Exceptions;
using FirecrawlApi.Core.Models;
using FirecrawlApi.Core.Request;
using FirecrawlApi.Core.Response;
using FirecrawlApi.Errors;
using FirecrawlApi.Models;

namespace FirecrawlApi.Api;

public sealed class ThreatProtection
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal ThreatProtection(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Get the team's threat protection policy
    /// </summary>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="TeamThreatProtectionResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="GetThreatProtectionError"/> when the server returns an error response.</exception>
    public Task<TeamThreatProtectionResponse> GetThreatProtection(RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/team/threat-protection"),
            [],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<TeamThreatProtectionResponse>(),
            GetThreatProtectionErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);

    /// <summary>
    /// Update the team's threat protection policy
    /// </summary>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="TeamThreatProtectionResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="UpdateThreatProtectionError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Full-document update. Unspecified fields reset to defaults. Enterprise feature, team admins only.
    /// </remarks>
    public Task<TeamThreatProtectionResponse> UpdateThreatProtection(TeamThreatProtectionRequest body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/team/threat-protection"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Put,
            JsonRequest.Create(body),
            JsonResponse.Create<TeamThreatProtectionResponse>(),
            UpdateThreatProtectionErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);
}
