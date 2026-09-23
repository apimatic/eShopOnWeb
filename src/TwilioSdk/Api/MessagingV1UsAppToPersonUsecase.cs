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

public sealed class MessagingV1UsAppToPersonUsecase
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal MessagingV1UsAppToPersonUsecase(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Messaging Service Use Case resource. Fetch possible use cases for service. The Use Cases API returns an empty list if there is an issue with the customer's A2P brand registration. This Brand cannot register any campaign use cases. Customers are requested to contact support with their A2P brand information.
    /// </summary>
    /// <param name="messagingServiceSid">The SID of the <see href="https://www.twilio.com/docs/messaging/api/service-resource">Messaging Service</see> to fetch the resource from.</param>
    /// <param name="brandRegistrationSid">The unique string to identify the A2P brand.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="MessagingV1ServiceUsAppToPersonUsecase"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    public Task<MessagingV1ServiceUsAppToPersonUsecase> FetchUsAppToPersonUsecase(string messagingServiceSid,
        string? brandRegistrationSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default1("/v1/Services/{MessagingServiceSid}/Compliance/Usa2p/Usecases"),
            [new TemplateParam("MessagingServiceSid", messagingServiceSid)],
            [new Param("BrandRegistrationSid", brandRegistrationSid)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<MessagingV1ServiceUsAppToPersonUsecase>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
