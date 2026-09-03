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

public sealed class FlexV1PluginVersions
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal FlexV1PluginVersions(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    public Task<FlexV1PluginPluginVersion> CreatePluginVersion(string pluginSid,
        string? flexMetadata,
        string version,
        string pluginUrl,
        string? changelog,
        bool? @private,
        string? cliVersion,
        string? validateStatus,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/PluginService/Plugins/{PluginSid}/Versions"),
            [new TemplateParam("PluginSid", pluginSid)],
            [],
            [new HeaderParam("Flex-Metadata", flexMetadata), new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("Version", version),
                    new Param("PluginUrl", pluginUrl),
                    new Param("Changelog", changelog),
                    new Param("Private", @private),
                    new Param("CliVersion", cliVersion),
                    new Param("ValidateStatus", validateStatus)]),
            JsonResponse.Create<FlexV1PluginPluginVersion>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    public Task<FlexV1PluginPluginVersion> FetchPluginVersion(string pluginSid,
        string sid,
        string? flexMetadata,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/PluginService/Plugins/{PluginSid}/Versions/{Sid}"),
            [new TemplateParam("PluginSid", pluginSid), new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Flex-Metadata", flexMetadata)],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<FlexV1PluginPluginVersion>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    public Task<ListPluginVersionResponse> ListPluginVersion(string pluginSid,
        long? pageSize,
        int? page,
        string? pageToken,
        string? flexMetadata,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/PluginService/Plugins/{PluginSid}/Versions"),
            [new TemplateParam("PluginSid", pluginSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [new HeaderParam("Flex-Metadata", flexMetadata)],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListPluginVersionResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
