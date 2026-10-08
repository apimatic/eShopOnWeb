using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PublicApiIntegrationTests.OrderEndpoints;

/// <summary>
/// PublicApi with Adyen replaced at its HttpClient seam, so the whole stack runs without network access.
/// </summary>
public class PaymentApiFactory : WebApplicationFactory<Program>
{
    public FakeAdyen Adyen { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Adyen:ApiKey"] = "offline-test-placeholder",
            ["Adyen:MerchantAccount"] = "OfflineTestMerchant",
            ["Adyen:Environment"] = "test",
            ["Adyen:Currency"] = "USD",
        }));

        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient(AdyenClientFactory.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Adyen);
            services.AddSingleton(new PaymentOptions { ProviderTimeBudget = TimeSpan.FromSeconds(2) });
        });
    }
}

/// <summary>
/// Answers like Adyen's checkout API. The card holder name scripts the outcome: "REFUSE ME" is refused,
/// "HANG" never answers; anything else is authorised for the amount requested.
/// </summary>
public class FakeAdyen : DelegatingHandler
{
    private readonly object _lock = new();
    private readonly List<(string Path, string? IdempotencyKey, JsonDocument Body)> _requests = new();

    public FakeAdyen() : base(new HttpClientHandler())
    {
    }

    public IReadOnlyList<(string Path, string? IdempotencyKey, JsonDocument Body)> Requests
    {
        get { lock (_lock) return _requests.ToList(); }
    }

    public bool Hang { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var bodyText = await request.Content!.ReadAsStringAsync(cancellationToken);
        var body = JsonDocument.Parse(bodyText);
        var path = request.RequestUri!.AbsolutePath;
        var key = request.Headers.TryGetValues("Idempotency-Key", out var values) ? values.Single() : null;
        lock (_lock) _requests.Add((path, key, body));

        var amount = body.RootElement.GetProperty("amount");
        var value = amount.GetProperty("value").GetInt64();
        var currency = amount.GetProperty("currency").GetString();

        if (path.EndsWith("/refunds"))
        {
            var paymentPsp = path.Split('/')[^2];
            return Json(HttpStatusCode.Created, $$"""
                { "merchantAccount": "OfflineTestMerchant", "paymentPspReference": "{{paymentPsp}}", "pspReference": "REFUND-{{key![..8]}}",
                  "status": "received", "amount": { "currency": "{{currency}}", "value": {{value}} } }
                """);
        }

        var holder = body.RootElement.GetProperty("paymentMethod").GetProperty("holderName").GetString();
        if (Hang || holder == "HANG")
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        if (holder == "REFUSE ME")
        {
            return Json(HttpStatusCode.OK, """{ "resultCode": "Refused", "pspReference": "REFUSED-1", "refusalReason": "Not enough balance", "refusalReasonCode": "5" }""");
        }
        return Json(HttpStatusCode.OK, $$"""
            { "resultCode": "Authorised", "pspReference": "AUTH-{{key![..8]}}", "amount": { "currency": "{{currency}}", "value": {{value}} } }
            """);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
