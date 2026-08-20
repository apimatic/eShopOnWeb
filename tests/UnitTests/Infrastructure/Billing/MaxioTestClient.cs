using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string> Bodies { get; } = new();

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
        return _responder(request);
    }
}

internal static class MaxioTestClient
{
    public static (MaxioSubscriptionBillingService Service, StubHandler Handler) Create(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        string familyHandle = "eshop-subscribe")
    {
        var handler = new StubHandler(responder);
        var http = new HttpClient(handler);
        var options = new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = familyHandle
        };
        var client = MaxioServiceCollectionExtensions.CreateClient(http, options);
        var service = new MaxioSubscriptionBillingService(
            client,
            Options.Create(options),
            NullLogger<MaxioSubscriptionBillingService>.Instance);
        return (service, handler);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
}
