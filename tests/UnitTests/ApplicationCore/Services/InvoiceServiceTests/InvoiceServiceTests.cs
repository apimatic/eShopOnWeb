using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.InvoiceServiceTests;

public class InvoiceServiceTests
{
    private const string Buyer = "shopper@example.com";
    private const string ProviderId = "PROV-1";

    private readonly IRepository<Invoice> _invoiceRepo = Substitute.For<IRepository<Invoice>>();
    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly IInvoicingProvider _provider = Substitute.For<IInvoicingProvider>();
    private readonly InvoiceService _service;

    public InvoiceServiceTests()
    {
        _service = new InvoiceService(_invoiceRepo, _orderRepo, _provider, new InvoicingSettings { Currency = "USD" });
        _invoiceRepo.AddAsync(Arg.Any<Invoice>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(ci.Arg<Invoice>()));
    }

    private static Order OrderOwnedBy(string buyerId) => new(
        buyerId,
        new Address("1 St", "City", "ST", "Country", "00000"),
        new List<OrderItem> { new(new CatalogItemOrdered(7, "Widget", "pic.png"), 10m, 2) });

    private static ProviderInvoice ProviderInvoice(string status, string? paymentLink = null) => new(
        Id: ProviderId, Status: status, PaymentLink: paymentLink, TotalAmount: "20.00", Currency: "USD",
        DueDate: null, CustomerName: "Shopper", CustomerEmail: Buyer, MerchantCustomerId: "eShopOnWeb-order-1",
        History: new List<ProviderInvoiceEvent>());

    private static Invoice DraftInvoice() => new(
        orderId: 1, buyerId: Buyer, providerInvoiceId: ProviderId, providerStatus: "DRAFT",
        dueDate: new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero), totalAmount: 20m, currency: "USD",
        customerName: "Shopper", customerEmail: Buyer, merchantCustomerId: "eShopOnWeb-order-1");

    [Fact]
    public async Task RaiseInvoice_PersistsDraft_WithAmountFromOrder()
    {
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>())
            .Returns(OrderOwnedBy(Buyer));
        _invoiceRepo.ListAsync(Arg.Any<InvoicesByOrderSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<Invoice>());
        _provider.RaiseAsync(Arg.Any<RaiseInvoiceCommand>(), Arg.Any<CancellationToken>())
            .Returns(ProviderInvoice("DRAFT"));

        var invoice = await _service.RaiseInvoiceForOrderAsync(1, Buyer, new DateOnly(2026, 9, 30), null);

        Assert.NotNull(invoice);
        Assert.Equal(InvoiceState.Draft, invoice!.State);
        Assert.Equal(ProviderId, invoice.ProviderInvoiceId);
        Assert.Equal(20m, invoice.TotalAmount); // 10 * 2, from the order — not from the caller
        Assert.Equal("USD", invoice.Currency);
        await _provider.Received(1).RaiseAsync(
            Arg.Is<RaiseInvoiceCommand>(c => c.TotalAmount == 20m && c.Currency == "USD" && c.Lines.Count == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RaiseInvoice_ReturnsNull_AndDoesNotCallProvider_WhenOrderNotOwned()
    {
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>())
            .Returns(OrderOwnedBy("someone-else@example.com"));

        var invoice = await _service.RaiseInvoiceForOrderAsync(1, Buyer, new DateOnly(2026, 9, 30), null);

        Assert.Null(invoice);
        await _provider.DidNotReceive().RaiseAsync(Arg.Any<RaiseInvoiceCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CorrectDraft_Throws_AndDoesNotCallProvider_WhenAlreadyIssued()
    {
        var invoice = DraftInvoice();
        invoice.MarkIssued("SENT", "https://pay/link");
        _invoiceRepo.FirstOrDefaultAsync(Arg.Any<InvoiceByProviderIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(invoice);

        await Assert.ThrowsAsync<InvoiceNotModifiableException>(() =>
            _service.CorrectDraftInvoiceAsync(ProviderId, Buyer, new DateOnly(2026, 10, 1), null));

        await _provider.DidNotReceive().UpdateAsync(Arg.Any<string>(), Arg.Any<UpdateInvoiceCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetForBuyer_ReturnsNull_WhenOwnedByAnotherShopper()
    {
        var invoice = DraftInvoice(); // owned by Buyer
        _invoiceRepo.FirstOrDefaultAsync(Arg.Any<InvoiceByProviderIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(invoice);

        var result = await _service.GetInvoiceForBuyerAsync(ProviderId, "intruder@example.com");

        Assert.Null(result);
        await _provider.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Issue_MarksIssued_AndExposesPaymentLink()
    {
        _invoiceRepo.FirstOrDefaultAsync(Arg.Any<InvoiceByProviderIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(DraftInvoice());
        _provider.IssueAsync(ProviderId, Arg.Any<CancellationToken>())
            .Returns(ProviderInvoice("SENT", "https://pay/link"));

        var invoice = await _service.IssueInvoiceAsync(ProviderId);

        Assert.NotNull(invoice);
        Assert.Equal(InvoiceState.Issued, invoice!.State);
        Assert.Equal("https://pay/link", invoice.PayableLink);
    }

    [Fact]
    public async Task Withdraw_MarksWithdrawn_AndHidesPaymentLink()
    {
        var invoice = DraftInvoice();
        invoice.MarkIssued("SENT", "https://pay/link");
        _invoiceRepo.FirstOrDefaultAsync(Arg.Any<InvoiceByProviderIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(invoice);
        _provider.WithdrawAsync(ProviderId, Arg.Any<CancellationToken>())
            .Returns(ProviderInvoice("CANCELED"));

        var result = await _service.WithdrawInvoiceAsync(ProviderId);

        Assert.NotNull(result);
        Assert.Equal(InvoiceState.Withdrawn, result!.State);
        Assert.Null(result.PayableLink);
    }
}
