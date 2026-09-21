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

public sealed class FlexV2WebChannels
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal FlexV2WebChannels(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    public Task<FlexV2WebChannel> CreateWebChannel2(string? uiVersion,
        string addressSid,
        string? chatFriendlyName,
        string? customerFriendlyName,
        string? preEngagementData,
        string? identity,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default13("/v2/WebChats"),
            [],
            [],
            [new HeaderParam("Ui-Version", uiVersion), new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("AddressSid", addressSid),
                    new Param("ChatFriendlyName", chatFriendlyName),
                    new Param("CustomerFriendlyName", customerFriendlyName),
                    new Param("PreEngagementData", preEngagementData),
                    new Param("Identity", identity)]),
            JsonResponse.Create<FlexV2WebChannel>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
