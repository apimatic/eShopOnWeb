using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Twilio;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Notifications;

public class TwilioSmsGatewayTests
{
    private const string From = "+15550000000";

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> Bodies { get; } = new();
        public HttpRequestMessage? LastRequest => Requests.Count == 0 ? null : Requests[^1];

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct));
            return _responder(request);
        }
    }

    private static (TwilioSmsGateway gateway, StubHandler handler) BuildGateway(HttpStatusCode status, string json)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });
        var client = new TwilioSdkClient(new HttpClient(handler), new TwilioSdkClientOptions
        {
            AccountSidAuthToken = new BasicAuthCredentials { Username = "ACtest", Password = "secret" }
        });
        var settings = Options.Create(new TwilioSettings
        {
            AccountSid = "ACtest",
            AuthToken = "secret",
            FromNumber = From,
            MessagingServiceSid = "MGtest"
        });
        return (new TwilioSmsGateway(client, settings), handler);
    }

    [Fact]
    public async Task Send_PostsMessage_AndParsesSidAndStatus()
    {
        var (gateway, handler) = BuildGateway(HttpStatusCode.Created,
            """{ "sid": "SM123", "status": "queued", "error_code": null, "error_message": null }""");

        var result = await gateway.SendAsync("+14165551234", "Hello there");

        Assert.Equal("SM123", result.ProviderSid);
        Assert.Equal("queued", result.Status);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Contains("Messages.json", handler.LastRequest!.RequestUri!.AbsolutePath);
        var body = handler.Bodies[^1];
        Assert.Contains("To", body);
        Assert.Contains("Hello", Uri.UnescapeDataString(body));
    }

    [Fact]
    public async Task Send_OnProviderError_ThrowsSmsGatewayException_CarryingStatus()
    {
        var (gateway, _) = BuildGateway(HttpStatusCode.Unauthorized,
            """{ "code": 20003, "message": "Authenticate" }""");

        var ex = await Assert.ThrowsAsync<SmsGatewayException>(() => gateway.SendAsync("+14165551234", "hi"));
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public async Task ListSentMessages_FiltersByConfiguredFromNumber_InTheRequest()
    {
        var (gateway, handler) = BuildGateway(HttpStatusCode.OK,
            """{ "messages": [ { "sid": "SM1", "status": "delivered", "date_sent": "Mon, 01 Jan 2024 00:00:00 +0000", "error_code": null } ], "next_page_uri": null }""");

        var results = await gateway.ListSentMessagesAsync(
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero));

        var record = Assert.Single(results);
        Assert.Equal("SM1", record.Sid);
        Assert.Equal("delivered", record.Status);
        // The From filter is asked of the provider, not applied after the fact.
        Assert.Contains("From=", handler.LastRequest!.RequestUri!.Query);
    }

    [Fact]
    public async Task RedactContent_PostsAnEmptyBodyToTheMessage()
    {
        var (gateway, handler) = BuildGateway(HttpStatusCode.OK,
            """{ "sid": "SMx", "status": "delivered" }""");

        await gateway.RedactContentAsync("SMx");

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Contains("SMx", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("Body=", handler.Bodies[^1]);
    }
}
