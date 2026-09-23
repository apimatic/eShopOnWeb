using System;
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

public sealed class FlexV1PluginReleaseApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal FlexV1PluginReleaseApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    public Task<FlexV1PluginRelease> CreatePluginRelease(string? flexMetadata,
        string configurationId,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/PluginService/Releases"),
            [],
            [],
            [new HeaderParam("Flex-Metadata", flexMetadata), new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("ConfigurationId", configurationId)]),
            JsonResponse.Create<FlexV1PluginRelease>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    public Task<FlexV1PluginRelease> FetchPluginRelease(string sid,
        string? flexMetadata,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/PluginService/Releases/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Flex-Metadata", flexMetadata)],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<FlexV1PluginRelease>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    public Task<ListPluginReleaseResponse> ListPluginRelease(long? pageSize,
        int? page,
        string? pageToken,
        string? flexMetadata,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/PluginService/Releases"),
            [],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [new HeaderParam("Flex-Metadata", flexMetadata)],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListPluginReleaseResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
