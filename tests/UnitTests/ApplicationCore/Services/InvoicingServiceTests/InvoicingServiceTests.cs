using System;
using System.Collections.Generic;
using System.Linq;
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

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.InvoicingServiceTests;

public class InvoicingServiceTests
{
    private const string Buyer = "shopper@example.com";
    private const string OtherBuyer = "someone-else@example.com";

    private readonly IRepository<Invoice> _invoiceRepo = Substitute.For<IRepository<Invoice>>();
    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly IInvoiceProvider _provider = Substitute.For<IInvoiceProvider>();
    private readonly InvoicingService _service;

    public InvoicingServiceTests()
    {
        _service = new InvoicingService(_invoiceRepo, _orderRepo, _provider);
        // Default: order has no existing bills.
        _invoiceRepo.ListAsync(Arg.Any<InvoicesByOrderSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<Invoice>());
    }

    private static Order OrderFor(string buyer)
    {
        var items = new List<OrderItem>
        {
            new(new CatalogItemOrdered(1, "Widget", "pic.png"), 10.00m, 2),
            new(new CatalogItemOrdered(3, "Gadget", "pic.png"), 5.50m, 1)
        };
        return new Order(buyer, new Address("1 St", "Town", "ST", "Country", "00000"), items);
    }

    private static Invoice DraftInvoice(string buyer = Buyer, string providerId = "prov-1", int orderId = 1)
        => new(orderId, buyer, providerId, $"eShopOnWeb-Order-{orderId}", 25.50m, "USD",
            new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero), "Ada", "ada@example.com", "DRAFT");

    [Fact]
    public async Task Raise_PersistsInvoice_AndReturnsProviderId_InUsdFromOrder()
    {
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>())
            .Returns(OrderFor(Buyer));
        RaiseInvoiceCommand? captured = null;
        _provider.RaiseAsync(Arg.Do<RaiseInvoiceCommand>(c => captured = c), Arg.Any<CancellationToken>())
            .Returns(new ProviderInvoice("prov-99", "DRAFT", null, Array.Empty<ProviderInvoiceEvent>()));

        var id = await _service.RaiseInvoiceAsync(1, Buyer,
            new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero), null, null);

        Assert.Equal("prov-99", id);
        await _invoiceRepo.Received(1).AddAsync(Arg.Is<Invoice>(i =>
            i.ProviderInvoiceId == "prov-99" && i.BuyerId == Buyer && i.Currency == "USD"
            && i.Amount == 25.50m && i.State == InvoiceState.Draft), Arg.Any<CancellationToken>());
        Assert.NotNull(captured);
        Assert.Equal("USD", captured!.Currency);
        Assert.Equal(25.50m, captured.Amount);              // 2*10.00 + 1*5.50, from the order
        Assert.Equal("eShopOnWeb-Order-1", captured.MerchantReference);
        Assert.Equal(2, captured.Lines.Count);
        Assert.All(captured.Lines, l => Assert.False(string.IsNullOrEmpty(l.ProductSku)));
    }

    [Fact]
    public async Task Raise_ThrowsNotFound_WhenOrderBelongsToAnotherShopper()
    {
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>())
            .Returns(OrderFor(OtherBuyer));

        await Assert.ThrowsAsync<InvoiceNotFoundException>(() =>
            _service.RaiseInvoiceAsync(1, Buyer, DateTimeOffset.UtcNow, null, null));
        await _provider.DidNotReceive().RaiseAsync(Arg.Any<RaiseInvoiceCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Raise_ThrowsState_WhenOrderAlreadyHasActiveBill()
    {
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>())
            .Returns(OrderFor(Buyer));
        _invoiceRepo.ListAsync(Arg.Any<InvoicesByOrderSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<Invoice> { DraftInvoice() });

        await Assert.ThrowsAsync<InvoiceStateException>(() =>
            _service.RaiseInvoiceAsync(1, Buyer, DateTimeOffset.UtcNow, null, null));
    }

    [Fact]
    public async Task Get_ThrowsNotFound_ForAnotherShoppersBill()
    {
        _invoiceRepo.FirstOrDefaultAsync(Arg.Any<InvoiceByProviderIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(DraftInvoice(OtherBuyer));

        await Assert.ThrowsAsync<InvoiceNotFoundException>(() =>
            _service.GetInvoiceForShopperAsync("prov-1", Buyer));
        await _provider.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Correct_ThrowsState_WhenAlreadyIssued()
    {
        var issued = DraftInvoice();
        issued.MarkIssued("SENT", "https://pay/x");
        _invoiceRepo.FirstOrDefaultAsync(Arg.Any<InvoiceByProviderIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(issued);

        await Assert.ThrowsAsync<InvoiceStateException>(() =>
            _service.CorrectInvoiceAsync("prov-1", Buyer, DateTimeOffset.UtcNow, null, null));
        await _provider.DidNotReceive().CorrectAsync(Arg.Any<string>(), Arg.Any<CorrectInvoiceCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Correct_ReSendsOrderAmount_AndUpdatesDueDateAndCustomer()
    {
        var draft = DraftInvoice();
        _invoiceRepo.FirstOrDefaultAsync(Arg.Any<InvoiceByProviderIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(draft);
        CorrectInvoiceCommand? cmd = null;
        _provider.CorrectAsync("prov-1", Arg.Do<CorrectInvoiceCommand>(c => cmd = c), Arg.Any<CancellationToken>())
            .Returns(new ProviderInvoice("prov-1", "CREATED", null, Array.Empty<ProviderInvoiceEvent>()));

        var newDue = new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);
        var details = await _service.CorrectInvoiceAsync("prov-1", Buyer, newDue, "Grace", null);

        Assert.Equal(newDue, details.DueDate);
        Assert.Equal("Grace", details.CustomerName);
        Assert.Equal("ada@example.com", details.CustomerEmail);  // unchanged
        Assert.NotNull(cmd);
        Assert.Equal(25.50m, cmd!.Amount);                       // amount still from the order, not correctable
        Assert.Equal("USD", cmd.Currency);
    }

    [Fact]
    public async Task Issue_MarksIssued_AndSurfacesPaymentLink()
    {
        _invoiceRepo.FirstOrDefaultAsync(Arg.Any<InvoiceByProviderIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(DraftInvoice());
        _provider.IssueAsync("prov-1", Arg.Any<CancellationToken>())
            .Returns(new ProviderInvoice("prov-1", "SENT", "https://pay/link", Array.Empty<ProviderInvoiceEvent>()));

        var details = await _service.IssueInvoiceAsync("prov-1");

        Assert.Equal("Issued", details.State);
        Assert.Equal("https://pay/link", details.PaymentLink);
    }

    [Fact]
    public async Task Issue_ThrowsState_WhenWithdrawn()
    {
        var withdrawn = DraftInvoice();
        withdrawn.MarkWithdrawn("CANCELED");
        _invoiceRepo.FirstOrDefaultAsync(Arg.Any<InvoiceByProviderIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(withdrawn);

        await Assert.ThrowsAsync<InvoiceStateException>(() => _service.IssueInvoiceAsync("prov-1"));
        await _provider.DidNotReceive().IssueAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Withdraw_MarksWithdrawn_AndDropsPaymentLink()
    {
        var issued = DraftInvoice();
        issued.MarkIssued("SENT", "https://pay/x");
        _invoiceRepo.FirstOrDefaultAsync(Arg.Any<InvoiceByProviderIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(issued);
        _provider.WithdrawAsync("prov-1", Arg.Any<CancellationToken>())
            .Returns(new ProviderInvoice("prov-1", "CANCELED", null, Array.Empty<ProviderInvoiceEvent>()));

        var details = await _service.WithdrawInvoiceAsync("prov-1");

        Assert.Equal("Withdrawn", details.State);
        Assert.Null(details.PaymentLink);
    }

    [Fact]
    public async Task Reconcile_DistinguishesEShopFromExternal_AndBothDiscrepancyDirections()
    {
        var from = new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

        // eShop's own records in range: "prov-1" (also at provider) and "prov-missing" (not at provider).
        var localMatched = new Invoice(1, Buyer, "prov-1", "eShopOnWeb-Order-1", 25.50m, "USD",
            to, "Ada", "ada@example.com", "SENT") { };
        var localMissing = new Invoice(2, Buyer, "prov-missing", "eShopOnWeb-Order-2", 8.50m, "USD",
            to, "Ada", "ada@example.com", "DRAFT");
        _invoiceRepo.ListAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Invoice> { localMatched, localMissing });

        _provider.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ProviderInvoicePage(new List<ProviderInvoiceSummary>
            {
                new("prov-1", "SENT", null, "eShopOnWeb-Order-1", "Ada", "25.50", "USD"),     // mine + in eShop
                new("prov-eshop-only", "SENT", null, "eShopOnWeb-Order-9", "Zoe", "12.00", "USD"), // mine, not in eShop
                new("ext-1", "CREATED", null, null, "Stranger", "99.00", "USD")                // external
            }, 3));

        var report = await _service.ReconcileAsync(from, to);

        Assert.False(report.ProviderCreatedDatesAvailable);
        Assert.Equal(1, report.Summary.Matched);                 // prov-1
        Assert.Equal(1, report.Summary.EShopMissingAtProvider);  // prov-missing
        Assert.Equal(1, report.Summary.ProviderMissingInEShop);  // prov-eshop-only
        Assert.Equal(1, report.Summary.ExternalAtProvider);      // ext-1

        var ext = Assert.Single(report.Entries.Where(e => e.Origin == "External"));
        Assert.Equal("ext-1", ext.InvoiceId);
        Assert.False(ext.PresentInEShop);
    }
}
