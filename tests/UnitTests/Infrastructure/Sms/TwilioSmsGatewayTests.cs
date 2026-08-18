using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Sms;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using TwilioSdk;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Sms;

public class TwilioSmsGatewayTests
{
    private static TwilioSettings Settings() => new()
    {
        AccountSid = "ACtest",
        AuthToken = "token",
        FromNumber = "+15005550006",
        MessagingServiceSid = "MGtest",
        PerAttemptTimeoutSeconds = 5
    };

    private static TwilioSmsGateway GatewayOver(HttpMessageHandler handler) =>
        new(
            new TwilioSdkClient(new HttpClient(handler), new TwilioSdkClientOptions()),
            Options.Create(Settings()),
            NullLogger<TwilioSmsGateway>.Instance);

    [Fact]
    public async Task ValidateNumber_ValidLookup_ReturnsProviderCanonicalForm()
    {
        var handler = new StubHttpMessageHandler(_ =>
            StubHttpMessageHandler.Json(HttpStatusCode.OK, """{ "valid": true, "phone_number": "+15145551588" }"""));

        var result = await GatewayOver(handler).ValidateNumberAsync("(514) 555 1588");

        Assert.True(result.IsValid);
        Assert.Equal("+15145551588", result.CanonicalE164);
    }

    [Fact]
    public async Task ValidateNumber_InvalidLookup_IsRejected()
    {
        var handler = new StubHttpMessageHandler(_ =>
            StubHttpMessageHandler.Json(HttpStatusCode.OK, """{ "valid": false, "validation_errors": ["TOO_SHORT"] }"""));

        var result = await GatewayOver(handler).ValidateNumberAsync("+1555");

        Assert.False(result.IsValid);
        Assert.Null(result.CanonicalE164);
    }

    [Fact]
    public async Task Send_Returns_ProviderSid_And_Status()
    {
        var handler = new StubHttpMessageHandler(_ =>
            StubHttpMessageHandler.Json(HttpStatusCode.Created, """{ "sid": "SMabc123", "status": "queued" }"""));

        var result = await GatewayOver(handler).SendAsync("+15145551588", "hello");

        Assert.Equal("SMabc123", result.ProviderMessageSid);
        Assert.Equal("queued", result.Status);
    }

    [Fact]
    public async Task Send_ProviderServerError_TranslatesToTransient()
    {
        var handler = new StubHttpMessageHandler(_ =>
            StubHttpMessageHandler.Json(HttpStatusCode.InternalServerError, """{ "code": 20500, "message": "boom" }"""));

        var ex = await Assert.ThrowsAsync<SmsGatewayException>(
            () => GatewayOver(handler).SendAsync("+15145551588", "hello"));

        Assert.Equal(SmsGatewayErrorKind.Transient, ex.Kind);
        Assert.Equal(500, ex.StatusCode);
    }

    [Fact]
    public async Task Send_ProviderClientError_TranslatesToRejected()
    {
        var handler = new StubHttpMessageHandler(_ =>
            StubHttpMessageHandler.Json(HttpStatusCode.BadRequest, """{ "code": 21211, "message": "invalid 'To'" }"""));

        var ex = await Assert.ThrowsAsync<SmsGatewayException>(
            () => GatewayOver(handler).SendAsync("+15145551588", "hello"));

        Assert.Equal(SmsGatewayErrorKind.Rejected, ex.Kind);
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Send_TransportFailure_IsNotDuplicatedByTheRetryLayer()
    {
        // The stub throws a transport error, which the SDK's retry layer re-sends on every verb — including
        // POST. The single-send guard must hold the create-message POST to exactly one attempt.
        var stub = new StubHttpMessageHandler(_ => throw new HttpRequestException("connection reset"));
        var guarded = new SingleSendGuardHandler { InnerHandler = stub };

        var gateway = new TwilioSmsGateway(
            new TwilioSdkClient(new HttpClient(guarded), new TwilioSdkClientOptions()),
            Options.Create(Settings()),
            NullLogger<TwilioSmsGateway>.Instance);

        await Assert.ThrowsAsync<SmsGatewayException>(() => gateway.SendAsync("+15145551588", "hello"));

        Assert.Equal(1, stub.PostCount);
    }
}
