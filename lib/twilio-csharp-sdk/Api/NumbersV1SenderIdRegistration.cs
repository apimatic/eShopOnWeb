using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Twilio.Core;
using Twilio.Core.Exceptions;
using Twilio.Core.Models;
using Twilio.Core.Request;
using Twilio.Core.Response;
using Twilio.Errors;
using Twilio.Models;

namespace Twilio.Api;

public sealed class NumbersV1SenderIdRegistration
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal NumbersV1SenderIdRegistration(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Create Sender ID Registration
    /// </summary>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="NumbersV1CreateEmbeddedRegistrationResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateSenderIdRegistrationError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Creates a new sender ID registration and initializes an embedded Persona inquiry session. Returns registration details and embedded session credentials for rendering the Compliance Embeddable UI.
    /// </remarks>
    public Task<NumbersV1CreateEmbeddedRegistrationResponse> CreateSenderIdRegistration(NumbersV1CreateEmbeddedRegistrationRequest body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v1/SenderIdRegistrations"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<NumbersV1CreateEmbeddedRegistrationResponse>(),
            CreateSenderIdRegistrationErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
