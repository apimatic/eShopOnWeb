using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.OrderNotificationServiceTests;

/// <summary>
/// Tests the real gateway against a stubbed HttpClient (the SDK's own test seam), so the request the SDK
/// actually builds and the gateway's mapping/translation are verified without a network call.
/// </summary>
public class TwilioMessagingGatewayTests
{
    private const string FromNumber = "+15005550006";

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            var response = _responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private static TwilioMessagingGateway BuildGateway(StubHandler handler)
    {
        var options = new TwilioSdkClientOptions
        {
            AccountSidAuthToken = new BasicAuthCredentials { Username = "AC123", Password = "secret" }
        };
        var client = new TwilioSdkClient(new HttpClient(handler), options);
        var settings = Options.Create(new TwilioSettings
        {
            AccountSid = "AC123",
            AuthToken = "secret",
            FromNumber = FromNumber,
            MessagingServiceSid = "MG123"
        });
        return new TwilioMessagingGateway(client, settings, NullLogger<TwilioMessagingGateway>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task SendMapsProviderStatusToState()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created,
            """{ "sid": "SM1", "status": "queued", "to": "+15145550123", "from": "+15005550006" }"""));
        var gateway = BuildGateway(handler);

        var result = await gateway.SendAsync("+15145550123", "hello", CancellationToken.None);

        Assert.Equal("SM1", result.Sid);
        Assert.Equal(NotificationDeliveryState.Pending, result.State);   // queued -> pending
        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
    }

    [Fact]
    public async Task ReconciliationAsksProviderForConfiguredNumber()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{ "messages": [ { "sid": "SM9", "status": "delivered", "from": "+15005550006", "date_sent": "Tue, 21 Jan 2026 10:00:00 +0000" } ], "next_page_uri": null }"""));
        var gateway = BuildGateway(handler);

        var listing = await gateway.ListSentAsync(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Single(listing.Messages);
        Assert.Equal("SM9", listing.Messages[0].Sid);
        // The provider was asked to filter by our own From number (not filtered here after the fact).
        var query = handler.Requests[0].RequestUri!.Query;
        Assert.Contains("From=", query);
        Assert.Contains("15005550006", Uri.UnescapeDataString(query));
    }

    [Fact]
    public async Task ProviderErrorIsTranslatedToGatewayException()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.BadRequest,
            """{ "code": 21211, "message": "Invalid 'To' Phone Number" }"""));
        var gateway = BuildGateway(handler);

        var ex = await Assert.ThrowsAsync<SmsGatewayException>(
            () => gateway.SendAsync("+1555", "hello", CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
        Assert.False(ex.OutcomeUnknown);   // a definite provider rejection, not an unknown transport outcome
    }
}
