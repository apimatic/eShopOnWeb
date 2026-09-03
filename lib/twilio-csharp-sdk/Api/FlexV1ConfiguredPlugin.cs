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

public sealed class FlexV1ConfiguredPlugin
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal FlexV1ConfiguredPlugin(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    public Task<FlexV1PluginConfigurationConfiguredPlugin> FetchConfiguredPlugin(string configurationSid,
        string pluginSid,
        string? flexMetadata,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/PluginService/Configurations/{ConfigurationSid}/Plugins/{PluginSid}"),
            [new TemplateParam("ConfigurationSid", configurationSid), new TemplateParam("PluginSid", pluginSid)],
            [],
            [new HeaderParam("Flex-Metadata", flexMetadata)],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<FlexV1PluginConfigurationConfiguredPlugin>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    public Task<ListConfiguredPluginResponse> ListConfiguredPlugin(string configurationSid,
        long? pageSize,
        int? page,
        string? pageToken,
        string? flexMetadata,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v1/PluginService/Configurations/{ConfigurationSid}/Plugins"),
            [new TemplateParam("ConfigurationSid", configurationSid)],
            [new Param("PageSize", pageSize), new Param("Page", page), new Param("PageToken", pageToken)],
            [new HeaderParam("Flex-Metadata", flexMetadata)],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListConfiguredPluginResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
