using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates invoicing use cases over the order model, eShop's own invoice records, and the
/// external provider gateway. Enforces that a shopper only ever sees or corrects their own bills;
/// operator callers (<c>isAdmin</c>) are exempt from that scoping.
/// </summary>
public class InvoiceService : IInvoiceService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<InvoiceRecord> _invoiceRepository;
    private readonly IInvoiceGateway _gateway;
    private readonly InvoicingSettings _settings;

    public InvoiceService(
        IRepository<Order> orderRepository,
        IRepository<InvoiceRecord> invoiceRepository,
        IInvoiceGateway gateway,
        InvoicingSettings settings)
    {
        _orderRepository = orderRepository;
        _invoiceRepository = invoiceRepository;
        _gateway = gateway;
        _settings = settings;
    }

    public async Task<RaisedInvoice> RaiseInvoiceAsync(
        string buyerId, bool isAdmin, int orderId, DateTime dueDate,
        string customerName, string customerEmail, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(customerName, nameof(customerName));
        Guard.Against.NullOrEmpty(customerEmail, nameof(customerEmail));

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }
        EnsureOwnership(order.BuyerId, buyerId, isAdmin, "order");

        var lines = BuildLines(order);
        var amount = order.Total();
        var description = $"eShopOnWeb order #{orderId}";
        var draft = new InvoiceDraft(orderId, customerName, customerEmail, dueDate.Date, description, _settings.Currency, amount, lines);

        var created = await _gateway.CreateInvoiceAsync(draft, cancellationToken);

        var record = new InvoiceRecord(
            orderId, order.BuyerId, created.Id, created.Status,
            order.OrderItems.Count, amount, _settings.Currency,
            customerName, customerEmail, dueDate.Date, description);
        record.SyncFromProvider(created.Status, created.PaymentLink);
        await _invoiceRepository.AddAsync(record, cancellationToken);

        return new RaisedInvoice(created.Id, created.Status);
    }

    public async Task<InvoiceDetails> GetInvoiceAsync(
        string buyerId, bool isAdmin, string invoiceId, CancellationToken cancellationToken = default)
    {
        var record = await LoadRecordAsync(invoiceId, cancellationToken);
        EnsureOwnership(record.BuyerId, buyerId, isAdmin, "invoice");

        var provider = await _gateway.GetInvoiceAsync(record.ProviderInvoiceId, cancellationToken);
        record.SyncFromProvider(provider.Status, provider.PaymentLink);
        await _invoiceRepository.UpdateAsync(record, cancellationToken);

        return ToDetails(record, provider);
    }

    public async Task<InvoiceDetails> CorrectInvoiceAsync(
        string buyerId, bool isAdmin, string invoiceId,
        DateTime? dueDate, string? customerName, string? customerEmail,
        CancellationToken cancellationToken = default)
    {
        var record = await LoadRecordAsync(invoiceId, cancellationToken);
        EnsureOwnership(record.BuyerId, buyerId, isAdmin, "invoice");

        if (!record.CanBeCorrected)
        {
            var reason = record.IsWithdrawn ? "withdrawn" : "put to the shopper";
            throw new InvoiceStateException(
                $"Invoice {invoiceId} can no longer be corrected because it has been {reason}.");
        }

        var newDueDate = (dueDate ?? record.DueDate).Date;
        var newName = string.IsNullOrWhiteSpace(customerName) ? record.CustomerName : customerName!;
        var newEmail = string.IsNullOrWhiteSpace(customerEmail) ? record.CustomerEmail : customerEmail!;

        // The amount and lines are re-sent from the order — they are never the caller's to restate.
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(record.OrderId), cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException(record.OrderId);
        }

        var amendment = new InvoiceAmendment(
            newDueDate, record.Description, newName, newEmail,
            record.Currency, order.Total(), BuildLines(order));

        var provider = await _gateway.UpdateInvoiceAsync(record.ProviderInvoiceId, amendment, cancellationToken);

        record.ApplyCorrection(newDueDate, newName, newEmail);
        record.SyncFromProvider(provider.Status, provider.PaymentLink);
        await _invoiceRepository.UpdateAsync(record, cancellationToken);

        return ToDetails(record, provider);
    }

    public async Task<InvoiceDetails> IssueInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default)
    {
        var record = await LoadRecordAsync(invoiceId, cancellationToken);
        var provider = await _gateway.IssueInvoiceAsync(record.ProviderInvoiceId, cancellationToken);
        record.SyncFromProvider(provider.Status, provider.PaymentLink);
        await _invoiceRepository.UpdateAsync(record, cancellationToken);
        return ToDetails(record, provider);
    }

    public async Task<InvoiceDetails> WithdrawInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default)
    {
        var record = await LoadRecordAsync(invoiceId, cancellationToken);
        var provider = await _gateway.WithdrawInvoiceAsync(record.ProviderInvoiceId, cancellationToken);
        record.SyncFromProvider(provider.Status, provider.PaymentLink);
        await _invoiceRepository.UpdateAsync(record, cancellationToken);
        return ToDetails(record, provider);
    }

    public async Task<IReadOnlyList<InvoiceSummaryView>> GetInvoicesForShopperAsync(
        string buyerId, CancellationToken cancellationToken = default)
    {
        var records = await _invoiceRepository.ListAsync(new CustomerInvoiceRecordsSpecification(buyerId), cancellationToken);
        return records.Select(ToSummary).ToList();
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerInvoices = await _gateway.ListInvoicesAsync(from, to, cancellationToken);
        var eShopRecords = await _invoiceRepository.ListAsync(new InvoiceRecordsInDateRangeSpecification(from, to), cancellationToken);

        var eShopById = eShopRecords
            .GroupBy(r => r.ProviderInvoiceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var providerIds = new HashSet<string>(providerInvoices.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);

        var entries = new List<ReconciliationEntry>();

        // The provider account carries bills that are not this application's; make plain which is which.
        foreach (var p in providerInvoices)
        {
            if (eShopById.TryGetValue(p.Id, out var rec))
            {
                entries.Add(new ReconciliationEntry(
                    p.Id, ReconciliationMatch.Matched, BelongsToEShop: true,
                    ProviderStatus: p.Status, EShopStatus: rec.Status,
                    OrderId: rec.OrderId, BuyerId: rec.BuyerId,
                    Amount: rec.Amount, Currency: rec.Currency,
                    CreatedDate: p.CreatedDate, CustomerName: p.CustomerName ?? rec.CustomerName));
            }
            else
            {
                entries.Add(new ReconciliationEntry(
                    p.Id, ReconciliationMatch.ProviderOnly, BelongsToEShop: false,
                    ProviderStatus: p.Status, EShopStatus: null,
                    OrderId: null, BuyerId: null,
                    Amount: p.Amount, Currency: p.Currency,
                    CreatedDate: p.CreatedDate, CustomerName: p.CustomerName));
            }
        }

        // Bills eShop believes it raised in range that the provider's list does not include.
        foreach (var rec in eShopRecords)
        {
            if (!providerIds.Contains(rec.ProviderInvoiceId))
            {
                entries.Add(new ReconciliationEntry(
                    rec.ProviderInvoiceId, ReconciliationMatch.EShopOnly, BelongsToEShop: true,
                    ProviderStatus: null, EShopStatus: rec.Status,
                    OrderId: rec.OrderId, BuyerId: rec.BuyerId,
                    Amount: rec.Amount, Currency: rec.Currency,
                    CreatedDate: rec.CreatedAt, CustomerName: rec.CustomerName));
            }
        }

        return new ReconciliationReport(
            from, to,
            ProviderInvoiceCount: providerInvoices.Count,
            EShopInvoiceCount: eShopRecords.Count,
            MatchedCount: entries.Count(e => e.Match == ReconciliationMatch.Matched),
            ProviderOnlyCount: entries.Count(e => e.Match == ReconciliationMatch.ProviderOnly),
            EShopOnlyCount: entries.Count(e => e.Match == ReconciliationMatch.EShopOnly),
            Entries: entries);
    }

    private async Task<InvoiceRecord> LoadRecordAsync(string invoiceId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));
        var record = await _invoiceRepository.FirstOrDefaultAsync(
            new InvoiceRecordByProviderIdSpecification(invoiceId), cancellationToken);
        if (record is null)
        {
            throw new InvoiceNotFoundException(invoiceId);
        }
        return record;
    }

    private static void EnsureOwnership(string ownerBuyerId, string callerBuyerId, bool isAdmin, string resource)
    {
        if (isAdmin)
        {
            return;
        }
        if (!string.Equals(ownerBuyerId, callerBuyerId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvoiceAccessDeniedException($"This {resource} belongs to another shopper.");
        }
    }

    private static List<InvoiceLine> BuildLines(Order order) =>
        order.OrderItems
            .Select(oi => new InvoiceLine(
                oi.ItemOrdered.ProductName, oi.Units, oi.UnitPrice, oi.ItemOrdered.CatalogItemId.ToString()))
            .ToList();

    private static InvoiceDetails ToDetails(InvoiceRecord record, ProviderInvoice provider) => new(
        InvoiceId: record.ProviderInvoiceId,
        OrderId: record.OrderId,
        BuyerId: record.BuyerId,
        Status: provider.Status,
        PutToShopper: InvoiceStatus.IsPutToShopper(provider.Status),
        Withdrawn: InvoiceStatus.IsWithdrawn(provider.Status),
        Amount: record.Amount,
        Currency: record.Currency,
        Description: record.Description,
        CustomerName: record.CustomerName,
        CustomerEmail: record.CustomerEmail,
        DueDate: record.DueDate,
        // The payment link is only handed out while the bill is genuinely payable.
        PaymentLink: InvoiceStatus.IsWithdrawn(provider.Status) ? null : provider.PaymentLink,
        CreatedAt: record.CreatedAt,
        ProviderHistory: provider.History);

    private static InvoiceSummaryView ToSummary(InvoiceRecord record) => new(
        InvoiceId: record.ProviderInvoiceId,
        OrderId: record.OrderId,
        Status: record.Status,
        PutToShopper: record.IsPutToShopper,
        Withdrawn: record.IsWithdrawn,
        Amount: record.Amount,
        Currency: record.Currency,
        DueDate: record.DueDate,
        CustomerName: record.CustomerName,
        PaymentLink: record.PaymentLink);
}
