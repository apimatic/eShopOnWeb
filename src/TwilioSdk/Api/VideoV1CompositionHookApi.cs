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
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Api;

public sealed class VideoV1CompositionHookApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal VideoV1CompositionHookApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Recording composition hooks
    /// </summary>
    /// <param name="friendlyName"></param>
    /// <param name="enabled"></param>
    /// <param name="videoLayout"></param>
    /// <param name="audioSources"></param>
    /// <param name="audioSourcesExcluded"></param>
    /// <param name="resolution"></param>
    /// <param name="format"></param>
    /// <param name="statusCallback"></param>
    /// <param name="statusCallbackMethod"></param>
    /// <param name="trim"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VideoV1CompositionHook"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<VideoV1CompositionHook> CreateCompositionHook(string friendlyName,
        bool? enabled,
        object? videoLayout,
        IReadOnlyList<string>? audioSources,
        IReadOnlyList<string>? audioSourcesExcluded,
        string? resolution,
        CompositionHookEnumFormat? format,
        string? statusCallback,
        AmdStatusCallbackMethod? statusCallbackMethod,
        bool? trim,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default6("/v1/CompositionHooks"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("Enabled", enabled),
                    new Param("VideoLayout", videoLayout),
                    new Param("AudioSources", audioSources),
                    new Param("AudioSourcesExcluded", audioSourcesExcluded),
                    new Param("Resolution", resolution),
                    new Param("Format", format),
                    new Param("StatusCallback", statusCallback),
                    new Param("StatusCallbackMethod", statusCallbackMethod),
                    new Param("Trim", trim)]),
            JsonResponse.Create<VideoV1CompositionHook>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Delete a Recording CompositionHook resource identified by a <c>CompositionHook SID</c>.
    /// </summary>
    /// <param name="sid">The SID of the CompositionHook resource to delete.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delete a Recording CompositionHook resource identified by a <c>CompositionHook SID</c>.
    /// </remarks>
    public Task DeleteCompositionHook(string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default6("/v1/CompositionHooks/{Sid}"),
            [new TemplateParam("Sid", sid)],
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
    /// Returns a single CompositionHook resource identified by a CompositionHook SID.
    /// </summary>
    /// <param name="sid">The SID of the CompositionHook resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VideoV1CompositionHook"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Returns a single CompositionHook resource identified by a CompositionHook SID.
    /// </remarks>
    public Task<VideoV1CompositionHook> FetchCompositionHook(string sid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default6("/v1/CompositionHooks/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<VideoV1CompositionHook>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// List of all Recording CompositionHook resources.
    /// </summary>
    /// <param name="enabled">Read only CompositionHook resources with an <c>enabled</c> value that matches this parameter.</param>
    /// <param name="dateCreatedAfter">Read only CompositionHook resources created on or after this <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> datetime with time zone.</param>
    /// <param name="dateCreatedBefore">Read only CompositionHook resources created before this <see href="https://en.wikipedia.org/wiki/ISO_8601">ISO 8601</see> datetime with time zone.</param>
    /// <param name="friendlyName">Read only CompositionHook resources with friendly names that match this string. The match is not case sensitive and can include asterisk <c>*</c> characters as wildcard match.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListCompositionHookResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// List of all Recording CompositionHook resources.
    /// </remarks>
    public Task<ListCompositionHookResponse> ListCompositionHook(bool? enabled,
        DateTimeOffset? dateCreatedAfter,
        DateTimeOffset? dateCreatedBefore,
        string? friendlyName,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default6("/v1/CompositionHooks"),
            [],
            [new Param("Enabled", enabled),
                new Param("DateCreatedAfter", dateCreatedAfter?.ToIso8601()),
                new Param("DateCreatedBefore", dateCreatedBefore?.ToIso8601()),
                new Param("FriendlyName", friendlyName),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListCompositionHookResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Recording composition hooks
    /// </summary>
    /// <param name="sid">The SID of the CompositionHook resource to update.</param>
    /// <param name="friendlyName"></param>
    /// <param name="enabled"></param>
    /// <param name="videoLayout"></param>
    /// <param name="audioSources"></param>
    /// <param name="audioSourcesExcluded"></param>
    /// <param name="trim"></param>
    /// <param name="format"></param>
    /// <param name="resolution"></param>
    /// <param name="statusCallback"></param>
    /// <param name="statusCallbackMethod"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="VideoV1CompositionHook"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<VideoV1CompositionHook> UpdateCompositionHook(string sid,
        string friendlyName,
        bool? enabled,
        object? videoLayout,
        IReadOnlyList<string>? audioSources,
        IReadOnlyList<string>? audioSourcesExcluded,
        bool? trim,
        CompositionHookEnumFormat? format,
        string? resolution,
        string? statusCallback,
        AmdStatusCallbackMethod? statusCallbackMethod,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default6("/v1/CompositionHooks/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FriendlyName", friendlyName),
                    new Param("Enabled", enabled),
                    new Param("VideoLayout", videoLayout),
                    new Param("AudioSources", audioSources),
                    new Param("AudioSourcesExcluded", audioSourcesExcluded),
                    new Param("Trim", trim),
                    new Param("Format", format),
                    new Param("Resolution", resolution),
                    new Param("StatusCallback", statusCallback),
                    new Param("StatusCallbackMethod", statusCallbackMethod)]),
            JsonResponse.Create<VideoV1CompositionHook>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
