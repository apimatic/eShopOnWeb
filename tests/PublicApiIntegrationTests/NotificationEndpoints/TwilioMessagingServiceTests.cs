using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TwilioSdk;
using TwilioSdk.Servers;

namespace PublicApiIntegrationTests.NotificationEndpoints;

/// <summary>
/// Offline tests for the Twilio integration boundary, faked at the HttpClient seam —
/// no real network calls happen.
/// </summary>
[TestClass]
public class TwilioMessagingServiceTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }

    private static TwilioMessagingService ServiceReturning(HttpStatusCode status, string json)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });
        var client = new TwilioSdkClient(new HttpClient(handler), new TwilioSdkClientOptions
        {
            Environment = ServerEnvironment.Production
        });
        var options = Microsoft.Extensions.Options.Options.Create(new TwilioOptions
        {
            AccountSid = "ACtest",
            AuthToken = "test",
            FromNumber = "+10000000000",
            MessagingServiceSid = "MGtest"
        });
        return new TwilioMessagingService(client, options, NullLogger<TwilioMessagingService>.Instance);
    }

    [TestMethod]
    public async Task SendMessageReturnsProviderStateOnSuccess()
    {
        var service = ServiceReturning(HttpStatusCode.Created,
            """{ "sid": "SM123", "status": "queued", "to": "+10000000001" }""");

        var message = await service.SendMessageAsync("+10000000001", "hello", CancellationToken.None);

        Assert.AreEqual("SM123", message.Sid);
        Assert.AreEqual("queued", message.Status);
    }

    [TestMethod]
    public async Task SendMessageThrowsMessagingExceptionCarryingProviderStatusOnRejection()
    {
        var service = ServiceReturning(HttpStatusCode.BadRequest,
            """{ "code": 21211, "message": "invalid number" }""");

        var ex = await Assert.ThrowsExceptionAsync<MessagingException>(
            () => service.SendMessageAsync("+10000000001", "hello", CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.BadRequest, ex.ProviderStatusCode);
    }

    [TestMethod]
    public async Task ValidateNumberReturnsCanonicalFormWhenValid()
    {
        var service = ServiceReturning(HttpStatusCode.OK,
            """{ "valid": true, "phone_number": "+18254751588", "national_format": "(825) 475-1588" }""");

        var result = await service.ValidateNumberAsync("825-475-1588", CancellationToken.None);

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual("+18254751588", result.CanonicalNumber);
    }

    [TestMethod]
    public async Task ValidateNumberReportsUnusableWhenProviderSaysInvalid()
    {
        var service = ServiceReturning(HttpStatusCode.OK,
            """{ "valid": false, "validation_errors": ["TOO_SHORT"] }""");

        var result = await service.ValidateNumberAsync("123", CancellationToken.None);

        Assert.IsFalse(result.IsValid);
        Assert.IsNull(result.CanonicalNumber);
        Assert.AreEqual(1, result.ValidationErrors.Count);
    }
}
