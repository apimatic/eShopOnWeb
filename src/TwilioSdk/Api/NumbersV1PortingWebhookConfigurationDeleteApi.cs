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
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Api;

public sealed class NumbersV1PortingWebhookConfigurationDeleteApi
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal NumbersV1PortingWebhookConfigurationDeleteApi(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// Allows the client to delete a webhook configuration.
    /// </summary>
    /// <param name="webhookType">The webhook type for the configuration to be delete. <c>PORT_IN</c>, <c>PORT_OUT</c></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Allows the client to delete a webhook configuration.
    /// </remarks>
    public Task DeletePortingWebhookConfigurationDelete(PortingWebhookConfigurationDeleteEnumWebhookType webhookType,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default5("/v1/Porting/Configuration/Webhook/{WebhookType}"),
            [new TemplateParam("WebhookType", webhookType)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            VoidResponse.Instance,
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
