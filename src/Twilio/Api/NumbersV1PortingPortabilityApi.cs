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

public sealed class NumbersV1PortingPortabilityApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal NumbersV1PortingPortabilityApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Check if a single phone number can be ported to Twilio
    /// </summary>
    /// <param name="phoneNumber">Phone number to check portability in e164 format.</param>
    /// <param name="targetAccountSid">Account Sid to which the number will be ported. This can be used to determine if a sub account already has the number in its inventory or a different sub account. If this is not provided, the authenticated account will be assumed to be the target account.</param>
    /// <param name="addressSid">Address Sid of customer to which the number will be ported.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="NumbersV1PortingPortability"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Check if a single phone number can be ported to Twilio
    /// </remarks>
    public Task<NumbersV1PortingPortability> FetchPortingPortability(string phoneNumber,
        string? targetAccountSid,
        string? addressSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v1/Porting/Portability/PhoneNumber/{PhoneNumber}"),
            [new TemplateParam("PhoneNumber", phoneNumber)],
            [new Param("TargetAccountSid", targetAccountSid), new Param("AddressSid", addressSid)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<NumbersV1PortingPortability>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
