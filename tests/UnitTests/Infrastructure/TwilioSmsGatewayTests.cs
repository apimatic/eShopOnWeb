using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Notifications;
using Microsoft.Extensions.Options;
using NSubstitute;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Servers;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure;

public class TwilioSmsGatewayTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();
        public HttpRequestMessage LastRequest => Requests[^1];
        // Captured at send time — HttpClient disposes the request content once the call returns.
        public string? LastRequestBody { get; private set; }

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            LastRequestBody = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            return Task.FromResult(_responder(request));
        }
    }

    private static readonly TwilioSettings Settings = new()
    {
        AccountSid = "ACtestaccountsid",
        AuthToken = "testtoken",
        FromNumber = "+15551230000",
        MessagingServiceSid = "MGtestservice"
    };

    private static TwilioSmsGateway GatewayReturning(HttpStatusCode status, string json, out StubHandler handler)
    {
        handler = new StubHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });
        var client = new TwilioSdkClient(new HttpClient(handler), new TwilioSdkClientOptions
        {
            Environment = ServerEnvironment.Production,
            AccountSidAuthToken = new BasicAuthCredentials { Username = Settings.AccountSid, Password = Settings.AuthToken }
        });
        return new TwilioSmsGateway(client, Options.Create(Settings), Substitute.For<IAppLogger<TwilioSmsGateway>>());
    }

    [Fact]
    public async Task SendAsync_PostsToMessages_AndReturnsSidAndStatus()
    {
        var gateway = GatewayReturning(HttpStatusCode.Created,
            """{ "sid": "SMabc123", "status": "queued", "from": "+15551230000", "to": "+16135550142", "body": "hi" }""",
            out var handler);

        var result = await gateway.SendAsync("+16135550142", "hi");

        Assert.Equal("SMabc123", result.Sid);
        Assert.Equal("queued", result.Status);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.Contains("/Accounts/ACtestaccountsid/Messages", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Contains("To", handler.LastRequestBody);
        Assert.Contains("hi", handler.LastRequestBody);
    }

    [Fact]
    public async Task ProviderError_IsTranslatedToSmsGatewayException_WithStatusAndCode()
    {
        var gateway = GatewayReturning(HttpStatusCode.BadRequest,
            """{ "code": 21211, "message": "The 'To' number is not a valid phone number.", "more_info": "https://www.twilio.com/docs/errors/21211" }""",
            out _);

        var ex = await Assert.ThrowsAsync<SmsGatewayException>(() => gateway.SendAsync("+1000", "hi"));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Equal(21211, ex.ProviderErrorCode);
        // The provider's free-text message (which can echo the destination number) is not surfaced.
        Assert.DoesNotContain("+1000", ex.Message);
    }

    [Fact]
    public async Task ListSentMessages_FiltersByFromNumber_AndReadsMessages()
    {
        var gateway = GatewayReturning(HttpStatusCode.OK,
            """{ "messages": [ { "sid": "SM1", "status": "delivered", "from": "+15551230000", "date_sent": "Tue, 10 Aug 2026 12:00:00 +0000" } ], "page": 0 }""",
            out var handler);

        var from = DateTimeOffset.UtcNow.AddDays(-1);
        var to = DateTimeOffset.UtcNow;
        var messages = await gateway.ListSentMessagesAsync(from, to);

        Assert.Single(messages);
        Assert.Equal("SM1", messages[0].Sid);
        Assert.Equal("delivered", messages[0].Status);
        // The provider filters server-side by the configured From number.
        Assert.Contains("From", handler.LastRequest.RequestUri!.Query);
    }
}
