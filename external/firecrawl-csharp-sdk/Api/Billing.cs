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

public sealed class Billing
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Billing(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Get remaining credits for the authenticated team
    /// </summary>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="TeamCreditUsageResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="GetCreditUsageError"/> when the server returns an error response.</exception>
    public Task<TeamCreditUsageResponse> GetCreditUsage(RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/team/credit-usage"),
            [],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<TeamCreditUsageResponse>(),
            GetCreditUsageErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);

    /// <summary>
    /// Get historical credit usage for the authenticated team
    /// </summary>
    /// <param name="byApiKey">Get historical credit usage by API key</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="TeamCreditUsageHistoricalResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="GetHistoricalCreditUsageError"/> when the server returns an error response.</exception>
    public Task<TeamCreditUsageHistoricalResponse> GetHistoricalCreditUsage(bool? byApiKey = false,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/team/credit-usage/historical"),
            [],
            [new Param("byApiKey", byApiKey)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<TeamCreditUsageHistoricalResponse>(),
            GetHistoricalCreditUsageErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);

    /// <summary>
    /// Get historical token usage for the authenticated team (Extract only)
    /// </summary>
    /// <param name="byApiKey">Get historical token usage by API key</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="TeamTokenUsageHistoricalResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="GetHistoricalTokenUsageError"/> when the server returns an error response.</exception>
    public Task<TeamTokenUsageHistoricalResponse> GetHistoricalTokenUsage(bool? byApiKey = false,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/team/token-usage/historical"),
            [],
            [new Param("byApiKey", byApiKey)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<TeamTokenUsageHistoricalResponse>(),
            GetHistoricalTokenUsageErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);

    /// <summary>
    /// Get remaining tokens for the authenticated team (Extract only)
    /// </summary>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="TeamTokenUsageResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="GetTokenUsageError"/> when the server returns an error response.</exception>
    public Task<TeamTokenUsageResponse> GetTokenUsage(RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/team/token-usage"),
            [],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<TeamTokenUsageResponse>(),
            GetTokenUsageErrorResponse.Instance,
            [_auth.BearerAuth],
            requestOptions,
            ct);
}
