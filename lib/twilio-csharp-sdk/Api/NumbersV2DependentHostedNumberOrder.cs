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
using Twilio.Models.Enums;

namespace Twilio.Api;

public sealed class NumbersV2DependentHostedNumberOrder
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal NumbersV2DependentHostedNumberOrder(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Retrieve a list of dependent HostedNumberOrders belonging to the AuthorizationDocument.
    /// </summary>
    /// <param name="signingDocumentSid">A 34 character string that uniquely identifies the LOA document associated with this HostedNumberOrder.</param>
    /// <param name="status">Status of an instance resource. It can hold one of the values: 1. opened 2. signing, 3. signed LOA, 4. canceled, 5. failed. See the section entitled <see href="https://www.twilio.com/docs/phone-numbers/hosted-numbers/hosted-numbers-api/authorization-document-resource#status-values">Status Values</see> for more information on each of these statuses.</param>
    /// <param name="phoneNumber">An E164 formatted phone number hosted by this HostedNumberOrder.</param>
    /// <param name="incomingPhoneNumberSid">A 34 character string that uniquely identifies the IncomingPhoneNumber resource created by this HostedNumberOrder.</param>
    /// <param name="friendlyName">A human readable description of this resource, up to 128 characters.</param>
    /// <param name="pageSize">How many resources to return in each list page. The default is 50, and the maximum is 1000.</param>
    /// <param name="page">The page index. This value is simply for client state.</param>
    /// <param name="pageToken">The page token. This is provided by the API.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ListDependentHostedNumberOrderResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve a list of dependent HostedNumberOrders belonging to the AuthorizationDocument.
    /// </remarks>
    public Task<ListDependentHostedNumberOrderResponse> ListDependentHostedNumberOrder(string signingDocumentSid,
        DependentHostedNumberOrderEnumStatus? status,
        string? phoneNumber,
        string? incomingPhoneNumberSid,
        string? friendlyName,
        long? pageSize,
        int? page,
        string? pageToken,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v2/HostedNumber/AuthorizationDocuments/{SigningDocumentSid}/DependentHostedNumberOrders"),
            [new TemplateParam("SigningDocumentSid", signingDocumentSid)],
            [new Param("Status", status),
                new Param("PhoneNumber", phoneNumber),
                new Param("IncomingPhoneNumberSid", incomingPhoneNumberSid),
                new Param("FriendlyName", friendlyName),
                new Param("PageSize", pageSize),
                new Param("Page", page),
                new Param("PageToken", pageToken)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<ListDependentHostedNumberOrderResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
