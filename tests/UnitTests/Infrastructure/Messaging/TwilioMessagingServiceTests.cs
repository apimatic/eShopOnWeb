using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Messaging;
using Microsoft.Extensions.Options;
using NSubstitute;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Messaging;

/// <summary>
/// Tests the Twilio wrapper through its one real seam — the <see cref="HttpClient"/> the SDK client is built
/// on — so no live call is made. These assert the wrapper's own behaviour: it reads the provider's SID and
/// status back on success, and translates a provider error into a single <see cref="SmsGatewayException"/>
/// carrying the status and provider code.
/// </summary>
public class TwilioMessagingServiceTests
{
    private const string AccountSid = "ACxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx";
    private const string FromNumber = "+15005550006";

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();
        public HttpRequestMessage Last => Requests[^1];

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (TwilioMessagingService service, StubHandler handler) Build(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new StubHandler(responder);
        var options = new TwilioSdkClientOptions
        {
            AccountSidAuthToken = new BasicAuthCredentials { Username = AccountSid, Password = "token" }
        };
        var client = new TwilioSdkClient(new HttpClient(handler), options);
        var settings = Options.Create(new TwilioSettings
        {
            AccountSid = AccountSid,
            AuthToken = "token",
            FromNumber = FromNumber,
            MessagingServiceSid = "MGxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
        });
        var logger = Substitute.For<IAppLogger<TwilioMessagingService>>();
        return (new TwilioMessagingService(client, settings, logger), handler);
    }

    [Fact]
    public async Task SendAsync_OnSuccess_ReadsBackProviderSidAndStatus()
    {
        var (service, handler) = Build(_ => Json(HttpStatusCode.Created,
            """{ "sid": "SM123", "status": "queued", "to": "+15195550123", "from": "+15005550006", "body": "hi" }"""));

        var result = await service.SendAsync("+15195550123", "hi");

        Assert.Equal("SM123", result.ProviderMessageSid);
        Assert.Equal("queued", result.Status);
        Assert.Contains("Messages.json", handler.Last.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task SendAsync_OnProviderError_TranslatesToGatewayExceptionWithStatusAndCode()
    {
        var (service, _) = Build(_ => Json(HttpStatusCode.BadRequest,
            """{ "code": 21211, "message": "The 'To' number is not a valid phone number.", "status": 400 }"""));

        var ex = await Assert.ThrowsAsync<SmsGatewayException>(() => service.SendAsync("+1555", "hi"));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Equal(21211, ex.ProviderErrorCode);
    }

    [Fact]
    public async Task ListSentFromConfiguredNumberAsync_FiltersByFromNumberServerSide()
    {
        var (service, handler) = Build(_ => Json(HttpStatusCode.OK,
            """{ "messages": [ { "sid": "SM1", "from": "+15005550006", "to": "+15195550123", "status": "delivered", "date_sent": "Fri, 24 May 2019 17:44:46 +0000" } ], "next_page_uri": null }"""));

        var from = DateTimeOffset.Parse("2019-05-01T00:00:00Z");
        var to = DateTimeOffset.Parse("2019-06-01T00:00:00Z");
        var records = await service.ListSentFromConfiguredNumberAsync(from, to);

        Assert.Single(records);
        Assert.Equal("SM1", records[0].Sid);
        // The From filter is applied at the provider (in the query string), not after the fact.
        Assert.Contains("From=", Uri.UnescapeDataString(handler.Last.RequestUri!.Query));
    }
}
