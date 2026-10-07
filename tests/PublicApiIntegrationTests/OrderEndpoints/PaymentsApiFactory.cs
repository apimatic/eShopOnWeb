using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.Payments.Adyen;
using Microsoft.Extensions.DependencyInjection;

namespace PublicApiIntegrationTests.OrderEndpoints;

/// <summary>
/// The real PublicApi host with Adyen replaced at its named HttpClient: every Adyen call is answered from a
/// script, so these tests never touch the network or real credentials.
/// </summary>
public class PaymentsApiFactory : WebApplicationFactory<Program>
{
    public StubAdyen Adyen { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
            services.AddHttpClient(AdyenServiceCollectionExtensions.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => Adyen));
    }
}

public sealed class StubAdyen : HttpMessageHandler
{
    private readonly ConcurrentQueue<(HttpStatusCode Status, string Body)> _responses = new();

    public int Calls;

    public void Enqueue(HttpStatusCode status, string body) => _responses.Enqueue((status, body));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        if (!_responses.TryDequeue(out var next))
            throw new InvalidOperationException($"Unexpected Adyen call {request.Method} {request.RequestUri}");
        return Task.FromResult(new HttpResponseMessage(next.Status)
        {
            Content = new StringContent(next.Body, Encoding.UTF8, "application/json"),
            RequestMessage = request
        });
    }
}
