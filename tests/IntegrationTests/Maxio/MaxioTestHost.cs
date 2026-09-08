using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.eShopWeb.IntegrationTests.Maxio;

/// <summary>
/// Builds a <see cref="MaxioSubscriptionService"/> over a scripted <see cref="HttpMessageHandler"/> so no
/// real Maxio traffic happens. Also owns an in-memory <see cref="CatalogContext"/> for the enrollment store.
/// </summary>
internal sealed class MaxioTestHost : IDisposable
{
    private readonly CatalogContext _catalogContext;
    private readonly HttpClient _httpClient;

    public MaxioTestHost(ScriptedHandler handler)
    {
        Handler = handler;
        _httpClient = new HttpClient(handler);

        var dbOptions = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(databaseName: "MaxioTest-" + Guid.NewGuid().ToString("N"))
            .Options;
        _catalogContext = new CatalogContext(dbOptions);

        var client = new MaxioAdvancedBillingClient(
            _httpClient,
            new MaxioAdvancedBillingClientOptions
            {
                BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" },
            });

        Settings = new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "cp-exp-5",
            ProductFamilyHandle = "eshop-subscribe",
        };

        Service = new MaxioSubscriptionService(
            client,
            Settings,
            new EfRepository<SubscriptionEnrollment>(_catalogContext),
            NullLogger<MaxioSubscriptionService>.Instance);
    }

    public ScriptedHandler Handler { get; }

    public MaxioSettings Settings { get; }

    public MaxioSubscriptionService Service { get; }

    public void Dispose()
    {
        _httpClient.Dispose();
        _catalogContext.Dispose();
    }
}

/// <summary>HttpMessageHandler that records every request and lets tests script responses per request.</summary>
internal sealed class ScriptedHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _responder;

    public ScriptedHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
    {
        _responder = responder;
    }

    public List<HttpRequestMessage> Requests { get; } = new();

    public IReadOnlyList<HttpRequestMessage> WritesTo(string pathContains) =>
        Requests.Where(r => r.Method != HttpMethod.Get &&
                            (r.RequestUri?.AbsolutePath.Contains(pathContains) ?? false)).ToList();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return await _responder(request);
    }
}

internal static class MaxioTestResponses
{
    public static Task<HttpResponseMessage> Json(HttpStatusCode status, string json) =>
        Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

    public static Task<HttpResponseMessage> Json(string json) => Json(HttpStatusCode.OK, json);

    public static Task<HttpResponseMessage> NotFound() =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent(string.Empty),
        });
}
