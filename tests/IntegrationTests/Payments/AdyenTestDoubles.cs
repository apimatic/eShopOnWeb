using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AdyenApIs;
using Microsoft.eShopWeb.Payments.Adyen;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.IntegrationTests.Payments;

/// <summary>
/// Stands in for Adyen at the HttpClient seam, under the real SDK client. No network is used.
/// </summary>
public sealed class ScriptedAdyenHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _script = new();
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? _fallback;

    public ScriptedAdyenHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? fallback = null)
    {
        _fallback = fallback;
    }

    public List<SentRequest> Sent { get; } = new();

    public ScriptedAdyenHandler Then(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        _script.Enqueue(responder);
        return this;
    }

    public ScriptedAdyenHandler ThenJson(HttpStatusCode status, string json) =>
        Then((_, _) => Task.FromResult(Json(status, json)));

    public ScriptedAdyenHandler ThenConnectionFailure() =>
        Then((_, _) => throw new HttpRequestException("connection reset by peer"));

    /// <summary>Never answers; only the caller's cancellation (or the HttpClient timeout) ends the wait.</summary>
    public ScriptedAdyenHandler ThenHang() =>
        Then(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Read the body now: the SDK disposes request content after each attempt.
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (Sent)
        {
            Sent.Add(new SentRequest(request.Method, request.RequestUri!,
                request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null,
                request.Headers.Contains("X-API-Key"),
                body));
        }

        var responder = _script.TryDequeue(out var next) ? next : _fallback
            ?? throw new InvalidOperationException($"Unexpected call to {request.Method} {request.RequestUri}");
        var response = await responder(request, cancellationToken);
        response.RequestMessage = request;
        return response;
    }
}

public sealed record SentRequest(HttpMethod Method, Uri Uri, string? IdempotencyKey, bool HasApiKey, string? Body);

public static class AdyenTestFactory
{
    public static AdyenSettings Settings() => new()
    {
        ApiKey = "offline-test-key",
        MerchantAccount = "TestMerchantECOM",
        Environment = "test",
        Currency = "USD",
        ReturnUrl = "https://shop.example/"
    };

    /// <summary>The production client options over a scripted handler.</summary>
    public static AdyenPaymentGateway Gateway(HttpMessageHandler handler, TimeSpan? attemptTimeout = null)
    {
        var settings = Settings();
        var httpClient = new HttpClient(handler) { Timeout = attemptTimeout ?? AdyenServiceCollectionExtensions.AttemptTimeout };
        var client = new AdyenApIsClient(httpClient,
            AdyenServiceCollectionExtensions.BuildClientOptions(settings, NullLoggerFactory.Instance, TimeProvider.System));
        return new AdyenPaymentGateway(client, Options.Create(settings), TimeProvider.System, NullLogger<AdyenPaymentGateway>.Instance);
    }

    public const string AuthorisedJson =
        """{"pspReference":"PSP-AUTH-1","resultCode":"Authorised","amount":{"currency":"USD","value":369},"merchantReference":"ref","additionalData":{"cardSummary":"1142"},"someFieldAdyenAddsLater":{"nested":[1,2,3]}}""";

    public const string RefusedJson =
        """{"pspReference":"PSP-REFUSED-1","resultCode":"Refused","refusalReason":"Not enough balance","refusalReasonCode":"51","merchantReference":"ref"}""";

    public static string RefundReceivedJson(string paymentPsp, long value, string refundPsp = "PSP-REFUND-1") =>
        $$"""{"merchantAccount":"TestMerchantECOM","paymentPspReference":"{{paymentPsp}}","pspReference":"{{refundPsp}}","reference":"r","status":"received","amount":{"currency":"USD","value":{{value}}},"anotherNewField":"kept"}""";

    public const string ValidationErrorJson =
        """{"status":422,"errorCode":"101","message":"Invalid card number","errorType":"validation","pspReference":"PSP-ERR-1"}""";
}
