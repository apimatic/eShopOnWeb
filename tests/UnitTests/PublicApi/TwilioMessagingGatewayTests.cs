using System.Net;
using System.Text;
using Microsoft.eShopWeb.PublicApi.Notifications;
using Microsoft.Extensions.Options;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Servers;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi;

public sealed class TwilioMessagingGatewayTests
{
    [Fact]
    public async Task LookupUsesLookupHostAndReturnsProviderCanonicalNumber()
    {
        var handler = new StubHandler(request => request.RequestUri!.Host == "lookups.twilio.com"
            ? """{"phone_number":"+14165550123","valid":true}"""
            : """{"sid":"SM123","status":"queued"}""");
        var gateway = Gateway(handler);

        var canonical = await gateway.ValidateAndCanonicalizeAsync("(416) 555-0123", default);

        Assert.Equal("+14165550123", canonical);
        Assert.Equal("lookups.twilio.com", Assert.Single(handler.Requests).Uri.Host);
    }

    [Fact]
    public async Task MessagingOverrideAndReconciliationFiltersAreOnProviderRequest()
    {
        var handler = new StubHandler(request => request.Method == HttpMethod.Get
            ? """{"messages":[],"next_page_uri":null}"""
            : """{"sid":"SM123","status":"queued"}""");
        var gateway = Gateway(handler);
        var from = DateTimeOffset.Parse("2026-08-20T00:00:00Z");
        var to = DateTimeOffset.Parse("2026-08-21T00:00:00Z");

        await gateway.SendAsync("+14165550123", "test", null, default);
        await gateway.ListAsync(from, to, default);

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, x => Assert.Equal("messaging.test.invalid", x.Uri.Host));
        Assert.Contains("From=%2B15005550006", handler.Requests[0].Body);
        var query = Uri.UnescapeDataString(handler.Requests[1].Uri.Query);
        Assert.Contains("From=+15005550006", query);
        Assert.Contains("DateSent<=2026-08-21", query);
        Assert.Contains("DateSent>=2026-08-20", query);
    }

    private static TwilioMessagingGateway Gateway(StubHandler handler)
    {
        var options = new TwilioSdk.TwilioSdkClientOptions
        {
            Environment = ServerEnvironment.Production,
            AccountSidAuthToken = new BasicAuthCredentials { Username = "ACtest", Password = "secret" }
        };
        options.Server.Default.Production.BaseUrl = "https://messaging.test.invalid";
        var client = new TwilioSdk.TwilioSdkClient(new HttpClient(handler), options);
        var settings = Options.Create(new TwilioSettings
        {
            AccountSid = "ACtest",
            AuthToken = "secret",
            FromNumber = "+15005550006",
            MessagingServiceSid = "MGtest",
            BaseUrl = "https://messaging.test.invalid"
        });
        return new TwilioMessagingGateway(client, settings);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string> _response;
        public List<RecordedRequest> Requests { get; } = [];

        public StubHandler(Func<HttpRequestMessage, string> response) => _response = response;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_response(request), Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string Body);
}
