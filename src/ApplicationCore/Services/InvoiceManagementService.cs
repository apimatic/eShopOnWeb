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
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class InvoiceManagementService : IInvoiceManagementService
{
    // The sandbox account bills in USD; every bill this application raises uses it.
    private const string BillingCurrency = "USD";

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Invoice> _invoiceRepository;
    private readonly IVisaInvoicingService _visaInvoicingService;

    public InvoiceManagementService(
        IRepository<Order> orderRepository,
        IRepository<Invoice> invoiceRepository,
        IVisaInvoicingService visaInvoicingService)
    {
        _orderRepository = orderRepository;
        _invoiceRepository = invoiceRepository;
        _visaInvoicingService = visaInvoicingService;
    }

    public async Task<InvoiceSnapshot> RaiseInvoiceForOrderAsync(
        int orderId,
        DateOnly dueDate,
        VisaCustomer? customer,
        string callerId,
        bool isOperator,
        CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || (!isOperator && !OwnedBy(order.BuyerId, callerId)))
        {
            throw new OrderNotFoundException(orderId);
        }

        var billedCustomer = customer ?? DefaultCustomerFor(order.BuyerId);
        var draft = BuildDraft(order, dueDate, billedCustomer);

        var state = await _visaInvoicingService.RaiseInvoiceAsync(draft, cancellationToken);

        var invoice = new Invoice(
            orderId: order.Id,
            buyerId: order.BuyerId,
            providerInvoiceId: state.ProviderInvoiceId,
            status: string.IsNullOrEmpty(state.Status) ? InvoiceStatus.Draft : state.Status,
            amount: draft.Amount,
            currency: draft.Currency,
            dueDate: dueDate,
            customerName: billedCustomer.Name,
            customerEmail: billedCustomer.Email);

        await _invoiceRepository.AddAsync(invoice, cancellationToken);

        return new InvoiceSnapshot(invoice, state);
    }

    public async Task<InvoiceSnapshot> GetInvoiceAsync(string invoiceId, string callerId, bool isOperator, CancellationToken cancellationToken = default)
    {
        var invoice = await FindOwnedInvoiceAsync(invoiceId, callerId, isOperator, cancellationToken);
        var state = await _visaInvoicingService.GetInvoiceAsync(invoiceId, cancellationToken);
        await SyncStatusAsync(invoice, state.Status, cancellationToken);
        return new InvoiceSnapshot(invoice, state);
    }

    public async Task<InvoiceSnapshot> CorrectInvoiceAsync(
        string invoiceId,
        DateOnly? dueDate,
        VisaCustomer? customer,
        string callerId,
        bool isOperator,
        CancellationToken cancellationToken = default)
    {
        var invoice = await FindOwnedInvoiceAsync(invoiceId, callerId, isOperator, cancellationToken);

        // Take the provider's authoritative state before deciding whether a correction is still allowed.
        var current = await _visaInvoicingService.GetInvoiceAsync(invoiceId, cancellationToken);
        await SyncStatusAsync(invoice, current.Status, cancellationToken);

        if (InvoiceStatus.IsWithdrawn(current.Status))
        {
            throw new InvoiceStateException("This bill has been withdrawn and can no longer be corrected.");
        }

        if (!InvoiceStatus.IsDraft(current.Status))
        {
            throw new InvoiceStateException("This bill has already been put to the shopper and can no longer be corrected.");
        }

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(invoice.OrderId), cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException(invoice.OrderId);
        }

        var newDueDate = dueDate ?? invoice.DueDate;
        var newCustomer = customer ?? new VisaCustomer(invoice.CustomerName, invoice.CustomerEmail);

        // The amount and lines are re-derived from the order so a correction can never change what is billed.
        var draft = BuildDraft(order, newDueDate, newCustomer);

        var state = await _visaInvoicingService.UpdateInvoiceAsync(invoiceId, draft, cancellationToken);

        invoice.ApplyCorrection(newDueDate, newCustomer.Name, newCustomer.Email);
        invoice.SyncStatus(state.Status);
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

        return new InvoiceSnapshot(invoice, state);
    }

    public async Task<InvoiceSnapshot> IssueInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await FindInvoiceAsync(invoiceId, cancellationToken);
        var state = await _visaInvoicingService.IssueInvoiceAsync(invoiceId, cancellationToken);
        await SyncStatusAsync(invoice, state.Status, cancellationToken);
        return new InvoiceSnapshot(invoice, state);
    }

    public async Task<InvoiceSnapshot> WithdrawInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await FindInvoiceAsync(invoiceId, cancellationToken);
        var state = await _visaInvoicingService.WithdrawInvoiceAsync(invoiceId, cancellationToken);
        await SyncStatusAsync(invoice, state.Status, cancellationToken);
        return new InvoiceSnapshot(invoice, state);
    }

    public async Task<IReadOnlyList<Invoice>> GetInvoicesForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var invoices = await _invoiceRepository.ListAsync(new InvoicesByBuyerSpecification(buyerId), cancellationToken);
        return invoices;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerInvoices = await _visaInvoicingService.ListInvoicesAsync(from, to, cancellationToken);

        var allLocal = await _invoiceRepository.ListAsync(cancellationToken);
        var localByProviderId = allLocal
            .Where(i => !string.IsNullOrEmpty(i.ProviderInvoiceId))
            .GroupBy(i => i.ProviderInvoiceId)
            .ToDictionary(g => g.Key, g => g.First());

        var localInRange = allLocal
            .Where(i => i.CreatedDate >= from && i.CreatedDate <= to)
            .ToList();

        var providerIds = new HashSet<string>(providerInvoices.Select(p => p.ProviderInvoiceId));

        var entries = new List<ReconciliationEntry>();
        var matched = 0;
        var providerOnly = 0;

        foreach (var provider in providerInvoices.OrderByDescending(p => p.CreatedUtc))
        {
            if (localByProviderId.TryGetValue(provider.ProviderInvoiceId, out var local))
            {
                matched++;
                entries.Add(new ReconciliationEntry(
                    InvoiceId: provider.ProviderInvoiceId,
                    Source: ReconciliationSource.Matched,
                    ProviderStatus: provider.Status,
                    ProviderCreatedUtc: provider.CreatedUtc,
                    Amount: provider.Amount ?? local.Amount,
                    Currency: provider.Currency ?? local.Currency,
                    OrderId: local.OrderId,
                    BuyerId: local.BuyerId,
                    CustomerName: string.IsNullOrEmpty(provider.CustomerName) ? local.CustomerName : provider.CustomerName,
                    EShopStatus: local.Status));
            }
            else
            {
                providerOnly++;
                entries.Add(new ReconciliationEntry(
                    InvoiceId: provider.ProviderInvoiceId,
                    Source: ReconciliationSource.ProviderOnly,
                    ProviderStatus: provider.Status,
                    ProviderCreatedUtc: provider.CreatedUtc,
                    Amount: provider.Amount,
                    Currency: provider.Currency,
                    OrderId: null,
                    BuyerId: null,
                    CustomerName: provider.CustomerName,
                    EShopStatus: null));
            }
        }

        var eShopOnly = 0;
        foreach (var local in localInRange.Where(i => !providerIds.Contains(i.ProviderInvoiceId)))
        {
            eShopOnly++;
            entries.Add(new ReconciliationEntry(
                InvoiceId: local.ProviderInvoiceId,
                Source: ReconciliationSource.EShopOnly,
                ProviderStatus: null,
                ProviderCreatedUtc: null,
                Amount: local.Amount,
                Currency: local.Currency,
                OrderId: local.OrderId,
                BuyerId: local.BuyerId,
                CustomerName: local.CustomerName,
                EShopStatus: local.Status));
        }

        return new ReconciliationReport(
            From: from,
            To: to,
            ProviderCount: providerInvoices.Count,
            EShopCount: localInRange.Count,
            MatchedCount: matched,
            ProviderOnlyCount: providerOnly,
            EShopOnlyCount: eShopOnly,
            Entries: entries);
    }

    private async Task<Invoice> FindInvoiceAsync(string invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await _invoiceRepository.FirstOrDefaultAsync(new InvoiceByProviderIdSpecification(invoiceId), cancellationToken);
        if (invoice is null)
        {
            throw new InvoiceNotFoundException(invoiceId);
        }

        return invoice;
    }

    private async Task<Invoice> FindOwnedInvoiceAsync(string invoiceId, string callerId, bool isOperator, CancellationToken cancellationToken)
    {
        var invoice = await FindInvoiceAsync(invoiceId, cancellationToken);

        // A shopper must never see another's bill: hide it as if it does not exist. Operators may act on any.
        if (!isOperator && !OwnedBy(invoice.BuyerId, callerId))
        {
            throw new InvoiceNotFoundException(invoiceId);
        }

        return invoice;
    }

    private async Task SyncStatusAsync(Invoice invoice, string? status, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(status) && !string.Equals(invoice.Status, status, StringComparison.OrdinalIgnoreCase))
        {
            invoice.SyncStatus(status);
            await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
        }
    }

    private static VisaInvoiceDraft BuildDraft(Order order, DateOnly dueDate, VisaCustomer customer)
    {
        var lines = order.OrderItems
            .Select(item => new VisaInvoiceLine(
                ProductName: item.ItemOrdered.ProductName,
                Sku: item.ItemOrdered.CatalogItemId.ToString(),
                Quantity: item.Units,
                UnitPrice: item.UnitPrice))
            .ToList();

        return new VisaInvoiceDraft(
            Amount: order.Total(),
            Currency: BillingCurrency,
            Description: $"eShopOnWeb order #{order.Id}",
            DueDate: dueDate,
            Customer: customer,
            Lines: lines);
    }

    private static VisaCustomer DefaultCustomerFor(string buyerId) => new(buyerId, buyerId);

    private static bool OwnedBy(string buyerId, string callerId) =>
        string.Equals(buyerId, callerId, StringComparison.OrdinalIgnoreCase);
}
