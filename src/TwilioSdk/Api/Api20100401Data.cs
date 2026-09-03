using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TwilioSdk.Core;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Request;
using TwilioSdk.Core.Response;

namespace TwilioSdk.Api;

public sealed class Api20100401Data
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Api20100401Data(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Fetch an instance of a result payload
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that created the Recording AddOnResult Payload resource to fetch.</param>
    /// <param name="referenceSid">The SID of the recording to which the AddOnResult resource that contains the payload to fetch belongs.</param>
    /// <param name="addOnResultSid">The SID of the AddOnResult to which the payload to fetch belongs.</param>
    /// <param name="payloadSid">The Twilio-provided string that uniquely identifies the Recording AddOnResult Payload resource to fetch.</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Fetch an instance of a result payload
    /// </remarks>
    public Task FetchRecordingAddOnResultPayloadData(string accountSid,
        string referenceSid,
        string addOnResultSid,
        string payloadSid,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Recordings/{ReferenceSid}/AddOnResults/{AddOnResultSid}/Payloads/{PayloadSid}/Data.json"),
            [new TemplateParam("AccountSid", accountSid),
                new TemplateParam("ReferenceSid", referenceSid),
                new TemplateParam("AddOnResultSid", addOnResultSid),
                new TemplateParam("PayloadSid", payloadSid)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            VoidResponse.Instance,
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
