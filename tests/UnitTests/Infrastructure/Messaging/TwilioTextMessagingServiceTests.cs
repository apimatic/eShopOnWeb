using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Messaging;
using Microsoft.Extensions.Options;
using NSubstitute;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Servers;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Messaging;

public class TwilioTextMessagingServiceTests
{
    private const string AccountSid = "ACtest00000000000000000000000000";
    private const string FromNumber = "+15550001111";
    private const string MessagingServiceSid = "MGtest00000000000000000000000000";

    private static TwilioTextMessagingService CreateService(HttpMessageHandler handler, string? baseUrl = null)
    {
        var httpClient = new HttpClient(handler);
        var options = new TwilioSdkClientOptions
        {
            Environment = ServerEnvironment.Production,
            AccountSidAuthToken = new BasicAuthCredentials { Username = AccountSid, Password = "test-token" }
        };
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            // Mirrors TwilioMessagingRegistration: the override is scoped to the messaging node.
            options.Server.Default.Production.BaseUrl = baseUrl;
        }
        var client = new TwilioSdkClient(httpClient, options);
        var settings = Options.Create(new TwilioSettings
        {
            AccountSid = AccountSid,
            AuthToken = "test-token",
            FromNumber = FromNumber,
            MessagingServiceSid = MessagingServiceSid
        });
        return new TwilioTextMessagingService(client, settings, Substitute.For<IAppLogger<TwilioTextMessagingService>>());
    }

    private static TwilioTextMessagingService CreateServiceWithGuard(StubHandler stub)
    {
        var guarded = new SendOnceGuardHandler { InnerHandler = stub };
        return CreateService(guarded);
    }

    [Fact]
    public async Task ValidatePhoneNumber_ValidNumber_ReturnsCanonicalForm()
    {
        var stub = StubHandler.ReturningJson(HttpStatusCode.OK,
            """{ "valid": true, "phone_number": "+14155552671", "national_format": "(415) 555-2671", "country_code": "US", "calling_country_code": "1" }""");
        var service = CreateService(stub);

        var result = await service.ValidatePhoneNumberAsync("4155552671");

        Assert.True(result.IsValid);
        Assert.Equal("+14155552671", result.CanonicalNumber);
        Assert.Contains("/v2/PhoneNumbers/", stub.LastRequest!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task ValidatePhoneNumber_InvalidNumber_ReturnsNotValidWithReasons()
    {
        var stub = StubHandler.ReturningJson(HttpStatusCode.OK,
            """{ "valid": false, "phone_number": null, "validation_errors": ["TOO_SHORT"] }""");
        var service = CreateService(stub);

        var result = await service.ValidatePhoneNumberAsync("123");

        Assert.False(result.IsValid);
        Assert.Null(result.CanonicalNumber);
        Assert.Contains("TOO_SHORT", result.ValidationErrors);
    }

    [Fact]
    public async Task SendAsync_PostsToMessagesWithFromNumberAndBody()
    {
        var stub = StubHandler.ReturningJson(HttpStatusCode.Created,
            """{ "sid": "SMaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "status": "queued", "to": "+14155552671", "from": "+15550001111", "body": "hello" }""");
        var service = CreateServiceWithGuard(stub);

        var result = await service.SendAsync("+14155552671", "hello");

        Assert.Equal("SMaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", result.ProviderMessageId);
        Assert.Equal("queued", result.Status);

        var request = stub.LastRequest!;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"/2010-04-01/Accounts/{AccountSid}/Messages.json", request.RequestUri!.AbsolutePath);
        var sentBody = stub.LastRequestBody;
        Assert.Contains("To=%2B14155552671", sentBody);
        Assert.Contains($"From={Uri.EscapeDataString(FromNumber)}", sentBody);
        Assert.Contains("Body=hello", sentBody);
    }

    [Fact]
    public async Task SendAsync_ProviderRejection_ThrowsMessagingProviderExceptionWithStatus()
    {
        var stub = StubHandler.ReturningJson(HttpStatusCode.BadRequest,
            """{ "code": 21211, "message": "The 'To' number is not a valid phone number." }""");
        var service = CreateServiceWithGuard(stub);

        var ex = await Assert.ThrowsAsync<MessagingProviderException>(
            () => service.SendAsync("+14155552671", "hello"));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        // The provider body (which embeds the destination number) must not leak into the message.
        Assert.DoesNotContain("+14155552671", ex.Message);
    }

    [Fact]
    public async Task ScheduleAsync_UsesMessagingServiceAndFixedSchedule()
    {
        var stub = StubHandler.ReturningJson(HttpStatusCode.Created,
            """{ "sid": "SMbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "status": "scheduled" }""");
        var service = CreateServiceWithGuard(stub);
        var sendAt = DateTimeOffset.UtcNow.AddDays(3);

        var result = await service.ScheduleAsync("+14155552671", "how was it?", sendAt);

        Assert.Equal("SMbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", result.ProviderMessageId);
        Assert.Equal("scheduled", result.Status);

        var sentBody = stub.LastRequestBody;
        Assert.Contains("ScheduleType=fixed", sentBody);
        Assert.Contains($"MessagingServiceSid={MessagingServiceSid}", sentBody);
        Assert.Contains("SendAt=", sentBody);
    }

    [Fact]
    public async Task CancelScheduledAsync_PostsCanceledStatus()
    {
        var stub = StubHandler.ReturningJson(HttpStatusCode.OK,
            """{ "sid": "SMbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "status": "canceled" }""");
        var service = CreateService(stub);

        await service.CancelScheduledAsync("SMbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

        var request = stub.LastRequest!;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("/Messages/SMbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.json", request.RequestUri!.AbsolutePath);
        Assert.Contains("Status=canceled", stub.LastRequestBody);
    }

    [Fact]
    public async Task RedactBodyAsync_PostsEmptyBody()
    {
        var stub = StubHandler.ReturningJson(HttpStatusCode.OK,
            """{ "sid": "SMaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "status": "sent", "body": "" }""");
        var service = CreateService(stub);

        await service.RedactBodyAsync("SMaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        Assert.Contains("Body=", stub.LastRequestBody);
        Assert.DoesNotContain("Status=", stub.LastRequestBody);
    }

    [Fact]
    public async Task GetDeliveryOutcomeAsync_ReturnsProviderStatus()
    {
        var stub = StubHandler.ReturningJson(HttpStatusCode.OK,
            """{ "sid": "SMaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "status": "undelivered", "error_code": 30034, "error_message": "carrier refused" }""");
        var service = CreateService(stub);

        var outcome = await service.GetDeliveryOutcomeAsync("SMaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        Assert.Equal("undelivered", outcome.Status);
        Assert.Equal(30034, outcome.ErrorCode);
        Assert.Equal(HttpMethod.Get, stub.LastRequest!.Method);
    }

    [Fact]
    public async Task ListSentMessagesAsync_FiltersByFromNumberAndDateRangeServerSide()
    {
        var stub = StubHandler.ReturningJson(HttpStatusCode.OK,
            """{ "messages": [ { "sid": "SM1", "status": "delivered", "to": "+14155552671", "from": "+15550001111", "date_sent": "Wed, 31 Aug 2026 10:00:00 +0000", "body": "hi" } ], "next_page_uri": null, "page": 0, "page_size": 1000 }""");
        var service = CreateService(stub);
        var from = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 8, 31, 23, 59, 59, TimeSpan.Zero);

        var result = await service.ListSentMessagesAsync(from, to);

        Assert.False(result.Truncated);
        var message = Assert.Single(result.Messages);
        Assert.Equal("SM1", message.ProviderMessageId);
        Assert.Equal("delivered", message.Status);

        var query = stub.LastRequest!.RequestUri!.Query;
        Assert.Contains($"From={Uri.EscapeDataString(FromNumber)}", query);
        Assert.Contains("DateSent%3C=", query.Replace("%3c", "%3C", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("DateSent%3E=", query.Replace("%3e", "%3E", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BaseUrlOverride_RetargetsMessagingCallsButNotLookup()
    {
        var stub = StubHandler.ReturningJson(HttpStatusCode.Created,
            """{ "sid": "SMcccccccccccccccccccccccccccccccc", "status": "queued" }""");
        var service = CreateService(stub, baseUrl: "https://twilio-mock.example.com");

        await service.SendAsync("+14155552671", "hello");

        Assert.Equal("twilio-mock.example.com", stub.LastRequest!.RequestUri!.Host);

        var lookupStub = StubHandler.ReturningJson(HttpStatusCode.OK,
            """{ "valid": true, "phone_number": "+14155552671" }""");
        var lookupService = CreateService(lookupStub, baseUrl: "https://twilio-mock.example.com");

        await lookupService.ValidatePhoneNumberAsync("4155552671");

        Assert.Equal("lookups.twilio.com", lookupStub.LastRequest!.RequestUri!.Host);
    }

    [Fact]
    public async Task SendAsync_TransportFailureRetry_IsRefusedBySendOnceGuard()
    {
        var stub = new StubHandler(_ => throw new HttpRequestException("connection reset"));
        var service = CreateServiceWithGuard(stub);

        var ex = await Assert.ThrowsAsync<MessagingProviderException>(
            () => service.SendAsync("+14155552671", "hello"));

        // The first attempt may have reached the provider; the retry must not go out.
        Assert.Single(stub.Requests);
    }
}
