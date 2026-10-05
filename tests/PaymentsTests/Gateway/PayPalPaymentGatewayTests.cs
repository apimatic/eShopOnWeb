using System.Net;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.eShopWeb.PaymentsTests.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PaymentsTests.Gateway;

/// <summary>The PayPal adapter against the real SDK client, with the HttpClient pointed at a fake.</summary>
public class PayPalPaymentGatewayTests
{
    private static (ServiceProvider Provider, IPaymentGateway Gateway) Build(HttpMessageHandler handler, Dictionary<string, string?>? settings = null, PayPalResilienceSettings? resilience = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings ?? new Dictionary<string, string?>
            {
                ["PayPal:ClientId"] = "id",
                ["PayPal:ClientSecret"] = "secret",
                ["PayPal:Environment"] = "sandbox",
                ["PayPal:Currency"] = "USD",
                ["PayPal:BaseUrl"] = "https://paypal.test.invalid",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        if (resilience is not null) services.AddSingleton(resilience);
        services.AddPayPalPayments(configuration);
        services.AddHttpClient(PayPalServiceCollectionExtensions.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        return (provider, scope.ServiceProvider.GetRequiredService<IPaymentGateway>());
    }

    private static AuthorizePaymentCommand Command(decimal amount = 47.5m) => new(
        7, "eshop-7-auth-test", amount, "eshop-7-inv", "eshop-order-7", "eShopOnWeb order 7",
        new CardDetails("4111111111111111", "2030-12", "123", "Test Shopper", new CardBillingAddress("1 Main St", null, "San Jose", "CA", "95131", "US")),
        null);

    [Fact]
    public async Task Authorize_sends_an_AUTHORIZE_order_for_the_exact_amount_with_a_request_id()
    {
        var paypal = new FakePayPal();
        var (provider, gateway) = Build(paypal);
        using var _ = provider;

        var result = await gateway.AuthorizeAsync(Command());

        var create = Assert.Single(paypal.Calls("POST", "/v2/checkout/orders"));
        Assert.Equal("AUTHORIZE", create.Json!["intent"]!.GetValue<string>());
        Assert.Equal("47.50", create.Json["purchase_units"]![0]!["amount"]!["value"]!.GetValue<string>());
        Assert.Equal("eshop-7-inv", create.Json["purchase_units"]![0]!["invoice_id"]!.GetValue<string>());
        Assert.Equal("2030-12", create.Json["payment_source"]!["card"]!["expiry"]!.GetValue<string>());
        Assert.Equal("US", create.Json["payment_source"]!["card"]!["billing_address"]!["country_code"]!.GetValue<string>());
        Assert.Equal("eshop-7-auth-test", create.Header("PayPal-Request-Id"));
        Assert.Equal(AuthorizationOutcome.Approved, result.Outcome);
        Assert.Equal(47.50m, result.Amount);
        Assert.Equal("1111", result.CardLastDigits);
        Assert.NotNull(result.ExpiresAt);
    }

    [Fact]
    public async Task Every_call_including_the_token_request_uses_the_configured_base_url()
    {
        var paypal = new FakePayPal();
        var (provider, gateway) = Build(paypal);
        using var _ = provider;

        await gateway.AuthorizeAsync(Command());

        var calls = paypal.Requests.ToList();
        Assert.Contains(calls, c => c.Path == "/v1/oauth2/token");
        Assert.All(paypal.Requests, r => Assert.True(r.Path.StartsWith("/v")));
        Assert.Equal(2, calls.Count);
    }

    [Fact]
    public async Task Base_url_override_is_used_verbatim_and_sandbox_is_the_default()
    {
        var hosts = new List<string>();
        var handler = new RecordingHandler(r => hosts.Add(r.RequestUri!.Host), new FakePayPal());
        var (provider, gateway) = Build(handler, new Dictionary<string, string?>
        {
            ["PayPal:ClientId"] = "id",
            ["PayPal:ClientSecret"] = "secret",
            ["PayPal:Environment"] = "live",
            ["PayPal:Currency"] = "USD",
            ["PayPal:BaseUrl"] = "https://payments-proxy.example.internal",
        });
        using var _ = provider;

        await gateway.GetAuthorizationAsync("AUTH-X").ContinueWith(_ => { });

        Assert.Equal(new[] { "payments-proxy.example.internal", "payments-proxy.example.internal" }, hosts);

        var sandboxHosts = new List<string>();
        var (provider2, gateway2) = Build(new RecordingHandler(r => sandboxHosts.Add(r.RequestUri!.Host), new FakePayPal()), new Dictionary<string, string?>
        {
            ["PayPal:ClientId"] = "id",
            ["PayPal:ClientSecret"] = "secret",
            ["PayPal:Environment"] = "sandbox",
            ["PayPal:Currency"] = "USD",
        });
        using var __ = provider2;
        await gateway2.GetAuthorizationAsync("AUTH-X").ContinueWith(_ => { });
        Assert.All(sandboxHosts, h => Assert.Equal("api-m.sandbox.paypal.com", h));
    }

    [Fact]
    public async Task Capture_asks_for_a_final_capture_and_reports_fee_and_net()
    {
        var paypal = new FakePayPal();
        var (provider, gateway) = Build(paypal);
        using var _ = provider;
        var authorization = await gateway.AuthorizeAsync(Command(100m));

        var capture = await gateway.CaptureAsync(authorization.AuthorizationId, 100m, "eshop-7-capture-x");

        var call = Assert.Single(paypal.Calls("POST", "/v2/payments/authorizations/.*/capture"));
        Assert.True(call.Json!["final_capture"]!.GetValue<bool>());
        Assert.Equal("100.00", call.Json["amount"]!["value"]!.GetValue<string>());
        Assert.Equal("eshop-7-capture-x", call.Header("PayPal-Request-Id"));
        Assert.Equal(CaptureOutcome.Completed, capture.Outcome);
        Assert.Equal(100m, capture.Amount);
        Assert.Equal(3.98m, capture.PayPalFee);
        Assert.Equal(96.02m, capture.NetAmount);
    }

    [Fact]
    public async Task A_provider_refusal_carries_PayPals_issue_and_debug_id()
    {
        var paypal = new FakePayPal();
        var (provider, gateway) = Build(paypal);
        using var _ = provider;
        var command = Command() with { Card = new CardDetails(FakePayPal.DeclinedCard, "2030-12", "123", null, null) };

        var ex = await Assert.ThrowsAsync<PaymentProviderException>(() => gateway.AuthorizeAsync(command));

        Assert.Equal(PaymentProviderErrorKind.Rejected, ex.Kind);
        Assert.Equal(422, ex.ProviderStatusCode);
        Assert.Equal("fake-debug-instrument_declined", ex.DebugId);
        Assert.Contains("INSTRUMENT_DECLINED", ex.Issues);
    }

    [Fact]
    public async Task A_dropped_connection_on_a_write_is_an_unknown_outcome_and_is_never_resent_by_the_SDK()
    {
        var paypal = new FakePayPal();
        var (provider, gateway) = Build(paypal);
        using var _ = provider;
        var authorization = await gateway.AuthorizeAsync(Command());
        paypal.InjectFault("POST", "/v2/payments/authorizations/.*/capture", Fault.DropBeforeProcessing, times: 5);

        var ex = await Assert.ThrowsAsync<PaymentProviderException>(() => gateway.CaptureAsync(authorization.AuthorizationId, 47.5m, "req"));

        Assert.Equal(PaymentProviderErrorKind.OutcomeUnknown, ex.Kind);
        Assert.Single(paypal.Calls("POST", "/v2/payments/authorizations/.*/capture"));
    }

    [Fact]
    public async Task A_lost_capture_is_found_again_on_the_PayPal_order()
    {
        var paypal = new FakePayPal();
        var (provider, gateway) = Build(paypal);
        using var _ = provider;
        var authorization = await gateway.AuthorizeAsync(Command());
        paypal.InjectFault("POST", "/v2/payments/authorizations/.*/capture", Fault.DropAfterProcessing);

        await Assert.ThrowsAsync<PaymentProviderException>(() => gateway.CaptureAsync(authorization.AuthorizationId, 47.5m, "req"));
        var found = await gateway.FindCaptureAsync(authorization.ProviderOrderId);

        Assert.NotNull(found);
        Assert.Equal(47.5m, found!.Amount);
    }

    [Fact]
    public async Task Rejected_credentials_surface_as_unavailable_not_as_the_callers_fault()
    {
        var handler = new RecordingHandler(_ => { }, new StatusHandler(HttpStatusCode.Unauthorized, """{"error":"invalid_client","error_description":"Client Authentication failed"}"""));
        var (provider, gateway) = Build(handler);
        using var _ = provider;

        var ex = await Assert.ThrowsAsync<PaymentProviderException>(() => gateway.AuthorizeAsync(Command()));

        Assert.Equal(PaymentProviderErrorKind.Unavailable, ex.Kind);
    }

    [Fact]
    public async Task A_hung_read_is_cut_off_by_the_request_budget()
    {
        var paypal = new FakePayPal();
        paypal.InjectFault("GET", "/v2/payments/authorizations/.*", Fault.Hang, times: 10);
        var (provider, gateway) = Build(paypal, resilience: new PayPalResilienceSettings
        {
            RequestBudget = TimeSpan.FromSeconds(1),
            AttemptTimeout = TimeSpan.FromSeconds(30),
            HttpClientTimeout = TimeSpan.FromSeconds(30),
        });
        using var _ = provider;

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<PaymentProviderException>(() => gateway.GetAuthorizationAsync("AUTH-1"));

        Assert.Equal(PaymentProviderErrorKind.Timeout, ex.Kind);
        Assert.StartsWith("PayPal did not respond", ex.Message);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Saving_a_card_vaults_it_via_a_setup_token_and_returns_only_display_details()
    {
        var paypal = new FakePayPal();
        var (provider, gateway) = Build(paypal);
        using var _ = provider;

        var saved = await gateway.SaveCardAsync(new CardDetails("4111111111111111", "2031-01", "123", "T S", null), null, "eshop-card-1");

        var setup = Assert.Single(paypal.Calls("POST", "/v3/vault/setup-tokens"));
        Assert.Equal("4111111111111111", setup.Json!["payment_source"]!["card"]!["number"]!.GetValue<string>());
        var token = Assert.Single(paypal.Calls("POST", "/v3/vault/payment-tokens"));
        Assert.Equal("SETUP_TOKEN", token.Json!["payment_source"]!["token"]!["type"]!.GetValue<string>());
        Assert.StartsWith("pt", saved.PaymentTokenId);
        Assert.Equal("1111", saved.LastDigits);
        Assert.Equal("VISA", saved.Brand);
        Assert.Equal("2031-01", saved.Expiry);
        Assert.NotNull(saved.ProviderCustomerId);
    }

    [Fact]
    public async Task Search_maps_PayPals_transaction_record()
    {
        var paypal = new FakePayPal();
        var at = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        paypal.ReportingTransactions.Add(FakePayPal.ReportingTransaction("TX1", at, 12.34m, "inv-1"));
        var (provider, gateway) = Build(paypal);
        using var _ = provider;

        var page = await gateway.SearchTransactionsAsync(at.AddDays(-1), at.AddDays(1), 1);

        var tx = Assert.Single(page.Transactions);
        Assert.Equal("TX1", tx.TransactionId);
        Assert.Equal(12.34m, tx.Amount);
        Assert.Equal("inv-1", tx.InvoiceId);
        Assert.Equal(1, page.TotalPages);
        var call = Assert.Single(paypal.Calls("GET", "/v1/reporting/transactions"));
        Assert.Contains("start_date=2026-09-09T12%3A00%3A00Z", call.Query);
    }

    private sealed class RecordingHandler(Action<HttpRequestMessage> record, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            record(request);
            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class StatusHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json"), RequestMessage = request });
    }
}
