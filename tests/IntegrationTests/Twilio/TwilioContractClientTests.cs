using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Twilio;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Twilio;

public sealed class TwilioContractClientTests
{
    [Fact]
    public async Task ReconciliationUsesConfiguredBaseFromFilterAndEveryProviderPage()
    {
        var handler = new RecordingHandler(new[]
        {
            Json("""
                 {"messages":[{"sid":"SM00000000000000000000000000000001","from":"+15550000000","to":"+15551111111","body":"one","status":"delivered","error_code":null,"error_message":null,"date_created":"Thu, 28 Aug 2025 10:00:00 +0000","date_sent":"Thu, 28 Aug 2025 10:00:01 +0000"}],"next_page_uri":"https://api.twilio.com/2010-04-01/Accounts/AC00000000000000000000000000000000/Messages.json?PageToken=next"}
                 """),
            Json("""
                 {"messages":[{"sid":"SM00000000000000000000000000000002","from":"+15550000000","to":"+15552222222","body":"two","status":"undelivered","error_code":30003,"error_message":"unreachable","date_created":"Thu, 28 Aug 2025 11:00:00 +0000","date_sent":"Thu, 28 Aug 2025 11:00:01 +0000"}],"next_page_uri":null}
                 """)
        });
        var client = new TwilioMessagingClient(new HttpClient(handler), Options.Create(OptionsForTest()));

        var result = await client.ListAsync(DateTimeOffset.Parse("2025-08-28T09:00:00Z"),
            DateTimeOffset.Parse("2025-08-28T12:00:00Z"));

        Assert.Equal(2, result.Count);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.StartsWith("https://example.test/twilio/", request.Uri));
        Assert.Contains("From=%2B15550000000", handler.Requests[0].Uri, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DateSent>=", handler.Requests[0].Uri, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DateSent<=", handler.Requests[0].Uri, StringComparison.OrdinalIgnoreCase);
        Assert.All(handler.Requests, request => Assert.StartsWith("Basic ", request.Authorization));
    }

    [Fact]
    public async Task MessageUpdateUsesContractFormFieldsForCancellationAndRedaction()
    {
        const string response = """
            {"sid":"SM00000000000000000000000000000001","from":"+15550000000","to":"+15551111111","body":"","status":"canceled","error_code":null,"error_message":null,"date_created":"Thu, 28 Aug 2025 10:00:00 +0000","date_sent":null}
            """;
        var handler = new RecordingHandler(new[] { Json(response), Json(response) });
        var client = new TwilioMessagingClient(new HttpClient(handler), Options.Create(OptionsForTest()));

        await client.CancelAsync("SM00000000000000000000000000000001");
        await client.RedactAsync("SM00000000000000000000000000000001");

        Assert.Equal("Status=canceled", handler.Requests[0].Body);
        Assert.Equal("Body=", handler.Requests[1].Body);
    }

    private static TwilioOptions OptionsForTest() => new()
    {
        AccountSid = "AC00000000000000000000000000000000",
        AuthToken = Guid.Empty.ToString("N"),
        FromNumber = "+15550000000",
        MessagingServiceSid = "MG00000000000000000000000000000000",
        BaseUrl = "https://example.test/twilio"
    };

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;
        public List<RecordedRequest> Requests { get; } = new();

        public RecordingHandler(IEnumerable<HttpResponseMessage> responses) => _responses = new Queue<HttpResponseMessage>(responses);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(request.RequestUri!.ToString(),
                request.Headers.Authorization?.ToString() ?? string.Empty,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return _responses.Dequeue();
        }
    }

    private sealed record RecordedRequest(string Uri, string Authorization, string Body);
}
