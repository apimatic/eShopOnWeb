using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Notifications;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Notifications;

/// <summary>
/// Wire-level tests for the Twilio gateway. They drive the real SDK over a stubbed HttpClient, so
/// they assert the request shapes and the error boundary without sending a real (billable) message.
/// </summary>
public class TwilioSmsGatewayTests
{
    private static TwilioSettings Settings(string? baseUrl = null) => new()
    {
        AccountSid = "ACtestaccountsid",
        AuthToken = "testtoken",
        FromNumber = "+15550001111",
        MessagingServiceSid = "MGtestservice",
        BaseUrl = baseUrl
    };

    private static TwilioSmsGateway Gateway(StubHttpMessageHandler handler, string? baseUrl = null)
    {
        var settings = Settings(baseUrl);
        var client = TwilioClientFactory.Create(settings, new HttpClient(handler));
        return new TwilioSmsGateway(client, settings);
    }

    [Fact]
    public async Task SendAsync_posts_to_messages_endpoint_and_reads_back_sid_and_status()
    {
        var handler = StubHttpMessageHandler.Json(HttpStatusCode.Created,
            """{ "sid": "SM123", "status": "queued", "to": "+14165551234", "from": "+15550001111", "body": "hi" }""");
        var gateway = Gateway(handler);

        var result = await gateway.SendAsync("+14165551234", "hi", CancellationToken.None);

        Assert.Equal("SM123", result.Sid);
        Assert.Equal("queued", result.Status);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Contains("/Messages.json", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("ACtestaccountsid", handler.LastRequest!.RequestUri!.AbsolutePath);
        // Sent from the configured From number so reconciliation can count it later.
        Assert.Contains("15550001111", handler.LastBody);
    }

    [Fact]
    public async Task SendAsync_translates_provider_error_to_gateway_exception_with_status()
    {
        var handler = StubHttpMessageHandler.Json(HttpStatusCode.BadRequest,
            """{ "code": 21211, "message": "Invalid 'To' Phone Number", "status": 400 }""");
        var gateway = Gateway(handler);

        var ex = await Assert.ThrowsAsync<SmsGatewayException>(
            () => gateway.SendAsync("+1999", "hi", CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task ValidateNumberAsync_returns_canonical_form_when_valid()
    {
        var handler = StubHttpMessageHandler.Json(HttpStatusCode.OK,
            """{ "valid": true, "phone_number": "+14165551234", "country_code": "CA" }""");
        var gateway = Gateway(handler);

        var result = await gateway.ValidateNumberAsync("(416) 555-1234", CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal("+14165551234", result.CanonicalNumber);
    }

    [Fact]
    public async Task ValidateNumberAsync_reports_invalid_without_throwing()
    {
        var handler = StubHttpMessageHandler.Json(HttpStatusCode.OK,
            """{ "valid": false, "validation_errors": ["TOO_SHORT"] }""");
        var gateway = Gateway(handler);

        var result = await gateway.ValidateNumberAsync("123", CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Null(result.CanonicalNumber);
    }

    [Fact]
    public async Task RedactAsync_posts_an_empty_body_to_the_message_resource()
    {
        var handler = StubHttpMessageHandler.Json(HttpStatusCode.OK,
            """{ "sid": "SM123", "status": "delivered", "body": null }""");
        var gateway = Gateway(handler);

        await gateway.RedactAsync("SM123", CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Contains("/Messages/SM123.json", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("Body=", handler.LastBody);
    }

    [Fact]
    public async Task BaseUrl_override_is_used_for_messaging_calls()
    {
        var handler = StubHttpMessageHandler.Json(HttpStatusCode.Created,
            """{ "sid": "SM1", "status": "queued" }""");
        var gateway = Gateway(handler, baseUrl: "https://messaging.example.test");

        await gateway.SendAsync("+14165551234", "hi", CancellationToken.None);

        Assert.Equal("messaging.example.test", handler.LastRequest!.RequestUri!.Host);
    }

    [Fact]
    public async Task ListSentMessagesAsync_filters_by_configured_from_number()
    {
        var handler = StubHttpMessageHandler.Json(HttpStatusCode.OK,
            """{ "messages": [ { "sid": "SM1", "from": "+15550001111", "to": "+14165551234", "status": "delivered" } ], "next_page_uri": null }""");
        var gateway = Gateway(handler);

        var results = await gateway.ListSentMessagesAsync(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("SM1", results[0].Sid);
        // Asked the provider for our own sending number's traffic (server-side From filter).
        Assert.Contains("15550001111", handler.LastRequest!.RequestUri!.Query);
    }
}
