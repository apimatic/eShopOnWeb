using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class InvoiceService : IInvoiceService
{
    private readonly IRepository<Invoice> _invoiceRepository;
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IVisaInvoiceGateway _gateway;
    private readonly IAppLogger<InvoiceService> _logger;

    public InvoiceService(
        IRepository<Invoice> invoiceRepository,
        IReadRepository<Order> orderRepository,
        IVisaInvoiceGateway gateway,
        IAppLogger<InvoiceService> logger)
    {
        _invoiceRepository = invoiceRepository;
        _orderRepository = orderRepository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<Result<InvoiceDetailView>> RaiseInvoiceForOrderAsync(int orderId, string buyerId, DateOnly dueDate, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);

        // A shopper may only bill their own order; do not reveal others' orders.
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            return Result<InvoiceDetailView>.NotFound($"Order {orderId} was not found.");
        }

        if (!order.OrderItems.Any())
        {
            return Result<InvoiceDetailView>.Error("Order has no items to bill.");
        }

        // One live bill per order: a bill already raised (and not withdrawn) blocks another.
        var existing = await _invoiceRepository.ListAsync(new InvoiceByOrderIdSpec(orderId), cancellationToken);
        if (existing.Any(i => i.Status != InvoiceStatus.Withdrawn))
        {
            return Result<InvoiceDetailView>.Error($"Order {orderId} has already been billed.");
        }

        var currency = _gateway.AccountCurrency;
        var lines = BuildLines(order);
        var description = $"eShopOnWeb order #{orderId}";

        var draft = new ProviderInvoiceDraft(
            InvoiceNumber: null,
            Description: description,
            DueDate: dueDate,
            CustomerName: buyerId,
            CustomerEmail: buyerId,
            CurrencyCode: currency,
            TotalAmount: order.Total(),
            Lines: lines);

        ProviderInvoiceState state;
        try
        {
            state = await _gateway.CreateDraftAsync(draft, cancellationToken);
        }
        catch (VisaInvoiceProviderException ex) when (ex.IsStateConflict)
        {
            return Result<InvoiceDetailView>.Error(ProviderMessage(ex));
        }

        var invoice = new Invoice(
            orderId: orderId,
            buyerId: buyerId,
            providerInvoiceId: state.Id,
            providerInvoiceNumber: state.InvoiceNumber ?? state.Id,
            description: description,
            currencyCode: state.CurrencyCode ?? currency,
            dueDate: dueDate,
            customerName: buyerId,
            customerEmail: buyerId,
            providerStatus: state.Status,
            items: order.OrderItems
                .Select(oi => new InvoiceItem(Sku(oi.ItemOrdered.CatalogItemId), oi.ItemOrdered.ProductName, oi.UnitPrice, oi.Units))
                .ToList());

        await _invoiceRepository.AddAsync(invoice, cancellationToken);
        _logger.LogInformation($"Raised bill {invoice.ProviderInvoiceId} for order {orderId} (buyer {buyerId}).");

        return Result<InvoiceDetailView>.Success(MapDetail(invoice, state));
    }

    public async Task<Result<InvoiceDetailView>> GetInvoiceAsync(string invoiceId, string? buyerId, bool isOperator, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceRepository.FirstOrDefaultAsync(new InvoiceByProviderIdSpec(invoiceId), cancellationToken);
        if (invoice is null || !CanAccess(invoice, buyerId, isOperator))
        {
            return Result<InvoiceDetailView>.NotFound($"Invoice {invoiceId} was not found.");
        }

        var state = await _gateway.GetAsync(invoice.ProviderInvoiceId, cancellationToken);
        invoice.SyncProviderStatus(state.Status);
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

        return Result<InvoiceDetailView>.Success(MapDetail(invoice, state));
    }

    public async Task<Result<InvoiceDetailView>> CorrectInvoiceAsync(string invoiceId, string buyerId, DateOnly? dueDate, string? customerName, string? customerEmail, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var invoice = await _invoiceRepository.FirstOrDefaultAsync(new InvoiceByProviderIdSpec(invoiceId), cancellationToken);
        if (invoice is null || !string.Equals(invoice.BuyerId, buyerId, StringComparison.Ordinal))
        {
            return Result<InvoiceDetailView>.NotFound($"Invoice {invoiceId} was not found.");
        }

        // Once the bill has been put to the shopper or withdrawn, correcting it is no
        // longer possible and the caller must be told so rather than it silently no-op.
        if (!invoice.CanBeAmended)
        {
            return Result<InvoiceDetailView>.Error(
                $"Invoice {invoiceId} can no longer be corrected because it has been {invoice.Status.ToString().ToLowerInvariant()}.");
        }

        var newDueDate = dueDate ?? invoice.DueDate;
        var newName = customerName ?? invoice.CustomerName;
        var newEmail = customerEmail ?? invoice.CustomerEmail;

        // The billed amount and lines always come from the order snapshot, never from
        // the caller, so a correction re-sends them unchanged.
        var draft = new ProviderInvoiceDraft(
            InvoiceNumber: invoice.ProviderInvoiceNumber,
            Description: invoice.Description,
            DueDate: newDueDate,
            CustomerName: newName,
            CustomerEmail: newEmail,
            CurrencyCode: invoice.CurrencyCode,
            TotalAmount: invoice.Total(),
            Lines: invoice.Items.Select(i => new ProviderInvoiceLine(i.ProductSku, i.ProductName, i.Units, i.UnitPrice)).ToList());

        ProviderInvoiceState state;
        try
        {
            state = await _gateway.UpdateAsync(invoice.ProviderInvoiceId, draft, cancellationToken);
        }
        catch (VisaInvoiceProviderException ex) when (ex.IsStateConflict)
        {
            return Result<InvoiceDetailView>.Error(ProviderMessage(ex));
        }

        invoice.ApplyCorrection(newDueDate, newName, newEmail);
        invoice.SyncProviderStatus(state.Status);
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
        _logger.LogInformation($"Corrected bill {invoice.ProviderInvoiceId} (buyer {buyerId}).");

        return Result<InvoiceDetailView>.Success(MapDetail(invoice, state));
    }

    public Task<Result<InvoiceDetailView>> IssueAsync(string invoiceId, CancellationToken cancellationToken = default)
        => TransitionAsync(invoiceId, issue: true, cancellationToken);

    public Task<Result<InvoiceDetailView>> WithdrawAsync(string invoiceId, CancellationToken cancellationToken = default)
        => TransitionAsync(invoiceId, issue: false, cancellationToken);

    private async Task<Result<InvoiceDetailView>> TransitionAsync(string invoiceId, bool issue, CancellationToken cancellationToken)
    {
        var invoice = await _invoiceRepository.FirstOrDefaultAsync(new InvoiceByProviderIdSpec(invoiceId), cancellationToken);
        if (invoice is null)
        {
            return Result<InvoiceDetailView>.NotFound($"Invoice {invoiceId} was not found.");
        }

        try
        {
            // The action itself moves the provider-side state; some transitions are
            // legitimately refused for the state the bill is in and surface as conflicts.
            if (issue)
            {
                var sent = await _gateway.SendAsync(invoice.ProviderInvoiceId, cancellationToken);
                invoice.MarkIssued(sent.Status);
            }
            else
            {
                var cancelled = await _gateway.CancelAsync(invoice.ProviderInvoiceId, cancellationToken);
                invoice.MarkWithdrawn(cancelled.Status);
            }
        }
        catch (VisaInvoiceProviderException ex) when (ex.IsStateConflict)
        {
            return Result<InvoiceDetailView>.Error(ProviderMessage(ex));
        }

        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

        // Re-read the authoritative state so the response reports the payment link
        // (now handed out after issue, and withheld after withdraw) and history.
        var state = await _gateway.GetAsync(invoice.ProviderInvoiceId, cancellationToken);
        invoice.SyncProviderStatus(state.Status);
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
        _logger.LogInformation($"{(issue ? "Issued" : "Withdrew")} bill {invoice.ProviderInvoiceId}.");

        return Result<InvoiceDetailView>.Success(MapDetail(invoice, state));
    }

    public async Task<IReadOnlyList<InvoiceSummaryView>> GetInvoicesForShopperAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var invoices = await _invoiceRepository.ListAsync(new CustomerInvoicesSpecification(buyerId), cancellationToken);
        return invoices.Select(i => new InvoiceSummaryView(
            InvoiceId: i.ProviderInvoiceId,
            OrderId: i.OrderId,
            LocalStatus: i.Status.ToString(),
            ProviderStatus: i.ProviderStatus,
            Amount: i.Total(),
            Currency: i.CurrencyCode,
            DueDate: i.DueDate)).ToList();
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerStates = await _gateway.ListRaisedBetweenAsync(from, to, cancellationToken);
        var localInvoices = await _invoiceRepository.ListAsync(new InvoicesCreatedBetweenSpecification(from, to), cancellationToken);

        var localById = localInvoices
            .GroupBy(i => i.ProviderInvoiceId)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var providerIds = new HashSet<string>(providerStates.Select(p => p.Id), StringComparer.Ordinal);

        var entries = new List<ReconciliationEntry>();

        foreach (var p in providerStates)
        {
            localById.TryGetValue(p.Id, out var local);
            var known = local is not null;
            entries.Add(new ReconciliationEntry(
                InvoiceId: p.Id,
                Source: known ? ReconciliationSource.Both : ReconciliationSource.ProviderOnly,
                BelongsToEShop: known,
                ProviderStatus: p.Status,
                LocalStatus: local?.Status.ToString(),
                Amount: p.TotalAmount ?? local?.Total(),
                Currency: p.CurrencyCode ?? local?.CurrencyCode,
                DueDate: p.DueDate ?? local?.DueDate,
                RaisedAt: p.CreatedDate,
                CustomerName: p.CustomerName ?? local?.CustomerName,
                OrderId: local?.OrderId));
        }

        // Bills eShop believes it raised in range that the provider has no record of in range.
        foreach (var local in localInvoices.Where(i => !providerIds.Contains(i.ProviderInvoiceId)))
        {
            entries.Add(new ReconciliationEntry(
                InvoiceId: local.ProviderInvoiceId,
                Source: ReconciliationSource.EShopOnly,
                BelongsToEShop: true,
                ProviderStatus: local.ProviderStatus,
                LocalStatus: local.Status.ToString(),
                Amount: local.Total(),
                Currency: local.CurrencyCode,
                DueDate: local.DueDate,
                RaisedAt: local.CreatedAt,
                CustomerName: local.CustomerName,
                OrderId: local.OrderId));
        }

        var ordered = entries.OrderBy(e => e.RaisedAt ?? DateTimeOffset.MaxValue).ThenBy(e => e.InvoiceId).ToList();

        return new ReconciliationReport(
            From: from,
            To: to,
            ProviderCount: providerStates.Count,
            EShopCount: localInvoices.Count,
            MatchedCount: ordered.Count(e => e.Source == ReconciliationSource.Both),
            ProviderOnlyCount: ordered.Count(e => e.Source == ReconciliationSource.ProviderOnly),
            EShopOnlyCount: ordered.Count(e => e.Source == ReconciliationSource.EShopOnly),
            Entries: ordered);
    }

    private static bool CanAccess(Invoice invoice, string? buyerId, bool isOperator)
        => isOperator || (buyerId is not null && string.Equals(invoice.BuyerId, buyerId, StringComparison.Ordinal));

    private static string Sku(int catalogItemId) => $"CATALOG-{catalogItemId}";

    private static IReadOnlyList<ProviderInvoiceLine> BuildLines(Order order)
        => order.OrderItems
            .Select(oi => new ProviderInvoiceLine(Sku(oi.ItemOrdered.CatalogItemId), oi.ItemOrdered.ProductName, oi.Units, oi.UnitPrice))
            .ToList();

    private static string ProviderMessage(VisaInvoiceProviderException ex)
        => ex.Reason is not null ? $"{ex.Reason}: {ex.Message}" : ex.Message;

    private static InvoiceDetailView MapDetail(Invoice invoice, ProviderInvoiceState state)
    {
        // The payment link is only handed out for a bill that has been put to the
        // shopper and not withdrawn; the provider withholds it otherwise too.
        var paymentLink = invoice.Status == InvoiceStatus.Issued ? state.PaymentLink : null;

        var lines = invoice.Items
            .Select(i => new InvoiceLineView(i.ProductSku, i.ProductName, i.Units, i.UnitPrice, i.UnitPrice * i.Units))
            .ToList();

        var history = (state.History ?? Array.Empty<ProviderInvoiceEvent>())
            .Select(h => new InvoiceEventView(h.Event, h.Date))
            .ToList();

        return new InvoiceDetailView(
            InvoiceId: invoice.ProviderInvoiceId,
            OrderId: invoice.OrderId,
            BuyerId: invoice.BuyerId,
            LocalStatus: invoice.Status.ToString(),
            ProviderStatus: state.Status ?? invoice.ProviderStatus,
            Amount: invoice.Total(),
            Currency: invoice.CurrencyCode,
            DueDate: invoice.DueDate,
            CustomerName: invoice.CustomerName,
            CustomerEmail: invoice.CustomerEmail,
            Description: invoice.Description,
            PaymentLink: paymentLink,
            Lines: lines,
            History: history);
    }
}
