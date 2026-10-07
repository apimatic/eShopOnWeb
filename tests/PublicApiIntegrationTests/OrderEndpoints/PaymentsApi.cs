using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.Infrastructure.Payments.Adyen;
using Microsoft.Extensions.DependencyInjection;

namespace PublicApiIntegrationTests.OrderEndpoints;

/// <summary>
/// The PublicApi host with Adyen replaced at the HttpClient seam by <see cref="FakeAdyen"/>. No network.
/// </summary>
public static class PaymentsApi
{
    public static readonly FakeAdyen Adyen = new();

    private static readonly Lazy<WebApplicationFactory<Program>> Factory = new(() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddHttpClient(AdyenServiceCollectionExtensions.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => new FakeAdyenHandler(Adyen)))));

    public static HttpClient Client(string? token)
    {
        var client = Factory.Value.CreateClient();
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    public static async Task<JsonNode> ReadJson(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

    public static object TestCard(string cardNumber = "test_4111111145551142") => new
    {
        encryptedCardNumber = cardNumber,
        encryptedExpiryMonth = "test_03",
        encryptedExpiryYear = "test_2030",
        encryptedSecurityCode = "test_737",
        holderName = "John Smith",
    };

    public static async Task<int> PlaceOrderAsync(HttpClient client, params (int CatalogItemId, int Quantity)[] items)
    {
        var response = await client.PostAsync("api/orders",
            Json(new { items = items.Select(i => new { catalogItemId = i.CatalogItemId, quantity = i.Quantity }) }));
        Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        return (int)(await ReadJson(response))["orderId"]!;
    }
}

/// <summary>Scripted Adyen: records every call and answers like the Checkout API, plus a field this build does not model.</summary>
public sealed class FakeAdyen
{
    public const string RefusedCardNumber = "test_refused_card";

    public ConcurrentQueue<(string Path, string? IdempotencyKey, JsonNode Body)> Calls { get; } = new();

    public IReadOnlyList<(string Path, string? IdempotencyKey, JsonNode Body)> CallsForOrder(int orderId) =>
        Calls.Where(c => ((string?)c.Body["reference"])?.StartsWith($"eshop-order-{orderId}-") == true).ToList();

    public HttpResponseMessage Answer(HttpRequestMessage request, string body)
    {
        var json = JsonNode.Parse(body)!;
        var key = request.Headers.TryGetValues("Idempotency-Key", out var values) ? values.Single() : null;
        var path = request.RequestUri!.AbsolutePath;
        Calls.Enqueue((path, key, json));

        var psp = "PSP" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        if (path.EndsWith("/payments"))
        {
            var refused = (string?)json["paymentMethod"]?["encryptedCardNumber"] == RefusedCardNumber;
            var response = refused
                ? new JsonObject { ["pspReference"] = psp, ["resultCode"] = "Refused", ["refusalReason"] = "Not enough balance", ["refusalReasonCode"] = "51" }
                : new JsonObject { ["pspReference"] = psp, ["resultCode"] = "Authorised", ["amount"] = json["amount"]!.DeepClone() };
            response["merchantReference"] = json["reference"]!.DeepClone();
            response["fieldAdyenAddsLater"] = new JsonObject { ["nested"] = "kept verbatim" };
            return Respond(HttpStatusCode.OK, response);
        }

        var refundedPsp = path.Split('/')[^2];
        return Respond(HttpStatusCode.Created, new JsonObject
        {
            ["merchantAccount"] = json["merchantAccount"]!.DeepClone(),
            ["paymentPspReference"] = refundedPsp,
            ["pspReference"] = psp,
            ["reference"] = json["reference"]!.DeepClone(),
            ["status"] = "received",
            ["amount"] = json["amount"]!.DeepClone(),
            ["fieldAdyenAddsLater"] = 42,
        });
    }

    private static HttpResponseMessage Respond(HttpStatusCode status, JsonNode body) =>
        new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
}

internal sealed class FakeAdyenHandler : HttpMessageHandler
{
    private readonly FakeAdyen _adyen;

    public FakeAdyenHandler(FakeAdyen adyen) => _adyen = adyen;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        var response = _adyen.Answer(request, body);
        response.RequestMessage = request;
        return response;
    }
}
