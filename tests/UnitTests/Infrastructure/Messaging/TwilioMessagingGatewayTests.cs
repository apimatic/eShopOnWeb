using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Messaging;
using Microsoft.Extensions.Options;
using NSubstitute;
using TwilioSdk;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Messaging;

public class TwilioMessagingGatewayTests
{
    private static TwilioSettings Settings() => new()
    {
        AccountSid = "AC00000000000000000000000000000000",
        AuthToken = "token",
        FromNumber = "+15005550006",
        MessagingServiceSid = "MG00000000000000000000000000000000",
        CallTimeoutSeconds = 30,
        ListPageSize = 1000,
        MaxReconciliationPages = 50
    };

    private static TwilioMessagingGateway Gateway(StubHttpMessageHandler handler)
    {
        var client = new TwilioSdkClient(new HttpClient(handler), new TwilioSdkClientOptions());
        var logger = Substitute.For<IAppLogger<TwilioMessagingGateway>>();
        return new TwilioMessagingGateway(client, Options.Create(Settings()), logger);
    }

    [Fact]
    public async Task SendAsync_PostsToMessages_WithFromAndBody_AndParsesResponse()
    {
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.Created,
            """{ "sid": "SM123", "status": "queued", "to": "+15005550006", "from": "+15005550006" }"""));

        var result = await Gateway(handler).SendAsync("+15145550123", "Hello", CancellationToken.None);

        Assert.Equal("SM123", result.Sid);
        Assert.Equal("queued", result.Status);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Contains("/Messages.json", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("To=%2B15145550123", handler.LastBody);   // form-encoded destination
        Assert.Contains("From=%2B15005550006", handler.LastBody);  // the configured sending number
        Assert.Contains("Body=Hello", handler.LastBody);
    }

    [Fact]
    public async Task ScheduleAsync_UsesMessagingServiceAndFixedSchedule()
    {
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.Created,
            """{ "sid": "SM999", "status": "scheduled" }"""));

        var result = await Gateway(handler).ScheduleAsync("+15145550123", "How was it?",
            DateTimeOffset.UtcNow.AddDays(3), CancellationToken.None);

        Assert.Equal("scheduled", result.Status);
        Assert.Contains("ScheduleType=fixed", handler.LastBody);
        Assert.Contains("MessagingServiceSid=MG", handler.LastBody);
        Assert.DoesNotContain("From=", handler.LastBody);          // scheduled goes via the service, not From
    }

    [Fact]
    public async Task RedactContentAsync_PostsEmptyBody()
    {
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK,
            """{ "sid": "SM123", "status": "sent" }"""));

        await Gateway(handler).RedactContentAsync("SM123", CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Contains("Body=", handler.LastBody);                // empty body -> redaction
    }

    [Fact]
    public async Task CancelScheduledAsync_PostsCanceledStatus()
    {
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK,
            """{ "sid": "SM999", "status": "canceled" }"""));

        await Gateway(handler).CancelScheduledAsync("SM999", CancellationToken.None);

        Assert.Contains("Status=canceled", handler.LastBody);
    }

    [Fact]
    public async Task SendAsync_TranslatesProviderErrorToGatewayException()
    {
        var handler = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(HttpStatusCode.BadRequest,
            """{ "code": 21211, "message": "Invalid 'To'", "more_info": "https://twilio", "status": 400 }"""));

        var ex = await Assert.ThrowsAsync<ProviderGatewayException>(
            () => Gateway(handler).SendAsync("+1", "Hi", CancellationToken.None));
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task SendAsync_TranslatesTransportFailure()
    {
        var handler = new StubHttpMessageHandler((_, _) => throw new HttpRequestException("connection reset"));

        var ex = await Assert.ThrowsAsync<ProviderGatewayException>(
            () => Gateway(handler).SendAsync("+15145550123", "Hi", CancellationToken.None));
        Assert.Null(ex.StatusCode);   // no HTTP status for a transport failure
    }

    [Fact]
    public async Task ListForFromNumber_WalksAllPages()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            var query = request.RequestUri!.Query;
            // Page 0 has a next page; page 1 is the last.
            if (query.Contains("Page=1"))
                return StubHttpMessageHandler.Json(HttpStatusCode.OK,
                    """{ "messages": [ { "sid": "SM2", "status": "delivered" } ], "next_page_uri": null }""");

            return StubHttpMessageHandler.Json(HttpStatusCode.OK,
                """{ "messages": [ { "sid": "SM1", "status": "sent" } ], "next_page_uri": "/next?Page=1" }""");
        });

        var listing = await Gateway(handler).ListForFromNumberAsync(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(2, listing.Messages.Count);
        Assert.Equal(2, listing.PagesRead);
        Assert.False(listing.Truncated);
        Assert.Contains("From=%2B15005550006", handler.LastRequest!.RequestUri!.Query); // asks the provider for THIS number
    }
}
