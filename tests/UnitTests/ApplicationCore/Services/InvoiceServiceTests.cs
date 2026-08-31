using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class InvoiceServiceTests
{
    private const string Buyer = "shopper@example.com";
    private static readonly IReadOnlyList<ProviderInvoiceHistoryEntry> NoHistory = Array.Empty<ProviderInvoiceHistoryEntry>();

    private readonly IRepository<Invoice> _invoiceRepo = Substitute.For<IRepository<Invoice>>();
    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly IInvoicingProvider _provider = Substitute.For<IInvoicingProvider>();

    private InvoiceService CreateService() => new(_invoiceRepo, _orderRepo, _provider);

    private static void SetId(BaseEntity entity, int id) =>
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(entity, id);

    private static Order BuildOrder(int id, string buyerId)
    {
        var address = new Address("s", "c", "st", "co", "z");
        var items = new List<OrderItem>
        {
            new(new CatalogItemOrdered(1, "Widget", "widget.png"), 10m, 2) // 20.00
        };
        var order = new Order(buyerId, address, items);
        SetId(order, id);
        return order;
    }

    private static Invoice BuildInvoice(int id, string buyerId, string providerId, InvoiceStatus status = InvoiceStatus.Draft)
    {
        var invoice = new Invoice(
            orderId: 100,
            buyerId: buyerId,
            providerInvoiceId: providerId,
            merchantCustomerId: $"eshop:{buyerId}",
            description: "desc",
            amount: 20m,
            currency: "USD",
            dueDate: DateTimeOffset.UtcNow.AddDays(10),
            customer: new InvoiceCustomer(buyerId, buyerId),
            providerStatus: "DRAFT");
        SetId(invoice, id);
        if (status == InvoiceStatus.Issued) invoice.MarkIssued("SENT");
        if (status == InvoiceStatus.Withdrawn) invoice.MarkWithdrawn("CANCELED");
        return invoice;
    }

    [Fact]
    public async Task RaiseInvoice_RaisesDraftFromOrder_AndPersists()
    {
        var order = BuildOrder(100, Buyer);
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>()).Returns(order);
        _invoiceRepo.CountAsync(Arg.Any<ActiveInvoiceForOrderSpecification>(), Arg.Any<CancellationToken>()).Returns(0);
        _provider.RaiseAsync(Arg.Any<RaiseInvoiceRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProviderInvoiceResult("PROV-1", "DRAFT", null, NoHistory));
        _invoiceRepo.AddAsync(Arg.Any<Invoice>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<Invoice>());

        var due = DateTimeOffset.UtcNow.AddDays(30);
        var invoice = await CreateService().RaiseInvoiceForOrderAsync(100, Buyer, due);

        Assert.Equal("PROV-1", invoice.ProviderInvoiceId);
        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
        Assert.Equal(20m, invoice.Amount);            // sourced from the order, not the caller
        Assert.Equal("USD", invoice.Currency);
        // The amount sent to the provider equals the order total.
        await _provider.Received(1).RaiseAsync(Arg.Is<RaiseInvoiceRequest>(r => r.Amount == 20m && r.Currency == "USD"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RaiseInvoice_ForAnotherShoppersOrder_ThrowsAndDoesNotCallProvider()
    {
        var order = BuildOrder(100, "someone-else@example.com");
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>()).Returns(order);

        await Assert.ThrowsAsync<OrderNotFoundException>(
            () => CreateService().RaiseInvoiceForOrderAsync(100, Buyer, DateTimeOffset.UtcNow.AddDays(1)));

        await _provider.DidNotReceive().RaiseAsync(Arg.Any<RaiseInvoiceRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RaiseInvoice_WhenOrderAlreadyHasLiveInvoice_Throws()
    {
        var order = BuildOrder(100, Buyer);
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>()).Returns(order);
        _invoiceRepo.CountAsync(Arg.Any<ActiveInvoiceForOrderSpecification>(), Arg.Any<CancellationToken>()).Returns(1);

        await Assert.ThrowsAsync<InvoiceAlreadyExistsException>(
            () => CreateService().RaiseInvoiceForOrderAsync(100, Buyer, DateTimeOffset.UtcNow.AddDays(1)));
        await _provider.DidNotReceive().RaiseAsync(Arg.Any<RaiseInvoiceRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetInvoice_ForAnotherShopper_ThrowsNotFound()
    {
        _invoiceRepo.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(BuildInvoice(5, "owner@example.com", "P"));

        await Assert.ThrowsAsync<InvoiceNotFoundException>(
            () => CreateService().GetInvoiceForShopperAsync(5, Buyer));
    }

    [Fact]
    public async Task GetInvoice_DraftHasNoPaymentLink_IssuedDoes()
    {
        var draft = BuildInvoice(5, Buyer, "P");
        _invoiceRepo.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(draft);
        _provider.GetAsync("P", Arg.Any<CancellationToken>())
            .Returns(new ProviderInvoiceResult("P", "DRAFT", "https://pay/link", NoHistory));

        var draftDetails = await CreateService().GetInvoiceForShopperAsync(5, Buyer);
        Assert.Null(draftDetails.PaymentLink); // provider offered one, but a draft is not payable

        var issued = BuildInvoice(6, Buyer, "P2", InvoiceStatus.Issued);
        _invoiceRepo.GetByIdAsync(6, Arg.Any<CancellationToken>()).Returns(issued);
        _provider.GetAsync("P2", Arg.Any<CancellationToken>())
            .Returns(new ProviderInvoiceResult("P2", "SENT", "https://pay/link2", NoHistory));

        var issuedDetails = await CreateService().GetInvoiceForShopperAsync(6, Buyer);
        Assert.Equal("https://pay/link2", issuedDetails.PaymentLink);
    }

    [Fact]
    public async Task CorrectInvoice_WhenIssued_ThrowsAndDoesNotCallProvider()
    {
        var issued = BuildInvoice(7, Buyer, "P", InvoiceStatus.Issued);
        _invoiceRepo.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(issued);

        await Assert.ThrowsAsync<InvoiceNotCorrectableException>(
            () => CreateService().CorrectInvoiceAsync(7, Buyer, new InvoiceCorrectionRequest(DateTimeOffset.UtcNow, null, null)));

        await _provider.DidNotReceive().CorrectAsync(Arg.Any<string>(), Arg.Any<CorrectInvoiceRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IssueInvoice_WhenWithdrawn_Throws()
    {
        var withdrawn = BuildInvoice(8, Buyer, "P", InvoiceStatus.Withdrawn);
        _invoiceRepo.GetByIdAsync(8, Arg.Any<CancellationToken>()).Returns(withdrawn);

        await Assert.ThrowsAsync<InvoiceTransitionException>(() => CreateService().IssueInvoiceAsync(8));
        await _provider.DidNotReceive().IssueAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reconcile_ClassifiesEachRow()
    {
        // eShop side: P1 present at provider (reconciled), P2 absent at provider (missing-from-provider).
        var eShopMatched = BuildInvoice(1, Buyer, "P1");
        var eShopMissing = BuildInvoice(2, Buyer, "P2");
        _invoiceRepo.ListAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Invoice> { eShopMatched, eShopMissing });

        // Provider side: P1 (ours), P3 eShop-tagged but no local record (missing-from-eShop), P4 foreign.
        _provider.ListAllInvoicesAsync(Arg.Any<CancellationToken>()).Returns(new List<ProviderInvoiceSummary>
        {
            new("P1", "SENT", null, DateTimeOffset.UtcNow, "eshop:" + Buyer, 20m, "USD"),
            new("P3", "DRAFT", null, null, "eshop:other@example.com", 5m, "USD"),
            new("P4", "PAID", null, null, "someone-elses-scheme", 9m, "USD"),
        });

        var report = await CreateService().ReconcileAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        Assert.Equal(1, report.Summary.ReconciledCount);
        Assert.Equal(1, report.Summary.MissingFromProviderCount);
        Assert.Equal(1, report.Summary.MissingFromEShopCount);
        Assert.Equal(1, report.Summary.ForeignProviderInvoiceCount);

        var foreign = report.Entries.Single(e => e.Status == ReconciliationStatus.ForeignProviderInvoice);
        Assert.False(foreign.BelongsToEShop);
        Assert.Equal("P4", foreign.ProviderInvoiceId);

        var reconciled = report.Entries.Single(e => e.Status == ReconciliationStatus.Reconciled);
        Assert.True(reconciled.PresentAtProvider && reconciled.PresentInEShop);
        Assert.Equal(1, reconciled.InvoiceId);
    }
}
