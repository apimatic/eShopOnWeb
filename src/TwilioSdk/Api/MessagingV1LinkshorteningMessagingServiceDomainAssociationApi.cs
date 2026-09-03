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

public sealed class MessagingV1LinkshorteningMessagingServiceDomainAssociationApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal MessagingV1LinkshorteningMessagingServiceDomainAssociationApi(RawClient rawClient,
        Server server,
        AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    public Task<MessagingV1LinkshorteningMessagingServiceDomainAssociation> FetchLinkshorteningMessagingServiceDomainAssociation(string messagingServiceSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default1("/v1/LinkShortening/MessagingServices/{MessagingServiceSid}/Domain"),
            [new TemplateParam("MessagingServiceSid", messagingServiceSid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<MessagingV1LinkshorteningMessagingServiceDomainAssociation>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
