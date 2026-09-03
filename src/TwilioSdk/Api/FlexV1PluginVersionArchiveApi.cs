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

public sealed class FlexV1PluginVersionArchiveApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal FlexV1PluginVersionArchiveApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    public Task<FlexV1PluginVersionArchive> UpdatePluginVersionArchive(string pluginSid,
        string sid,
        string? flexMetadata,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/PluginService/Plugins/{PluginSid}/Versions/{Sid}/Archive"),
            [new TemplateParam("PluginSid", pluginSid), new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Flex-Metadata", flexMetadata), new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            EmptyBody.Instance,
            JsonResponse.Create<FlexV1PluginVersionArchive>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
