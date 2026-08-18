using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Services.Twilio;
using Microsoft.Extensions.Options;
using TwilioSdk;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Notifications;

/// <summary>
/// Tests the Twilio SDK seam directly: a fake <see cref="HttpMessageHandler"/> stands in for the network, so
/// no real messages are sent. Asserts the outgoing request shape and the failure translation — the success
/// body deserialization is exercised against the live provider in end-to-end verification.
/// </summary>
public class TwilioSmsSenderTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> Bodies { get; } = new();
        public HttpRequestMessage LastRequest => Requests[^1];
        public string LastBody => Bodies[^1];

        public StubHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder) => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
            Requests.Add(request);
            Bodies.Add(body);
            return _responder(request, body);
        }
    }

    private static IOptions<TwilioSettings> Settings() => Options.Create(new TwilioSettings
    {
        AccountSid = "ACtest",
        AuthToken = "token",
        FromNumber = "+15550001111",
        MessagingServiceSid = "MGtestservice"
    });

    private static TwilioSmsSender SenderOver(HttpMessageHandler handler)
        => new(new TwilioSdkClient(new HttpClient(handler), new TwilioSdkClientOptions()), Settings());

    private static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Send_posts_to_messages_from_the_configured_number()
    {
        var stub = new StubHandler((_, _) => Json(HttpStatusCode.Created, "{}"));
        var sender = SenderOver(stub);

        try { await sender.SendAsync("+15551119999", "hello", default); } catch { /* body deser not under test */ }

        Assert.Equal(HttpMethod.Post, stub.LastRequest.Method);
        Assert.Contains("Messages", stub.LastRequest.RequestUri!.AbsolutePath);
        var body = Uri.UnescapeDataString(stub.LastBody);
        Assert.Contains("15551119999", body);   // to
        Assert.Contains("15550001111", body);    // from = configured sending number
        Assert.Contains("hello", body);          // body text
    }

    [Fact]
    public async Task Schedule_sends_fixed_schedule_via_the_messaging_service()
    {
        var stub = new StubHandler((_, _) => Json(HttpStatusCode.Created, "{}"));
        var sender = SenderOver(stub);

        try { await sender.ScheduleAsync("+15551119999", "later", DateTimeOffset.UtcNow.AddDays(3), default); }
        catch { /* body deser not under test */ }

        var body = Uri.UnescapeDataString(stub.LastBody);
        Assert.Contains("fixed", body);            // ScheduleType=fixed
        Assert.Contains("MGtestservice", body);    // scheduling goes via the messaging service
    }

    [Fact]
    public async Task List_asks_the_provider_to_filter_by_the_configured_from_number()
    {
        var stub = new StubHandler((_, _) => Json(HttpStatusCode.OK, "{\"messages\":[]}"));
        var sender = SenderOver(stub);

        try { await sender.ListSentMessagesAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, default); }
        catch { /* body deser not under test */ }

        Assert.Equal(HttpMethod.Get, stub.LastRequest.Method);
        var query = Uri.UnescapeDataString(stub.LastRequest.RequestUri!.Query);
        Assert.Contains("15550001111", query);   // From filter is the configured sending number
    }

    [Fact]
    public async Task Redact_updates_the_message_by_sid()
    {
        var stub = new StubHandler((_, _) => Json(HttpStatusCode.OK, "{}"));
        var sender = SenderOver(stub);

        try { await sender.RedactBodyAsync("SMabc123", default); } catch { /* body deser not under test */ }

        Assert.Equal(HttpMethod.Post, stub.LastRequest.Method);
        Assert.Contains("SMabc123", stub.LastRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Provider_error_status_becomes_SmsProviderException_carrying_the_status()
    {
        var stub = new StubHandler((_, _) => Json(HttpStatusCode.BadRequest, "{\"code\":21211,\"message\":\"invalid To\"}"));
        var sender = SenderOver(stub);

        var ex = await Assert.ThrowsAsync<SmsProviderException>(() => sender.SendAsync("+15551119999", "hi", default));
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task Invalid_number_at_lookup_is_rejected_not_thrown()
    {
        var stub = new StubHandler((_, _) => Json(HttpStatusCode.NotFound, "{}"));
        var sender = SenderOver(stub);

        var result = await sender.ValidateAsync("+1", default);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task A_transport_failure_never_sends_the_message_more_than_once()
    {
        // The stub throws (a connection reset), which the SDK retries on any verb. The guard must refuse the
        // re-send so the provider receives at most one POST — the outcome is reported as indeterminate.
        var stub = new StubHandler((_, _) => throw new HttpRequestException("connection reset"));
        var guarded = new MessageSendGuardHandler { InnerHandler = stub };
        var sender = new TwilioSmsSender(new TwilioSdkClient(new HttpClient(guarded), new TwilioSdkClientOptions()), Settings());

        var ex = await Assert.ThrowsAsync<SmsProviderException>(() => sender.SendAsync("+15551119999", "once", default));

        Assert.True(ex.OutcomeUnknown);
        Assert.Equal(1, stub.Requests.Count(r => r.Method == HttpMethod.Post));
    }
}
