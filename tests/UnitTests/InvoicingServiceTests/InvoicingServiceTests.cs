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
using Microsoft.eShopWeb.ApplicationCore.Invoicing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.InvoicingServiceTests;

public class InvoicingServiceTests
{
    private const string InstanceTag = "testtag1";

    private readonly IRepository<Order> _orderRepository = Substitute.For<IRepository<Order>>();
    private readonly IRepository<Invoice> _invoiceRepository = Substitute.For<IRepository<Invoice>>();
    private readonly IInvoiceProviderGateway _gateway = Substitute.For<IInvoiceProviderGateway>();
    private readonly IInvoicingInstance _instance = new InvoicingInstance(InstanceTag);

    private InvoicingService CreateService() => new(_orderRepository, _invoiceRepository, _gateway, _instance);

    private static Order BuildOrder(string buyerId)
    {
        var address = new Address("1 Main", "Town", "State", "Country", "00000");
        var item = new OrderItem(new CatalogItemOrdered(5, "Widget", "widget.png"), 10.00m, 2);
        return new Order(buyerId, address, new List<OrderItem> { item });
    }

    private static Invoice BuildInvoice(string buyerId, string providerInvoiceId, int orderId = 42)
    {
        return new Invoice(orderId, buyerId, providerInvoiceId, $"ESHOP-{orderId}", $"eshop-{orderId}",
            20.00m, "USD", new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero), "buyer", "buyer@example.com");
    }

    [Fact]
    public async Task RaiseInvoice_BuildsUsdRequestFromOrder_AndPersistsDraft()
    {
        var order = BuildOrder("buyer@example.com");
        _orderRepository.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>())
            .Returns(order);
        _invoiceRepository.FirstOrDefaultAsync(Arg.Any<InvoiceByOrderIdSpec>(), Arg.Any<CancellationToken>())
            .Returns((Invoice?)null);

        NewInvoiceRequest? captured = null;
        _gateway.RaiseAsync(Arg.Do<NewInvoiceRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(new InvoiceReceipt("PROV-1", "DRAFT"));

        var service = CreateService();
        var dueDate = new DateOnly(2026, 9, 30);

        var invoiceId = await service.RaiseInvoiceForOrderAsync(7, dueDate, "buyer@example.com", CancellationToken.None);

        Assert.Equal("PROV-1", invoiceId);
        Assert.NotNull(captured);
        Assert.Equal("USD", captured!.Currency);
        Assert.Equal("20.00", captured.TotalAmount);          // 10.00 * 2
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero), captured.DueDate);
        Assert.Single(captured.Lines);
        Assert.Equal("Widget", captured.Lines[0].ProductName);
        Assert.StartsWith("eshop-", captured.MerchantCustomerId);

        await _invoiceRepository.Received(1).AddAsync(
            Arg.Is<Invoice>(i => i.ProviderInvoiceId == "PROV-1" && i.Currency == "USD"
                && i.Amount == 20.00m && i.Status == InvoiceStatus.Draft),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RaiseInvoice_ForAnotherShoppersOrder_IsNotFound()
    {
        var order = BuildOrder("alice@example.com");
        _orderRepository.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>())
            .Returns(order);

        var service = CreateService();

        await Assert.ThrowsAsync<OrderNotFoundException>(() =>
            service.RaiseInvoiceForOrderAsync(7, new DateOnly(2026, 9, 30), "bob@example.com", CancellationToken.None));
        await _gateway.DidNotReceive().RaiseAsync(Arg.Any<NewInvoiceRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetInvoice_ForAnotherShopper_IsNotFound_ButOperatorSucceeds()
    {
        var invoice = BuildInvoice("alice@example.com", "PROV-9");
        _invoiceRepository.FirstOrDefaultAsync(Arg.Any<InvoiceByProviderIdSpec>(), Arg.Any<CancellationToken>())
            .Returns(invoice);
        _gateway.GetAsync("PROV-9", Arg.Any<CancellationToken>())
            .Returns(new InvoiceState("PROV-9", "CREATED", null, Array.Empty<InvoiceHistoryItem>()));

        var service = CreateService();

        await Assert.ThrowsAsync<InvoiceNotFoundException>(() =>
            service.GetInvoiceAsync("PROV-9", "bob@example.com", isOperator: false, CancellationToken.None));

        var details = await service.GetInvoiceAsync("PROV-9", "operator", isOperator: true, CancellationToken.None);
        Assert.Equal("PROV-9", details.InvoiceId);
    }

    [Fact]
    public async Task CorrectInvoice_OnceIssued_IsRefusedWith409_AndProviderNotCalled()
    {
        var invoice = BuildInvoice("buyer@example.com", "PROV-3");
        invoice.MarkIssued("https://pay.example/PROV-3");    // move out of Draft
        _invoiceRepository.FirstOrDefaultAsync(Arg.Any<InvoiceByProviderIdSpec>(), Arg.Any<CancellationToken>())
            .Returns(invoice);

        var service = CreateService();

        await Assert.ThrowsAsync<InvoiceStateException>(() =>
            service.CorrectInvoiceAsync("PROV-3", new DateOnly(2026, 10, 15), null,
                "buyer@example.com", isOperator: false, CancellationToken.None));
        await _gateway.DidNotReceive().CorrectAsync(Arg.Any<InvoiceCorrection>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Withdraw_ClearsPayLink_AndMarksWithdrawn()
    {
        var invoice = BuildInvoice("buyer@example.com", "PROV-4");
        invoice.MarkIssued("https://pay.example/PROV-4");
        _invoiceRepository.FirstOrDefaultAsync(Arg.Any<InvoiceByProviderIdSpec>(), Arg.Any<CancellationToken>())
            .Returns(invoice);

        var service = CreateService();
        await service.WithdrawInvoiceAsync("PROV-4", CancellationToken.None);

        Assert.True(invoice.IsWithdrawn);
        Assert.Null(invoice.PaymentLink);
        await _gateway.Received(1).WithdrawAsync("PROV-4", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reconcile_ClassifiesMatched_ForeignAndDiscrepancies()
    {
        // eShop invoices carry CreatedDate == UtcNow (set in the ctor); bound the range around now.
        var from = DateTimeOffset.UtcNow.AddMinutes(-5);
        var to = DateTimeOffset.UtcNow.AddMinutes(5);

        var eShopA = BuildInvoice("buyer@example.com", "P-A", orderId: 1);   // matched at provider
        var eShopC = BuildInvoice("buyer@example.com", "P-C", orderId: 3);   // missing from provider
        _invoiceRepository.ListAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Invoice> { eShopA, eShopC });

        var providerRecords = new List<ProviderInvoiceRecord>
        {
            new("P-A", "CREATED", $"eshop-{InstanceTag}-1", "20.00", "USD"),   // this deployment's, matched
            new("P-F", "PAID",    "someone-else",            "99.00", "USD"),  // foreign
            new("P-B", "CREATED", $"eshop-{InstanceTag}-9", "5.00",  "USD"),   // our marker, untracked
        };
        _gateway.ListAllAsync(Arg.Any<CancellationToken>()).Returns(providerRecords);

        var service = CreateService();
        var report = await service.ReconcileAsync(from, to, CancellationToken.None);

        Assert.Equal(3, report.ProviderInvoiceCount);
        Assert.Equal(2, report.EShopInvoiceCount);
        Assert.Equal(1, report.MatchedCount);

        var byId = report.Entries.ToDictionary(e => e.InvoiceId!);

        Assert.Equal(InvoiceOrigin.EShop, byId["P-A"].Origin);
        Assert.True(byId["P-A"].PresentAtProvider);
        Assert.True(byId["P-A"].PresentInEShop);
        Assert.Equal(ReconciliationDiscrepancy.None, byId["P-A"].Discrepancy);

        Assert.Equal(InvoiceOrigin.External, byId["P-F"].Origin);
        Assert.False(byId["P-F"].PresentInEShop);
        Assert.Equal(ReconciliationDiscrepancy.None, byId["P-F"].Discrepancy);

        Assert.Equal(InvoiceOrigin.EShop, byId["P-B"].Origin);
        Assert.False(byId["P-B"].PresentInEShop);
        Assert.Equal(ReconciliationDiscrepancy.MissingFromEShop, byId["P-B"].Discrepancy);

        Assert.False(byId["P-C"].PresentAtProvider);
        Assert.Equal(ReconciliationDiscrepancy.MissingFromProvider, byId["P-C"].Discrepancy);
    }
}
