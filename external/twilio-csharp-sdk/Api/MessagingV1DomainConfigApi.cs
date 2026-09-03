using System;
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

public sealed class MessagingV1DomainConfigApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal MessagingV1DomainConfigApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    public Task<MessagingV1DomainConfig> FetchDomainConfig(string domainSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default1("/v1/LinkShortening/Domains/{DomainSid}/Config"),
            [new TemplateParam("DomainSid", domainSid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<MessagingV1DomainConfig>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    public Task<MessagingV1DomainConfig> UpdateDomainConfig(string domainSid,
        string? fallbackUrl,
        string? callbackUrl,
        bool? continueOnFailure,
        bool? disableHttps,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default1("/v1/LinkShortening/Domains/{DomainSid}/Config"),
            [new TemplateParam("DomainSid", domainSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("FallbackUrl", fallbackUrl),
                    new Param("CallbackUrl", callbackUrl),
                    new Param("ContinueOnFailure", continueOnFailure),
                    new Param("DisableHttps", disableHttps)]),
            JsonResponse.Create<MessagingV1DomainConfig>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
