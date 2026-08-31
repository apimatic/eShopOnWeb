#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Messaging;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Servers;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Messaging;

public class TwilioMessagingServiceTests
{
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

    private static readonly TwilioSettings Settings = new TwilioSettings
    {
        AccountSid = "AC00000000000000000000000000000000",
        AuthToken = "test-token",
        FromNumber = "+17540000000",
        MessagingServiceSid = "MG00000000000000000000000000000000"
    };

    private static TwilioMessagingService ServiceReturning(StubHandler handler)
    {
        var client = new TwilioSdkClient(new HttpClient(handler), new TwilioSdkClientOptions
        {
            Environment = ServerEnvironment.Production,
            AccountSidAuthToken = new BasicAuthCredentials { Username = Settings.AccountSid, Password = Settings.AuthToken }
        });
        return new TwilioMessagingService(client, Settings);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new HttpResponseMessage(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

    [Fact]
    public async Task ValidateAsync_ValidNumber_ReturnsProviderCanonicalForm()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{ "calling_country_code": "1", "country_code": "CA", "phone_number": "+18254751588", "national_format": "(825) 475-1588", "valid": true, "validation_errors": [] }"""));
        var service = ServiceReturning(handler);

        var result = await service.ValidateAsync("825-475-1588");

        Assert.True(result.IsValid);
        Assert.Equal("+18254751588", result.CanonicalNumber);
    }

    [Fact]
    public async Task ValidateAsync_InvalidNumber_IsRejectedWithReason()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{ "calling_country_code": "1", "country_code": null, "phone_number": null, "national_format": null, "valid": false, "validation_errors": ["NOT_A_NUMBER"] }"""));
        var service = ServiceReturning(handler);

        var result = await service.ValidateAsync("not-a-number");

        Assert.False(result.IsValid);
        Assert.Null(result.CanonicalNumber);
        Assert.Contains("NOT_A_NUMBER", result.FailureReason);
    }

    [Fact]
    public async Task SendMessageAsync_MapsProviderResponse()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created,
            """{ "sid": "SM123", "status": "queued", "to": "+18254751588", "from": "+17540000000", "body": "hello", "date_sent": null, "error_code": null, "error_message": null }"""));
        var service = ServiceReturning(handler);

        var message = await service.SendMessageAsync("+18254751588", "hello");

        Assert.Equal("SM123", message.Sid);
        Assert.Equal("queued", message.Status);
        Assert.Equal("+18254751588", message.To);
    }

    [Fact]
    public async Task SendMessageAsync_ProviderRejection_ThrowsMessagingExceptionCarryingStatus()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.BadRequest,
            """{ "code": 21211, "message": "The 'To' number is not a valid phone number.", "status": 400, "more_info": "https://www.twilio.com/docs/errors/21211" }"""));
        var service = ServiceReturning(handler);

        var ex = await Assert.ThrowsAsync<MessagingException>(
            () => service.SendMessageAsync("+10000000000", "hello"));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task ListMessagesFromSenderAsync_FiltersBySenderAndDateRange()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{ "messages": [ { "sid": "SM1", "status": "delivered", "to": "+18254751588", "from": "+17540000000", "date_sent": "Mon, 31 Aug 2026 10:00:00 +0000" } ], "end": 0, "first_page_uri": "", "next_page_uri": null, "page": 0, "page_size": 1000, "previous_page_uri": null, "start": 0, "uri": "" }"""));
        var service = ServiceReturning(handler);

        var from = new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var messages = await service.ListMessagesFromSenderAsync(from, to);

        var message = Assert.Single(messages);
        Assert.Equal("SM1", message.Sid);
        Assert.Equal("delivered", message.Status);

        var query = handler.LastRequest!.RequestUri!.Query;
        Assert.Contains("From=", query);
        Assert.Contains("%2B17540000000", query); // the configured sending number, URL-encoded
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(query, "DateSent").Count); // DateSent< and DateSent>
    }

    [Fact]
    public async Task SingleFlightSendGuard_BlocksRetryOfMessageCreateWithinScope()
    {
        var calls = 0;
        var stub = new StubHandler(_ =>
        {
            calls++;
            return Json(HttpStatusCode.Created, """{ "sid": "SM1", "status": "queued" }""");
        });
        var guard = new SingleFlightSendGuard { InnerHandler = stub };
        var httpClient = new HttpClient(guard);

        using (SingleFlightSendGuard.BeginScope())
        {
            await httpClient.PostAsync("https://api.twilio.com/2010-04-01/Accounts/AC1/Messages.json", new FormUrlEncodedContent(new Dictionary<string, string>()));
            await Assert.ThrowsAsync<DuplicateSendBlockedException>(() =>
                httpClient.PostAsync("https://api.twilio.com/2010-04-01/Accounts/AC1/Messages.json", new FormUrlEncodedContent(new Dictionary<string, string>())));
        }

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task SingleFlightSendGuard_DoesNotTouchReadsOrUpdates()
    {
        var stub = new StubHandler(_ => Json(HttpStatusCode.OK, """{ "sid": "SM1", "status": "canceled" }"""));
        var guard = new SingleFlightSendGuard { InnerHandler = stub };
        var httpClient = new HttpClient(guard);

        using (SingleFlightSendGuard.BeginScope())
        {
            await httpClient.GetAsync("https://api.twilio.com/2010-04-01/Accounts/AC1/Messages/SM1.json");
            await httpClient.PostAsync("https://api.twilio.com/2010-04-01/Accounts/AC1/Messages/SM1.json", new FormUrlEncodedContent(new Dictionary<string, string>()));
            await httpClient.PostAsync("https://api.twilio.com/2010-04-01/Accounts/AC1/Messages/SM1.json", new FormUrlEncodedContent(new Dictionary<string, string>()));
        }

        Assert.Equal(3, stub.Requests.Count);
    }
}
