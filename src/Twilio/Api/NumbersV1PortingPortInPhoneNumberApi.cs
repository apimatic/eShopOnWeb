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

public sealed class NumbersV1PortingPortInPhoneNumberApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal NumbersV1PortingPortInPhoneNumberApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Allows to cancel a port in request phone number by SID
    /// </summary>
    /// <param name="portInRequestSid">The SID of the Port In request. This is a unique identifier of the port in request.</param>
    /// <param name="phoneNumberSid">The SID of the Port In request phone number. This is a unique identifier of the phone number.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Allows to cancel a port in request phone number by SID
    /// </remarks>
    public Task DeletePortingPortInPhoneNumber(string portInRequestSid,
        string phoneNumberSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v1/Porting/PortIn/{PortInRequestSid}/PhoneNumber/{PhoneNumberSid}"),
            [new TemplateParam("PortInRequestSid", portInRequestSid),
                new TemplateParam("PhoneNumberSid", phoneNumberSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            VoidResponse.Instance,
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Fetch a phone number by port in request SID and phone number SID
    /// </summary>
    /// <param name="portInRequestSid">The SID of the Port In request. This is a unique identifier of the port in request.</param>
    /// <param name="phoneNumberSid">The SID of the Phone number. This is a unique identifier of the phone number.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="NumbersV1PortingPortInPhoneNumber"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch a phone number by port in request SID and phone number SID
    /// </remarks>
    public Task<NumbersV1PortingPortInPhoneNumber> FetchPortingPortInPhoneNumber(string portInRequestSid,
        string phoneNumberSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v1/Porting/PortIn/{PortInRequestSid}/PhoneNumber/{PhoneNumberSid}"),
            [new TemplateParam("PortInRequestSid", portInRequestSid),
                new TemplateParam("PhoneNumberSid", phoneNumberSid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<NumbersV1PortingPortInPhoneNumber>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
