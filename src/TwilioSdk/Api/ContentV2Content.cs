using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TwilioSdk.Core;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Core.Extensions;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Request;
using TwilioSdk.Core.Response;
using TwilioSdk.Models;

namespace TwilioSdk.Api;

public sealed class ContentV2Content
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal ContentV2Content(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Retrieve a list of Contents belonging to the account used to make the request
    /// </summary>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="sortByDate">Whether to sort by ascending or descending date updated</param>
    /// <param name="sortByContentName">Whether to sort by ascending or descending content name</param>
    /// <param name="dateCreatedAfter">Filter by &gt;=[date-time]</param>
    /// <param name="dateCreatedBefore">Filter by &lt;=[date-time]</param>
    /// <param name="contentName">Filter by Regex Pattern in content name</param>
    /// <param name="content">Filter by Regex Pattern in template content</param>
    /// <param name="language">Filter by array of valid language(s)</param>
    /// <param name="contentType">Filter by array of contentType(s)</param>
    /// <param name="channelEligibility">Filter by array of ChannelEligibility(s), where ChannelEligibility=&lt;channel&gt;:&lt;status&gt;</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListContentResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<ListContentResponse> ListContent2(int? pageSize,
        int? page,
        string? pageToken,
        string? sortByDate,
        string? sortByContentName,
        DateTimeOffset? dateCreatedAfter,
        DateTimeOffset? dateCreatedBefore,
        string? contentName,
        string? content,
        IReadOnlyList<string>? language,
        IReadOnlyList<string>? contentType,
        IReadOnlyList<string>? channelEligibility,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default2("/v2/Content"),
            [],
            [new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken),
                new Param("SortByDate", sortByDate),
                new Param("SortByContentName", sortByContentName),
                new Param("DateCreatedAfter", dateCreatedAfter?.ToIso8601()),
                new Param("DateCreatedBefore", dateCreatedBefore?.ToIso8601()),
                new Param("ContentName", contentName),
                new Param("Content", content),
                new Param("Language", language),
                new Param("ContentType", contentType),
                new Param("ChannelEligibility", channelEligibility)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListContentResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
