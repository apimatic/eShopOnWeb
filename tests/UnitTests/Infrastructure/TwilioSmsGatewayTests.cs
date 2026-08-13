using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services;
using NSubstitute;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Servers;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure;

public class TwilioSmsGatewayTests
{
    private static TwilioSettings Settings() => new()
    {
        AccountSid = "AC_test",
        AuthToken = "token_test",
        FromNumber = "+15550001111",
        MessagingServiceSid = "MG_test"
    };

    private static TwilioSmsGateway GatewayOver(StubHandler handler)
    {
        var options = new TwilioSdkClientOptions
        {
            Environment = ServerEnvironment.Production,
            AccountSidAuthToken = new BasicAuthCredentials { Username = "AC_test", Password = "token_test" }
        };
        var client = new TwilioSdkClient(new HttpClient(handler), options);
        var logger = Substitute.For<IAppLogger<TwilioSmsGateway>>();
        return new TwilioSmsGateway(client, Settings(), logger);
    }

    [Fact]
    public async Task SendAsync_WhenProviderRejects_ReturnsFailureAndDoesNotThrow()
    {
        // A provider rejection must never surface as an exception — the order operation must still succeed.
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"code":21211,"message":"Invalid 'To'"}""",
                System.Text.Encoding.UTF8, "application/json")
        });
        var gateway = GatewayOver(handler);

        var result = await gateway.SendAsync("+15005550006", "hello");

        Assert.False(result.Accepted);
        Assert.Null(result.ProviderMessageSid);
    }

    [Fact]
    public async Task SendAsync_WhenTransportFails_ReturnsFailureAndDoesNotThrow()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection reset"));
        var gateway = GatewayOver(handler);

        var result = await gateway.SendAsync("+15550002222", "hello");

        Assert.False(result.Accepted);
    }

    [Fact]
    public async Task SendAsync_OnSuccess_ReturnsSidAndStatus()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"sid":"SM123","status":"queued"}""",
                System.Text.Encoding.UTF8, "application/json")
        });
        var gateway = GatewayOver(handler);

        var result = await gateway.SendAsync("+15550002222", "hello");

        Assert.True(result.Accepted);
        Assert.Equal("SM123", result.ProviderMessageSid);
        Assert.Equal("queued", result.Status);
    }

    [Fact]
    public async Task ListSentMessages_FiltersByConfiguredFromNumber_ServerSide()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"messages":[{"sid":"SM1","status":"delivered"}],"next_page_uri":null}""",
                System.Text.Encoding.UTF8, "application/json")
        });
        var gateway = GatewayOver(handler);

        var results = await gateway.ListSentMessagesAsync(
            DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow);

        Assert.Single(results);
        Assert.Equal("SM1", results[0].Sid);
        // The provider is asked only for this application's sending number's messages (server-side From filter).
        var query = handler.LastRequest!.RequestUri!.Query;
        Assert.Contains("From=", query);
        Assert.Contains("15550001111", Uri.UnescapeDataString(query));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();
        public HttpRequestMessage? LastRequest => Requests.Count == 0 ? null : Requests[^1];

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }
}
