#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

/// <summary>Stands in for Maxio on the HttpClient seam; records every request and its body.</summary>
public sealed class MaxioStubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public MaxioStubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string?> Bodies { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        // Read the body now: the SDK disposes request content after each attempt.
        Bodies.Add(request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult());
        var response = _responder(request);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static string Subscription(int id, string reference, int customerId, string planHandle, string familyHandle, string state = "active") =>
        JsonSerializer.Serialize(new
        {
            subscription = new
            {
                id,
                state,
                reference,
                customer = new { id = customerId },
                product = new { handle = planHandle, name = "Plan " + planHandle, product_family = new { handle = familyHandle } },
                product_price_in_cents = 29900,
                currency = "USD",
                next_assessment_at = "2026-11-15T00:00:00Z",
                created_at = "2026-10-15T00:00:00Z",
            }
        });

    public static string Customer(int id, string reference) =>
        JsonSerializer.Serialize(new { customer = new { id, reference } });

    public static string Product(int id, string handle, long priceInCents, string? archivedAt = null) =>
        JsonSerializer.Serialize(new
        {
            product = new
            {
                id,
                handle,
                name = "Plan " + handle,
                price_in_cents = priceInCents,
                interval = 1,
                interval_unit = "month",
                archived_at = archivedAt,
            }
        });
}
