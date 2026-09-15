using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Notifications;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;

namespace PublicApiIntegrationTests.Notifications;

[TestClass]
public class TwilioMessagingServiceTests
{
    [TestMethod]
    public async Task LookupKeepsItsProviderHostWhileMessagesUseOverride()
    {
        var handler = new StubHandler(request =>
        {
            var json = request.RequestUri!.Host == "lookups.twilio.com"
                ? "{\"valid\":true,\"phone_number\":\"+15005550006\"}"
                : "{\"sid\":\"SM_TEST\",\"status\":\"queued\",\"from\":\"+15005550006\",\"body\":\"Order 1 placed\"}";
            return Json(HttpStatusCode.OK, json);
        });
        var service = CreateService(handler);

        var canonical = await service.ValidateAndCanonicalizeAsync("not-canonical", CancellationToken.None);
        var sent = await service.SendAsync(canonical!, "Order 1 placed", CancellationToken.None);

        Assert.AreEqual("+15005550006", canonical);
        Assert.AreEqual("SM_TEST", sent.Sid);
        Assert.AreEqual("lookups.twilio.com", handler.Requests[0].Uri.Host);
        Assert.AreEqual("messaging.test", handler.Requests[1].Uri.Host);
        var form = handler.Requests[1].Body;
        StringAssert.Contains(form, "From=%2B15005550006");
        StringAssert.Contains(form, "To=%2B15005550006");
        StringAssert.Contains(form, "Body=Order+1+placed");
    }

    [TestMethod]
    public async Task ReconciliationSendsFromFilterToProvider()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "{\"messages\":[],\"next_page_uri\":null}"));
        var service = CreateService(handler);

        await service.ListAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, null, CancellationToken.None);

        Assert.AreEqual(HttpMethod.Get, handler.Requests[0].Method);
        StringAssert.Contains(handler.Requests[0].Uri.Query, "From=%2B15005550006");
    }

    [TestMethod]
    public async Task TransportFailureCannotCauseASecondPaidWrite()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection reset"));
        var service = CreateService(handler);

        var exception = await Assert.ThrowsExceptionAsync<TwilioProviderException>(() =>
            service.SendAsync("+15005550006", "Order 1 placed", CancellationToken.None));

        Assert.IsTrue(exception.IsAmbiguous);
        Assert.AreEqual(1, handler.Requests.Count);
    }

    private static TwilioMessagingService CreateService(StubHandler terminalHandler)
    {
        var context = new TwilioRequestContext();
        var guard = new TwilioWriteGuardHandler(context) { InnerHandler = terminalHandler };
        var httpClient = new HttpClient(guard);
        var clientOptions = new TwilioSdk.TwilioSdkClientOptions
        {
            Environment = ServerEnvironment.Production,
            AccountSidAuthToken = new BasicAuthCredentials { Username = "test-account", Password = "test-token" },
            Retry = RetryOptions.Default() with { MaxRetries = 1, Delay = TimeSpan.Zero, MaxJitter = TimeSpan.Zero }
        };
        clientOptions.Server.Default.Production.BaseUrl = "https://messaging.test";
        var client = new TwilioSdk.TwilioSdkClient(httpClient, clientOptions);
        var settings = Options.Create(new TwilioSettings
        {
            AccountSid = "test-account",
            AuthToken = "test-token",
            FromNumber = "+15005550006",
            MessagingServiceSid = "test-messaging-service",
            BaseUrl = "https://messaging.test"
        });
        return new TwilioMessagingService(client, settings, context);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, body));
            return responder(request);
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri Uri, string Body);
}
