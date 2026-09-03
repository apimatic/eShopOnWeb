using System;
using System.Collections.Generic;
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

public sealed class FlexV1PluginConfigurationApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal FlexV1PluginConfigurationApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    public Task<FlexV1PluginConfiguration> CreatePluginConfiguration(string? flexMetadata,
        string name,
        IReadOnlyList<object>? plugins,
        string? description,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/PluginService/Configurations"),
            [],
            [],
            [new HeaderParam("Flex-Metadata", flexMetadata), new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Name", name),
                    new Param("Plugins", plugins),
                    new Param("Description", description)]),
            JsonResponse.Create<FlexV1PluginConfiguration>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    public Task<FlexV1PluginConfiguration> FetchPluginConfiguration(string sid,
        string? flexMetadata,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/PluginService/Configurations/{Sid}"),
            [new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Flex-Metadata", flexMetadata)],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<FlexV1PluginConfiguration>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    public Task<ListPluginConfigurationResponse> ListPluginConfiguration(long? pageSize,
        int? page,
        string? pageToken,
        string? flexMetadata,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/PluginService/Configurations"),
            [],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [new HeaderParam("Flex-Metadata", flexMetadata)],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListPluginConfigurationResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
