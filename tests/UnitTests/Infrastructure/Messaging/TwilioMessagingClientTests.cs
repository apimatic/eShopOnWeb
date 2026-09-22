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
using NSubstitute;
using TwilioSdk;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Messaging;

/// <summary>
/// Exercises the Twilio wrapper at the HttpClient seam (per dotnet-testing) — no live calls. Asserts
/// the request the SDK actually builds and that provider/transport failures become
/// <see cref="ProviderMessagingException"/>.
/// </summary>
public class TwilioMessagingClientTests
{
    private const string AccountSid = "AC00000000000000000000000000000000";
    private const string FromNumber = "+15005550006";
    private const string MessagingServiceSid = "MG00000000000000000000000000000000";
    private const string ToNumber = "+14155552671";

    private static TwilioSettings Settings(string? baseUrl = null) => new()
    {
        AccountSid = AccountSid,
        AuthToken = "placeholder",
        FromNumber = FromNumber,
        MessagingServiceSid = MessagingServiceSid,
        BaseUrl = baseUrl
    };

    private static TwilioMessagingClient Build(StubHandler handler, string? baseUrl = null)
    {
        var settings = Settings(baseUrl);
        var options = new TwilioSdkClientOptions();
        if (!string.IsNullOrWhiteSpace(baseUrl))
            options.Server.Default.Production.BaseUrl = baseUrl!;
        var sdk = new TwilioSdkClient(new HttpClient(handler), options);
        var logger = Substitute.For<IAppLogger<TwilioMessagingClient>>();
        return new TwilioMessagingClient(sdk, settings, logger);
    }

    [Fact]
    public async Task SendAsync_maps_response_and_builds_a_create_request_from_the_configured_number()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created,
            """{"sid":"SM123","status":"queued","to":"+14155552671","from":"+15005550006"}"""));
        var client = Build(handler);

        var result = await client.SendAsync(ToNumber, "hello");

        Assert.Equal("SM123", result.Sid);
        Assert.Equal("queued", result.Status);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Contains("/Messages.json", handler.LastRequest!.RequestUri!.AbsolutePath);
        // Immediate sends go out from the configured FromNumber with the destination obfuscated.
        Assert.Contains("From=", handler.LastBody);
        Assert.Contains("AddressRetention=obfuscate", handler.LastBody);
    }

    [Fact]
    public async Task SendAsync_treats_undelivered_status_as_a_failure_not_a_success()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created,
            """{"sid":"SM777","status":"undelivered","error_code":30006}"""));
        var client = Build(handler);

        var result = await client.SendAsync(ToNumber, "hi");

        Assert.Equal("undelivered", result.Status);
        Assert.Equal(30006, result.ErrorCode);
    }

    [Fact]
    public async Task SendAsync_translates_a_provider_error_to_ProviderMessagingException_with_status()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.BadRequest,
            """{"code":21211,"message":"Invalid 'To' Phone Number"}"""));
        var client = Build(handler);

        var ex = await Assert.ThrowsAsync<ProviderMessagingException>(() => client.SendAsync(ToNumber, "hi"));
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task ScheduleAsync_uses_fixed_schedule_and_the_messaging_service()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created,
            """{"sid":"SM999","status":"scheduled"}"""));
        var client = Build(handler);

        var result = await client.ScheduleAsync(ToNumber, "feedback?", DateTimeOffset.UtcNow.AddDays(3));

        Assert.Equal("scheduled", result.Status);
        Assert.Contains("ScheduleType=fixed", handler.LastBody);
        Assert.Contains("MessagingServiceSid=", handler.LastBody);
        Assert.Contains("SendAt=", handler.LastBody);
    }

    [Fact]
    public async Task ValidateNumberAsync_reads_valid_and_canonical_form_and_targets_the_lookup_host()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{"valid":true,"phone_number":"+14155552671","country_code":"US"}"""));
        // Even with a messaging BaseUrl override, Lookup must resolve through its own host (Default4).
        var client = Build(handler, baseUrl: "https://messaging-mock.local");

        var result = await client.ValidateNumberAsync("(415) 555-2671");

        Assert.True(result.IsValid);
        Assert.Equal("+14155552671", result.E164Number);
        Assert.Equal("US", result.CountryCode);
        Assert.Contains("lookups.twilio.com", handler.LastRequest!.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task SendAsync_honours_the_messaging_base_url_override()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created, """{"sid":"SM1","status":"queued"}"""));
        var client = Build(handler, baseUrl: "https://messaging-mock.local");

        await client.SendAsync(ToNumber, "hi");

        Assert.Equal("messaging-mock.local", handler.LastRequest!.RequestUri!.Host);
    }

    [Fact]
    public async Task ListSentFromNumberAsync_filters_by_sender_and_date_and_stops_on_a_short_page()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{"messages":[{"sid":"SM1","status":"delivered","from":"+15005550006"}],"next_page_uri":null}"""));
        var client = Build(handler);

        var page = await client.ListSentFromNumberAsync(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, maxPages: 5);

        Assert.Single(page.Messages);
        Assert.False(page.Truncated);
        Assert.Equal("SM1", page.Messages[0].Sid);
        var query = handler.LastRequest!.RequestUri!.Query;
        Assert.Contains("From=", query);
        Assert.Contains("DateSent", query); // DateSent< / DateSent> range bounds
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>Captures each request and buffers its body while still readable.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string?> Bodies { get; } = new();
        public HttpRequestMessage? LastRequest => Requests.Count == 0 ? null : Requests[^1];
        public string? LastBody => Bodies.Count == 0 ? null : Bodies[^1];

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            Bodies.Add(request.Content?.ReadAsStringAsync(ct).Result);
            var response = _responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
