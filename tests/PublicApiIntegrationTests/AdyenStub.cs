using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests;

/// <summary>
/// Plays Adyen's Checkout API for the test host, so the integration tests never touch the network.
/// A holder name of <see cref="RefusedHolder"/> gets a refusal; everything else is authorised.
/// </summary>
public static class AdyenStub
{
    public const string RefusedHolder = "Refused Shopper";

    /// <summary>Payment requests received, keyed by merchant reference.</summary>
    public static ConcurrentDictionary<string, int> PaymentsByReference { get; } = new();

    /// <summary>Amount (minor units) of each payment request received, keyed by merchant reference.</summary>
    public static ConcurrentDictionary<string, long> AmountByReference { get; } = new();

    public static HttpMessageHandler CreateHandler() => new Handler();

    private sealed class Handler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            var reference = root.GetProperty("reference").GetString()!;
            var amount = root.GetProperty("amount");
            var value = amount.GetProperty("value").GetInt64();
            var currency = amount.GetProperty("currency").GetString();
            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/refunds"))
            {
                var paymentPsp = path.Split('/')[^2];
                return Json(HttpStatusCode.Created, JsonSerializer.Serialize(new
                {
                    merchantAccount = "IntegrationTestMerchant",
                    paymentPspReference = paymentPsp,
                    pspReference = $"REF{reference.GetHashCode():X8}",
                    reference,
                    status = "received",
                    amount = new { currency, value }
                }));
            }

            PaymentsByReference.AddOrUpdate(reference, 1, (_, n) => n + 1);
            AmountByReference[reference] = value;
            var holder = root.GetProperty("paymentMethod").GetProperty("holderName").GetString();
            if (holder == RefusedHolder)
            {
                return Json(HttpStatusCode.OK,
                    """{"pspReference":"REFUSEDPSP0001","resultCode":"Refused","refusalReason":"Refused","refusalReasonCode":"2"}""");
            }

            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                pspReference = $"PSP{reference.GetHashCode():X8}",
                resultCode = "Authorised",
                amount = new { currency, value },
                merchantReference = reference,
                futureField = new { addedByAdyenLater = true }
            }));
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
            new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
