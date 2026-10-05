using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Payments.PayPal;
using Microsoft.eShopWeb.PaymentTests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Payments;

/// <summary>The SDK boundary: what goes on the wire, and how every failure is translated.</summary>
public class PayPalGatewayTests : IDisposable
{
    private readonly PaymentTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private Task<T> Gateway<T>(Func<IPaymentGateway, Task<T>> call) =>
        _host.InScopeAsync(sp => call(sp.GetRequiredService<IPaymentGateway>()));

    private static CreateGatewayOrder Order(string requestId = "req-1") =>
        new(51.00m, "USD", "eshop-order-1", "eshop-1-abc", "eShop order 1", requestId);

    private void Respond(HttpStatusCode status, string json) =>
        _host.PayPal.Intercept = (req, _, _) => Task.FromResult<HttpResponseMessage?>(
            req.RequestUri!.AbsolutePath == "/v1/oauth2/token" ? null : FakePayPal.JsonResponse(status, json));

    [Fact]
    public async Task BaseUrlOverride_IsUsedForTheTokenAndEveryCall()
    {
        await Gateway(g => g.CreateOrderAsync(Order(), CancellationToken.None));

        Assert.Contains(_host.PayPal.Requests, r => r.Path == "/v1/oauth2/token");
        Assert.All(_host.PayPal.Requests, r => Assert.Equal(PaymentTestHost.BaseUrl, r.Authority));
    }

    [Fact]
    public async Task Writes_CarryTheCallersPayPalRequestId()
    {
        await Gateway(g => g.CreateOrderAsync(Order("stable-key"), CancellationToken.None));
        await Gateway(g => g.CreateOrderAsync(Order("stable-key"), CancellationToken.None));

        var creates = _host.PayPal.Requests.Where(r => r.Path == "/v2/checkout/orders").ToList();
        Assert.All(creates, c => Assert.Equal("stable-key", c.PayPalRequestId));
    }

    [Fact]
    public async Task Rejection_IsTranslatedWithPayPalsIssueAndDebugId()
    {
        Respond(HttpStatusCode.UnprocessableEntity,
            """{"name":"UNPROCESSABLE_ENTITY","message":"Business validation failed.","debug_id":"abc123","details":[{"issue":"DUPLICATE_INVOICE_ID","description":"Duplicate Invoice ID detected."}]}""");

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(() => Gateway(g => g.CreateOrderAsync(Order(), CancellationToken.None)));

        Assert.Equal(PaymentGatewayFailure.Rejected, ex.Failure);
        Assert.Equal(422, ex.ProviderStatus);
        Assert.Equal("DUPLICATE_INVOICE_ID", ex.ProviderIssue);
        Assert.Equal("abc123", ex.DebugId);
        Assert.Equal("Duplicate Invoice ID detected.", ex.Message);
        Assert.False(ex.OutcomeUnknown);
    }

    [Fact]
    public async Task Unauthorized_IsAMerchantConfigurationProblem()
    {
        Respond(HttpStatusCode.Unauthorized, """{"name":"AUTHENTICATION_FAILURE","message":"Authentication failed.","debug_id":"d1"}""");

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(() => Gateway(g => g.GetOrderAsync("ORDER1", CancellationToken.None)));

        Assert.Equal(PaymentGatewayFailure.MerchantConfiguration, ex.Failure);
    }

    [Fact]
    public async Task NotFound_IsTranslated()
    {
        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(() => Gateway(g => g.GetAuthorizationAsync("NOPE", CancellationToken.None)));

        Assert.Equal(PaymentGatewayFailure.NotFound, ex.Failure);
        Assert.Equal("fake-debug-404", ex.DebugId);
    }

    [Fact]
    public async Task ServerErrorOnAWrite_IsAnUnknownOutcome_AndIsNotResentByTheSdk()
    {
        Respond(HttpStatusCode.ServiceUnavailable, """{"name":"INTERNAL_SERVICE_ERROR","message":"Try later.","debug_id":"d5"}""");

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(() =>
            Gateway(g => g.CaptureAsync("AUTH1", 10m, "USD", null, "cap-key", CancellationToken.None)));

        Assert.Equal(PaymentGatewayFailure.ProviderError, ex.Failure);
        Assert.True(ex.OutcomeUnknown);
        Assert.Equal(1, _host.PayPal.Count("POST", "/capture$"));
    }

    [Fact]
    public async Task ConnectionFailure_IsUnreachable_AndTheWriteIsNotResent()
    {
        _host.PayPal.Intercept = (req, _, _) => req.RequestUri!.AbsolutePath.EndsWith("/refund")
            ? throw new HttpRequestException("connection reset")
            : Task.FromResult<HttpResponseMessage?>(null);

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(() =>
            Gateway(g => g.RefundAsync("CAP1", 5m, "USD", "c", "ref-key", CancellationToken.None)));

        Assert.Equal(PaymentGatewayFailure.Unreachable, ex.Failure);
        Assert.True(ex.OutcomeUnknown);
        Assert.Equal(1, _host.PayPal.Count("POST", "/refund$"));
    }

    [Fact]
    public async Task UnreadableSuccessBody_IsAnUnknownOutcome()
    {
        Respond(HttpStatusCode.Created, """{"id": 42, "status": ["not", "a", "string"]}""");

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(() =>
            Gateway(g => g.RefundAsync("CAP1", 5m, "USD", "c", "ref-key", CancellationToken.None)));

        Assert.Equal(PaymentGatewayFailure.ProviderError, ex.Failure);
        Assert.True(ex.OutcomeUnknown);
    }

    [Fact]
    public async Task HangingPayPal_IsCutOffByTheRequestBudget()
    {
        _host.PayPal.Intercept = async (req, _, ct) =>
        {
            if (req.RequestUri!.AbsolutePath != "/v1/oauth2/token") await Task.Delay(Timeout.Infinite, ct);
            return null;
        };

        var watch = Stopwatch.StartNew();
        var ex = await _host.InScopeAsync(async sp =>
        {
            var gateway = sp.GetRequiredService<IPaymentGateway>();
            // Several calls in one request share one budget (5 s in the test host): they cannot add up past it.
            PaymentGatewayException? last = null;
            for (var i = 0; i < 5; i++)
            {
                try { await gateway.GetAuthorizationAsync("AUTH1", CancellationToken.None); }
                catch (PaymentGatewayException e) { last = e; }
            }
            return last!;
        });
        watch.Stop();

        Assert.Equal(PaymentGatewayFailure.Timeout, ex.Failure);
        Assert.True(ex.BudgetExhausted);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(8), $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task SearchTransactions_SendsAnRfc3339RangeAndReadsPaging()
    {
        _host.PayPal.ReportTransactions.Add(FakePayPal.Transaction("T1", new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero), 10m));

        var page = await Gateway(g => g.SearchTransactionsAsync(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero), 1, 500, CancellationToken.None));

        Assert.Single(page.Transactions);
        Assert.Equal(1, page.TotalPages);
        var call = _host.PayPal.Requests.Single(r => r.Path.StartsWith("/v1/reporting/transactions"));
        Assert.Contains("start_date=2026-09-01T00%3A00%3A00Z", call.Path);
        Assert.Contains("page_size=500", call.Path);
    }

    [Theory]
    [InlineData("PayPal:ClientId", "PayPal:ClientId is not configured")]
    [InlineData("PayPal:ClientSecret", "PayPal:ClientSecret is not configured")]
    [InlineData("PayPal:Currency", "PayPal:Currency must be")]
    public void MissingSetting_FailsValidation_NamingTheKeyNotTheValue(string key, string expected)
    {
        using var host = new PaymentTestHost(s => s[key] = "  ");

        var ex = Assert.Throws<OptionsValidationException>(() => host.InScopeAsync(sp =>
            Task.FromResult(sp.GetRequiredService<IOptions<PayPalOptions>>().Value)).GetAwaiter().GetResult());

        Assert.Contains(expected, ex.Message);
        Assert.DoesNotContain("test-client-secret", ex.Message);
    }

    [Fact]
    public void NonSandboxEnvironment_RequiresAnExplicitBaseUrl()
    {
        var validator = new PayPalOptionsValidator();

        var withoutBase = validator.Validate(null, new PayPalOptions { ClientId = "a", ClientSecret = "b", Currency = "USD", Environment = "live" });
        var withBase = validator.Validate(null, new PayPalOptions { ClientId = "a", ClientSecret = "b", Currency = "USD", Environment = "live", BaseUrl = "https://api.example" });

        Assert.True(withoutBase.Failed);
        Assert.Contains("PayPal:BaseUrl must be set", withoutBase.FailureMessage);
        Assert.True(withBase.Succeeded);
    }
}
