using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Sms;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Sms;

/// <summary>
/// Offline tests of the Twilio implementation using the SDK's HttpClient seam. They prove the shape
/// of what the integration sends and how it maps the provider's responses — without live calls.
/// </summary>
public class TwilioSmsNotificationServiceTests
{
    private const string FromNumber = "+15550000001";
    private const string MessagingServiceSid = "MGtestservice";

    private static TwilioSmsNotificationService BuildService(StubHttpMessageHandler handler)
    {
        var options = new TwilioSdkClientOptions
        {
            AccountSidAuthToken = new BasicAuthCredentials { Username = "ACtest", Password = "secret" }
        };
        var client = new TwilioSdkClient(new HttpClient(handler), options);
        var settings = Options.Create(new TwilioSettings
        {
            AccountSid = "ACtest",
            AuthToken = "secret",
            FromNumber = FromNumber,
            MessagingServiceSid = MessagingServiceSid
        });
        return new TwilioSmsNotificationService(client, settings);
    }

    private static StubHttpMessageHandler Ok(string json) =>
        new(_ => (HttpStatusCode.OK, json));

    [Fact]
    public async Task Send_PostsToMessages_FromConfiguredNumber_WithBody()
    {
        var handler = Ok("""{ "sid": "SMabc", "status": "queued" }""");
        var service = BuildService(handler);

        var result = await service.SendAsync("+15551234567", "hello there");

        Assert.Equal("SMabc", result.ProviderMessageSid);
        Assert.Equal("queued", result.Status);
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.EndsWith("/Messages.json", handler.LastRequest.RequestUri!.AbsolutePath);
        var body = Uri.UnescapeDataString(handler.LastBody);
        Assert.Contains($"From={FromNumber}", body);
        Assert.Contains("To=+15551234567", body);
        Assert.Contains("Body=", body); // form-encoded space becomes '+', so assert the field, not the phrase
        Assert.DoesNotContain("MessagingServiceSid", body); // immediate send uses the number, not the service
    }

    [Fact]
    public async Task Schedule_UsesMessagingService_AndFixedScheduleType()
    {
        var handler = Ok("""{ "sid": "SMsched", "status": "scheduled" }""");
        var service = BuildService(handler);

        var result = await service.ScheduleAsync("+15551234567", "how was delivery?", DateTimeOffset.UtcNow.AddDays(3));

        Assert.Equal("scheduled", result.Status);
        var body = Uri.UnescapeDataString(handler.LastBody);
        Assert.Contains($"MessagingServiceSid={MessagingServiceSid}", body);
        Assert.Contains("ScheduleType=fixed", body);
        Assert.Contains("SendAt=", body);
        Assert.DoesNotContain($"From={FromNumber}", body); // scheduling goes through the messaging service
    }

    [Fact]
    public async Task Redact_PostsEmptyBodyToTheMessage()
    {
        var handler = Ok("""{ "sid": "SMabc", "status": "delivered" }""");
        var service = BuildService(handler);

        await service.RedactContentAsync("SMabc");

        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.EndsWith("/Messages/SMabc.json", handler.LastRequest.RequestUri!.AbsolutePath);
        var body = Uri.UnescapeDataString(handler.LastBody);
        Assert.Contains("Body=", body);
        Assert.DoesNotContain("Status=canceled", body);
    }

    [Fact]
    public async Task Cancel_PostsCanceledStatusToTheMessage()
    {
        var handler = Ok("""{ "sid": "SMabc", "status": "canceled" }""");
        var service = BuildService(handler);

        await service.CancelScheduledAsync("SMabc");

        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.EndsWith("/Messages/SMabc.json", handler.LastRequest.RequestUri!.AbsolutePath);
        var body = Uri.UnescapeDataString(handler.LastBody);
        Assert.Contains("Status=canceled", body);
    }

    [Fact]
    public async Task Reconciliation_FiltersByConfiguredFromAndDateRange_ServerSide()
    {
        var handler = Ok("""
            { "messages": [ { "sid": "SM1", "from": "+15550000001", "to": "+15551234567",
              "status": "delivered", "date_sent": "Tue, 18 Aug 2026 19:00:00 +0000", "body": "hi" } ],
              "next_page_uri": null }
            """);
        var service = BuildService(handler);

        var from = new DateTimeOffset(2026, 8, 18, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 8, 19, 0, 0, 0, TimeSpan.Zero);
        var messages = await service.ListSentFromConfiguredNumberAsync(from, to);

        Assert.Single(messages);
        Assert.Equal("SM1", messages[0].Sid);
        Assert.NotNull(messages[0].DateSent);

        Assert.Equal(HttpMethod.Get, handler.LastRequest.Method);
        Assert.EndsWith("/Messages.json", handler.LastRequest.RequestUri!.AbsolutePath);
        var query = Uri.UnescapeDataString(handler.LastRequest.RequestUri!.Query);
        Assert.Contains($"From={FromNumber}", query); // server-side From filter, not client-side
        Assert.Contains("DateSent", query);           // server-side date range
    }

    [Fact]
    public async Task Validate_ReturnsProviderCanonicalForm_WhenValid()
    {
        var handler = Ok("""{ "valid": true, "phone_number": "+15551234567", "national_format": "(555) 123-4567" }""");
        var service = BuildService(handler);

        var result = await service.ValidatePhoneNumberAsync("555 123 4567");

        Assert.True(result.IsValid);
        Assert.Equal("+15551234567", result.CanonicalNumber);
        Assert.Contains("/v2/PhoneNumbers/", handler.LastRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Validate_RejectsInvalidNumber()
    {
        var handler = Ok("""{ "valid": false, "phone_number": null, "validation_errors": ["TOO_SHORT"] }""");
        var service = BuildService(handler);

        var result = await service.ValidatePhoneNumberAsync("123");

        Assert.False(result.IsValid);
        Assert.Null(result.CanonicalNumber);
    }

    [Fact]
    public async Task Validate_RejectsWhenProviderReports404()
    {
        var handler = new StubHttpMessageHandler(_ => (HttpStatusCode.NotFound, """{ "code": 20404, "message": "Not Found" }"""));
        var service = BuildService(handler);

        var result = await service.ValidatePhoneNumberAsync("+10000000000");

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task GetDeliveryState_MapsUndeliveredWithErrorCode()
    {
        var handler = Ok("""{ "sid": "SMabc", "status": "undelivered", "error_code": 30034, "error_message": "blocked" }""");
        var service = BuildService(handler);

        var state = await service.GetDeliveryStateAsync("SMabc");

        Assert.Equal("undelivered", state.Status);
        Assert.Equal(30034, state.ErrorCode);
        Assert.Equal("blocked", state.ErrorMessage);
    }

    [Fact]
    public async Task ProviderRejection_SurfacesAsSmsNotificationException_WithStatus()
    {
        var handler = new StubHttpMessageHandler(_ => (HttpStatusCode.BadRequest, """{ "code": 21211, "message": "Invalid 'To'" }"""));
        var service = BuildService(handler);

        var ex = await Assert.ThrowsAsync<SmsNotificationException>(() => service.SendAsync("+15551234567", "hi"));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.DoesNotContain("+15551234567", ex.Message); // destination number never leaks into the error
    }
}
