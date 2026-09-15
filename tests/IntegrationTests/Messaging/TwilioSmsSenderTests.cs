using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Messaging;
using Microsoft.Extensions.Options;
using TwilioSdk;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Messaging;

/// <summary>
/// Exercises <see cref="TwilioSmsSender"/> against a fake HTTP transport (the SDK's constructor seam), so
/// the wire contract and the error boundary are verified without a real provider call.
/// </summary>
public class TwilioSmsSenderTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> Bodies { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct));
            return _responder(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static TwilioSmsSender CreateSender(StubHandler handler)
    {
        var client = new TwilioSdkClient(new HttpClient(handler), new TwilioSdkClientOptions());
        var settings = Options.Create(new TwilioSettings
        {
            AccountSid = "ACtest",
            AuthToken = "token",
            FromNumber = "+15005550006",
            MessagingServiceSid = "MGtest",
            RequestTimeoutSeconds = 30
        });
        return new TwilioSmsSender(client, settings);
    }

    [Fact]
    public async Task SendPostsToTheMessagesResourceAndReturnsTheSid()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created, """{"sid":"SM123","status":"queued"}"""));
        var sender = CreateSender(handler);

        var result = await sender.SendAsync("+15145551234", "hello");

        Assert.Equal("SM123", result.MessageSid);
        Assert.Equal("queued", result.Status);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Contains("/Messages.json", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Contains("Body", handler.Bodies[0]);
    }

    [Fact]
    public async Task AProviderErrorBecomesAnSmsProviderExceptionCarryingTheStatus()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.BadRequest, """{"code":21211,"message":"Invalid 'To'"}"""));
        var sender = CreateSender(handler);

        var ex = await Assert.ThrowsAsync<SmsProviderException>(() => sender.SendAsync("+1", "hi"));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        // The provider's response body (which can echo the destination number) is not surfaced.
        Assert.DoesNotContain("+1", ex.Message);
    }

    [Fact]
    public async Task ValidateReturnsCanonicalNumberForAValidLookup()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"valid":true,"phone_number":"+15145551234"}"""));
        var sender = CreateSender(handler);

        var result = await sender.ValidateAsync("514 555 1234");

        Assert.True(result.IsValid);
        Assert.Equal("+15145551234", result.CanonicalNumber);
    }

    [Fact]
    public async Task ValidateTreatsA404AsAnUnusableNumberRatherThanAnOutage()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.NotFound, """{"code":20404}"""));
        var sender = CreateSender(handler);

        var result = await sender.ValidateAsync("nonsense");

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ReconciliationAsksTheProviderForTheConfiguredFromNumber()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{"messages":[{"sid":"SMa","status":"delivered","from":"+15005550006"}],"next_page_uri":null}"""));
        var sender = CreateSender(handler);

        var messages = await sender.ListSentFromConfiguredNumberAsync(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow);

        Assert.Equal("SMa", Assert.Single(messages).Sid);
        // The From filter is asked of the provider, not applied after the fact.
        Assert.Contains("From=", handler.Requests[0].RequestUri!.Query);
    }
}
