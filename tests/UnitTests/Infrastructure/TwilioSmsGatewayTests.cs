using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Twilio;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure;

/// <summary>
/// Exercises the Twilio gateway through the SDK's own HttpClient seam with a stub handler, so no real
/// network call happens. Proves the wire behaviour the integration depends on: response mapping, error
/// translation, the server-side From filter, and the single-send duplicate guard.
/// </summary>
public class TwilioSmsGatewayTests
{
    private const string FromNumber = "+15550001111";

    private static TwilioSettings Settings() => new()
    {
        AccountSid = "ACtest00000000000000000000000000000",
        AuthToken = "test-token",
        FromNumber = FromNumber,
        MessagingServiceSid = "MGtest00000000000000000000000000000"
    };

    private static TwilioSmsGateway BuildGateway(StubHandler stub, RetryOptions? retry = null)
    {
        var httpClient = new HttpClient(new SingleSendGuardHandler { InnerHandler = stub });
        var settings = Settings();
        var options = new TwilioSdkClientOptions
        {
            Environment = ServerEnvironment.Production,
            AccountSidAuthToken = new BasicAuthCredentials { Username = settings.AccountSid, Password = settings.AuthToken },
            Retry = retry ?? (RetryOptions.Default() with { MaxRetries = 1, Timeout = TimeSpan.FromSeconds(5) })
        };
        var client = new TwilioSdkClient(httpClient, options);
        return new TwilioSmsGateway(client, Options.Create(settings));
    }

    [Fact]
    public async Task SendAsync_Success_ReturnsSidAndStatus_AndPostsToProvider()
    {
        var stub = new StubHandler(_ => Json(HttpStatusCode.Created, """{ "sid": "SM123", "status": "queued" }"""));
        var gateway = BuildGateway(stub);

        var result = await gateway.SendAsync("+15145550100", "hello");

        Assert.Equal("SM123", result.MessageSid);
        Assert.Equal("queued", result.Status);
        Assert.Equal(HttpMethod.Post, stub.LastRequest!.Method);
    }

    [Fact]
    public async Task SendAsync_ProviderError_ThrowsWithStatus_AndCode_ButNeverThePhoneNumber()
    {
        const string to = "+15005550009";
        var stub = new StubHandler(_ => Json(HttpStatusCode.BadRequest,
            $$"""{ "code": 21211, "message": "The 'To' number {{to}} is not a valid phone number." }"""));
        var gateway = BuildGateway(stub);

        var ex = await Assert.ThrowsAsync<SmsGatewayException>(() => gateway.SendAsync(to, "hi"));

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("21211", ex.Message);       // provider code surfaced
        Assert.DoesNotContain(to, ex.Message);       // the shopper's number is never echoed
    }

    [Fact]
    public async Task SendAsync_TransportRetry_IsBlockedBySingleSendGuard_OnlyOneRequestReachesProvider()
    {
        var stub = new StubHandler(_ => throw new HttpRequestException("connection reset"));
        // Default retries so a transport failure WOULD be retried; the guard must stop the resend.
        var gateway = BuildGateway(stub, RetryOptions.Default() with { MaxRetries = 3, Delay = TimeSpan.FromMilliseconds(1) });

        await Assert.ThrowsAsync<SmsGatewayException>(() => gateway.SendAsync("+15145550100", "hi"));

        Assert.Equal(1, stub.Requests.Count); // no duplicate send
    }

    [Fact]
    public async Task ListOwnMessages_FiltersByConfiguredFromNumber_ServerSide()
    {
        var stub = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{ "messages": [ { "sid": "SM1", "status": "delivered", "date_sent": "Fri, 24 May 2019 17:44:46 +0000", "error_code": null } ], "next_page_uri": null }"""));
        var gateway = BuildGateway(stub);

        var messages = await gateway.ListOwnMessagesAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow);

        Assert.Single(messages);
        Assert.Equal("SM1", messages[0].Sid);
        Assert.Equal("delivered", messages[0].Status);

        var query = stub.LastRequest!.RequestUri!.Query;
        Assert.Contains("From=" + Uri.EscapeDataString(FromNumber), query); // From filter sent to the provider
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();
        public HttpRequestMessage? LastRequest => Requests.Count == 0 ? null : Requests[^1];

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }
}
