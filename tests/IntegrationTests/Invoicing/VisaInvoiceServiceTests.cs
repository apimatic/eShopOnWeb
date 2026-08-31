using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CyberSourceMergedSpec;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Invoicing;
using Microsoft.eShopWeb.UnitTests.Builders;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Invoicing;

/// <summary>
/// Exercises the real <see cref="VisaInvoiceService"/> against a stubbed HTTP seam — no network — so
/// the provider mapping, local persistence, ownership/state rules and error translation are tested
/// as real behaviour rather than execution.
/// </summary>
public class VisaInvoiceServiceTests
{
    private static readonly DateTimeOffset DueDate = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        VisaInvoiceService Service,
        CatalogContext Context,
        EfRepository<Invoice> InvoiceRepository,
        EfRepository<Order> OrderRepository,
        StubHttpMessageHandler Handler);

    private static Harness Build(Func<HttpRequestMessage, (HttpStatusCode, string)> responder)
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var context = new CatalogContext(options);
        var invoiceRepository = new EfRepository<Invoice>(context);
        var orderRepository = new EfRepository<Order>(context);

        var handler = new StubHttpMessageHandler(responder);
        var client = new CyberSourceMergedSpecClient(new HttpClient(handler), new CyberSourceMergedSpecClientOptions());
        var settings = Options.Create(new VisaSettings
        {
            Currency = "USD",
            MerchantId = "merchant",
            KeyId = "key",
            SecretKey = "secret"
        });

        var service = new VisaInvoiceService(client, invoiceRepository, orderRepository, settings);
        return new Harness(service, context, invoiceRepository, orderRepository, handler);
    }

    private static bool IsPath(HttpRequestMessage r, HttpMethod method, Func<string, bool> pathMatch) =>
        r.Method == method && pathMatch(r.RequestUri!.AbsolutePath);

    [Fact]
    public async Task RaiseInvoice_persists_local_record_and_bills_order_amount_in_usd()
    {
        var h = Build(r => IsPath(r, HttpMethod.Post, p => p.EndsWith("/invoices"))
            ? (HttpStatusCode.Created, """{"id":"INV-1","status":"DRAFT"}""")
            : (HttpStatusCode.InternalServerError, "{}"));

        var order = new OrderBuilder().WithDefaultValues(); // buyer 12345, one item 1.23 x 3 = 3.69
        await h.OrderRepository.AddAsync(order);

        var result = await h.Service.RaiseInvoiceAsync(order.Id, order.BuyerId, DueDate, "Ada", "ada@example.com", CancellationToken.None);

        Assert.Equal(ServiceOutcome.Ok, result.Outcome);
        Assert.Equal("INV-1", result.Value!.InvoiceId);
        Assert.Equal("Draft", result.Value.LocalStatus);
        Assert.Equal("DRAFT", result.Value.ProviderStatus);
        Assert.Equal("USD", result.Value.Currency);
        Assert.Equal(3.69m, result.Value.Amount);

        // The amount billed comes from the order, in USD.
        var body = h.Handler.RequestBodies.Single(b => b is not null)!;
        Assert.Contains("\"totalAmount\":\"3.69\"", body);
        Assert.Contains("\"currency\":\"USD\"", body);

        // A local record now ties the order to the provider invoice.
        var stored = await h.InvoiceRepository.FirstOrDefaultAsync(new InvoiceByProviderIdSpecification("INV-1"));
        Assert.NotNull(stored);
        Assert.Equal(order.BuyerId, stored!.BuyerId);
        Assert.Equal(order.Id, stored.OrderId);
        Assert.Equal(InvoiceStatus.Draft, stored.Status);
    }

    [Fact]
    public async Task RaiseInvoice_for_another_shoppers_order_is_not_found_and_calls_no_provider()
    {
        var h = Build(_ => (HttpStatusCode.InternalServerError, "{}"));

        var order = new OrderBuilder().WithDefaultValues(); // owned by 12345
        await h.OrderRepository.AddAsync(order);

        var result = await h.Service.RaiseInvoiceAsync(order.Id, "intruder@example.com", DueDate, null, null, CancellationToken.None);

        Assert.Equal(ServiceOutcome.NotFound, result.Outcome);
        Assert.Empty(h.Handler.Requests); // never reached the provider
    }

    [Fact]
    public async Task GetInvoice_belonging_to_another_shopper_is_not_found()
    {
        var h = Build(_ => (HttpStatusCode.OK, """{"id":"INV-1","status":"DRAFT"}"""));
        await SeedInvoice(h, owner: "owner@example.com");

        var result = await h.Service.GetInvoiceAsync("INV-1", "intruder@example.com", CancellationToken.None);

        Assert.Equal(ServiceOutcome.NotFound, result.Outcome);
        Assert.Empty(h.Handler.Requests);
    }

    [Fact]
    public async Task GetInvoice_after_withdrawal_never_hands_out_a_payment_link()
    {
        var h = Build(r => IsPath(r, HttpMethod.Get, p => p.EndsWith("/invoices/INV-1"))
            ? (HttpStatusCode.OK, """{"id":"INV-1","status":"CANCELED","invoiceInformation":{"paymentLink":"https://pay.example/INV-1"}}""")
            : (HttpStatusCode.InternalServerError, "{}"));

        await SeedInvoice(h, owner: "owner@example.com", status: InvoiceStatus.Withdrawn);

        var result = await h.Service.GetInvoiceAsync("INV-1", "owner@example.com", CancellationToken.None);

        Assert.Equal(ServiceOutcome.Ok, result.Outcome);
        Assert.Null(result.Value!.PaymentLink); // suppressed even though the provider echoed one
    }

    [Fact]
    public async Task Correct_an_issued_invoice_is_refused_with_conflict_and_calls_no_provider()
    {
        var h = Build(_ => (HttpStatusCode.OK, "{}"));
        await SeedInvoice(h, owner: "owner@example.com", status: InvoiceStatus.Issued);

        var result = await h.Service.CorrectInvoiceAsync("INV-1", "owner@example.com", DueDate, "New Name", null, CancellationToken.None);

        Assert.Equal(ServiceOutcome.Conflict, result.Outcome);
        Assert.Empty(h.Handler.Requests);
    }

    [Fact]
    public async Task Issue_puts_bill_to_shopper_and_returns_payment_link()
    {
        var h = Build(r => IsPath(r, HttpMethod.Post, p => p.EndsWith("/delivery"))
            ? (HttpStatusCode.OK, """{"id":"INV-1","status":"SENT","invoiceInformation":{"paymentLink":"https://pay.example/INV-1"}}""")
            : (HttpStatusCode.InternalServerError, "{}"));

        await SeedInvoice(h, owner: "owner@example.com");

        var result = await h.Service.IssueInvoiceAsync("INV-1", CancellationToken.None);

        Assert.Equal(ServiceOutcome.Ok, result.Outcome);
        Assert.Equal("Issued", result.Value!.LocalStatus);
        Assert.Equal("https://pay.example/INV-1", result.Value.PaymentLink);

        var stored = await h.InvoiceRepository.FirstOrDefaultAsync(new InvoiceByProviderIdSpecification("INV-1"));
        Assert.Equal(InvoiceStatus.Issued, stored!.Status);
    }

    [Fact]
    public async Task Withdraw_makes_bill_unpayable_with_no_payment_link()
    {
        var h = Build(r => IsPath(r, HttpMethod.Post, p => p.EndsWith("/cancelation"))
            ? (HttpStatusCode.OK, """{"id":"INV-1","status":"CANCELED"}""")
            : (HttpStatusCode.InternalServerError, "{}"));

        await SeedInvoice(h, owner: "owner@example.com");

        var result = await h.Service.WithdrawInvoiceAsync("INV-1", CancellationToken.None);

        Assert.Equal(ServiceOutcome.Ok, result.Outcome);
        Assert.Equal("Withdrawn", result.Value!.LocalStatus);
        Assert.Null(result.Value.PaymentLink);

        var stored = await h.InvoiceRepository.FirstOrDefaultAsync(new InvoiceByProviderIdSpecification("INV-1"));
        Assert.Equal(InvoiceStatus.Withdrawn, stored!.Status);
    }

    [Fact]
    public async Task Provider_rejection_becomes_InvoiceProviderException_carrying_the_status()
    {
        var h = Build(r => IsPath(r, HttpMethod.Post, p => p.EndsWith("/invoices"))
            ? (HttpStatusCode.BadRequest,
               """{"submitTimeUtc":"2026-08-31T10:00:00Z","status":"INVALID_REQUEST","reason":"INVALID_DATA","message":"bad"}""")
            : (HttpStatusCode.InternalServerError, "{}"));

        var order = new OrderBuilder().WithDefaultValues();
        await h.OrderRepository.AddAsync(order);

        var ex = await Assert.ThrowsAsync<InvoiceProviderException>(() =>
            h.Service.RaiseInvoiceAsync(order.Id, order.BuyerId, DueDate, null, null, CancellationToken.None));

        Assert.Equal(400, ex.ProviderStatusCode);

        // Nothing was persisted for a bill that was never raised.
        var stored = await h.InvoiceRepository.FirstOrDefaultAsync(new InvoiceByProviderIdSpecification("INV-1"));
        Assert.Null(stored);
    }

    private static async Task SeedInvoice(Harness h, string owner, InvoiceStatus status = InvoiceStatus.Draft)
    {
        var invoice = new Invoice(orderId: 1, buyerId: owner, providerInvoiceId: "INV-1", dueDate: DueDate,
            customerName: "Owner", customerEmail: owner, currency: "USD", amount: 10m);

        if (status == InvoiceStatus.Issued) invoice.MarkIssued();
        if (status == InvoiceStatus.Withdrawn) invoice.MarkWithdrawn();

        await h.InvoiceRepository.AddAsync(invoice);
    }
}
