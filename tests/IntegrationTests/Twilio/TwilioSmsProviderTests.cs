using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.Infrastructure.Twilio;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Servers;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Twilio;

public class TwilioSmsProviderTests
{
    [Fact]
    public async Task UsesIsolatedHostsAndExactMessagingContracts()
    {
        var listPage = 0;
        var handler = new RecordingHandler(request =>
        {
            if (request.Uri.Host == "lookups.twilio.com")
            {
                return Json("""{"phone_number":"+10000000000","valid":true}""");
            }

            if (request.Method == HttpMethod.Get)
            {
                listPage++;
                return listPage == 1
                    ? Json("""{"messages":[{"sid":"SM-LIST-1","status":"delivered","date_sent":"2026-08-27T12:00:00Z"}],"next_page_uri":"/2010-04-01/Accounts/test/Messages.json?PageToken=next-token"}""")
                    : Json("""{"messages":[{"sid":"SM-LIST-2","status":"undelivered","date_sent":"2026-08-27T13:00:00Z"}],"next_page_uri":null}""");
            }

            if (request.Body.Contains("Status=canceled", StringComparison.Ordinal))
            {
                return Json("""{"sid":"SM-SCHEDULED","status":"canceled","body":"follow up"}""");
            }

            if (request.Body.StartsWith("Body=", StringComparison.Ordinal) && !request.Body.Contains('&'))
            {
                return Json("""{"sid":"SM-SENT","status":"delivered","body":""}""");
            }

            if (request.Body.Contains("ScheduleType=fixed", StringComparison.Ordinal))
            {
                return Json("""{"sid":"SM-SCHEDULED","status":"scheduled","body":"follow up"}""");
            }

            return Json("""{"sid":"SM-SENT","status":"queued","body":"placed"}""");
        });
        var options = new TwilioSdk.TwilioSdkClientOptions
        {
            Environment = ServerEnvironment.Production,
            AccountSidAuthToken = new BasicAuthCredentials
            {
                Username = "test-account",
                Password = "test-token"
            }
        };
        options.Server.Default.Production.BaseUrl = "https://messaging.example";
        var client = new TwilioSdk.TwilioSdkClient(new HttpClient(handler), options);
        var provider = new TwilioSmsProvider(client, Options.Create(new TwilioSettings
        {
            AccountSid = "test-account",
            AuthToken = "test-token",
            FromNumber = "+10000000001",
            MessagingServiceSid = "MG-TEST",
            BaseUrl = "https://messaging.example"
        }));

        var validation = await provider.ValidatePhoneNumberAsync("+10000000000", CancellationToken.None);
        Assert.True(validation.IsValid);
        Assert.Equal("+10000000000", validation.CanonicalNumber);

        await provider.SendAsync("+10000000000", "placed", null, CancellationToken.None);
        var sendAt = DateTimeOffset.Parse("2026-08-30T12:00:00Z");
        await provider.SendAsync("+10000000000", "follow up", sendAt, CancellationToken.None);
        await provider.CancelAsync("SM-SCHEDULED", CancellationToken.None);
        await provider.DisposeContentAsync("SM-SENT", CancellationToken.None);
        var listed = await provider.ListAsync(
            DateTimeOffset.Parse("2026-08-27T00:00:00Z"),
            DateTimeOffset.Parse("2026-08-28T00:00:00Z"),
            CancellationToken.None);

        Assert.Equal(2, listed.Count);
        Assert.Equal("lookups.twilio.com", handler.Requests[0].Uri.Host);
        Assert.All(handler.Requests.Skip(1), request => Assert.Equal("messaging.example", request.Uri.Host));

        var immediate = handler.Requests.Single(x => x.Method == HttpMethod.Post && x.Body.Contains("Body=placed"));
        Assert.Contains("From=%2B10000000001", immediate.Body);
        Assert.Contains("To=%2B10000000000", immediate.Body);
        Assert.DoesNotContain("ScheduleType", immediate.Body);

        var scheduled = handler.Requests.Single(x => x.Body.Contains("ScheduleType=fixed"));
        Assert.Contains("MessagingServiceSid=MG-TEST", scheduled.Body);
        Assert.Contains("SendAt=", scheduled.Body);
        Assert.Contains("From=%2B10000000001", scheduled.Body);

        var cancellation = handler.Requests.Single(x => x.Body.Contains("Status=canceled"));
        Assert.Contains("/Messages/SM-SCHEDULED", cancellation.Uri.AbsolutePath);
        Assert.Contains(handler.Requests, x => x.Body == "Body=");

        var listRequests = handler.Requests.Where(x => x.Method == HttpMethod.Get && x.Uri.Host == "messaging.example").ToArray();
        Assert.Equal(2, listRequests.Length);
        Assert.All(listRequests, request =>
        {
            Assert.Contains("From=%2B10000000001", request.Uri.Query);
            Assert.Contains("DateSent%3C=", request.Uri.Query);
            Assert.Contains("DateSent%3E=", request.Uri.Query);
        });
        Assert.DoesNotContain("PageToken", listRequests[0].Uri.Query);
        Assert.Contains("PageToken=next-token", listRequests[1].Uri.Query);
    }

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(Func<RecordedRequest, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var recorded = new RecordedRequest(request.Method, request.RequestUri!, body);
            Requests.Add(recorded);
            return responder(recorded);
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string Body);
}
