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
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class InvoiceService : IInvoiceService
{
    // eShopOnWeb prices its catalog without recording a currency; this provider account bills in USD.
    private const string Currency = "USD";

    private readonly IRepository<Invoice> _invoices;
    private readonly IReadRepository<Order> _orders;
    private readonly IInvoiceProvider _provider;
    private readonly IAppLogger<InvoiceService> _logger;

    public InvoiceService(
        IRepository<Invoice> invoices,
        IReadRepository<Order> orders,
        IInvoiceProvider provider,
        IAppLogger<InvoiceService> logger)
    {
        _invoices = invoices;
        _orders = orders;
        _provider = provider;
        _logger = logger;
    }

    public async Task<InvoiceResult> RaiseInvoiceAsync(int orderId, string buyerId, DateOnly dueDate, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || !OwnedBy(order.BuyerId, buyerId))
        {
            // Do not reveal whether the order exists but belongs to someone else.
            throw new InvoiceNotFoundException($"Order {orderId} was not found.");
        }

        var existing = await _invoices.FirstOrDefaultAsync(new ActiveInvoiceForOrderSpecification(orderId), cancellationToken);
        if (existing is not null)
        {
            throw new InvoiceOperationException(
                $"Order {orderId} already has an active bill ({existing.ProviderInvoiceId}). Withdraw it before raising another.");
        }

        // What is billed comes from the order itself — its items and what they cost — never from the caller.
        var amount = order.Total();
        var (customerName, customerEmail) = DeriveCustomer(buyerId);
        var invoiceNumber = EShopInvoiceNumber.Create(orderId);

        var request = new ProviderInvoiceRequest
        {
            InvoiceNumber = invoiceNumber,
            Description = $"eShopOnWeb order #{orderId}",
            DueDate = dueDate,
            Currency = Currency,
            TotalAmount = amount,
            CustomerName = customerName,
            CustomerEmail = customerEmail,
            Lines = order.OrderItems.Select(item => new ProviderInvoiceLine(
                item.ItemOrdered.CatalogItemId.ToString(),
                item.ItemOrdered.ProductName,
                item.Units,
                item.UnitPrice)).ToList()
        };

        var created = await _provider.CreateInvoiceAsync(request, cancellationToken);

        var invoice = new Invoice(orderId, order.BuyerId, created.Id, Currency, amount, dueDate,
            customerName, customerEmail, created.Status);
        await _invoices.AddAsync(invoice, cancellationToken);

        _logger.LogInformation($"Raised bill {created.Id} for order {orderId} ({created.Status}).");

        return new InvoiceResult
        {
            InvoiceId = created.Id,
            OrderId = orderId,
            Status = created.Status,
            Amount = amount,
            Currency = Currency,
            DueDate = dueDate
        };
    }

    public async Task<InvoiceDetail> GetInvoiceForBuyerAsync(string providerInvoiceId, string buyerId, CancellationToken cancellationToken = default)
    {
        var invoice = await LoadOwnedAsync(providerInvoiceId, buyerId, cancellationToken);
        var providerInvoice = await _provider.GetInvoiceAsync(providerInvoiceId, cancellationToken);
        await SyncAsync(invoice, providerInvoice.Status, cancellationToken);
        return MapDetail(invoice, providerInvoice);
    }

    public async Task<InvoiceDetail> CorrectInvoiceAsync(string providerInvoiceId, string buyerId, InvoiceCorrection correction, CancellationToken cancellationToken = default)
    {
        var invoice = await LoadOwnedAsync(providerInvoiceId, buyerId, cancellationToken);

        if (!invoice.IsDraft)
        {
            var reason = invoice.IsWithdrawn ? "withdrawn" : "put to the shopper";
            throw new InvoiceOperationException(
                $"Bill {providerInvoiceId} has been {reason}; its due date and customer details can no longer be corrected.");
        }

        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(invoice.OrderId), cancellationToken);
        if (order is null)
        {
            throw new InvoiceNotFoundException($"Order {invoice.OrderId} was not found.");
        }

        var newDueDate = correction.DueDate ?? invoice.DueDate;
        var newName = correction.CustomerName ?? invoice.CustomerName;
        var newEmail = correction.CustomerEmail ?? invoice.CustomerEmail;

        // The amount is re-asserted from the order, so a correction can never change what is billed.
        var request = new ProviderInvoiceRequest
        {
            InvoiceNumber = invoice.ProviderInvoiceId,
            Description = $"eShopOnWeb order #{invoice.OrderId}",
            DueDate = newDueDate,
            Currency = Currency,
            TotalAmount = order.Total(),
            CustomerName = newName,
            CustomerEmail = newEmail,
            Lines = order.OrderItems.Select(item => new ProviderInvoiceLine(
                item.ItemOrdered.CatalogItemId.ToString(),
                item.ItemOrdered.ProductName,
                item.Units,
                item.UnitPrice)).ToList()
        };

        var updated = await _provider.UpdateInvoiceAsync(providerInvoiceId, request, cancellationToken);

        invoice.ApplyCorrection(newDueDate, newName, newEmail);
        invoice.SyncStatus(updated.Status);
        await _invoices.UpdateAsync(invoice, cancellationToken);

        _logger.LogInformation($"Corrected draft bill {providerInvoiceId}.");

        return MapDetail(invoice, updated);
    }

    public async Task<IReadOnlyList<InvoiceSummary>> GetInvoicesForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var invoices = await _invoices.ListAsync(new InvoicesByBuyerSpecification(buyerId), cancellationToken);
        return invoices.Select(invoice => new InvoiceSummary
        {
            InvoiceId = invoice.ProviderInvoiceId,
            OrderId = invoice.OrderId,
            Status = invoice.Status,
            Amount = invoice.Amount,
            Currency = invoice.Currency,
            DueDate = invoice.DueDate,
            IsIssued = invoice.IsIssued,
            IsWithdrawn = invoice.IsWithdrawn
        }).ToList();
    }

    public async Task<InvoiceDetail> IssueInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await LoadAsync(providerInvoiceId, cancellationToken);

        var issued = await _provider.IssueInvoiceAsync(providerInvoiceId, cancellationToken);
        invoice.SyncStatus(issued.Status);
        await _invoices.UpdateAsync(invoice, cancellationToken);

        _logger.LogInformation($"Issued bill {providerInvoiceId} to the shopper ({issued.Status}).");

        // Re-read so the response carries the payment link and the updated history.
        var detail = await _provider.GetInvoiceAsync(providerInvoiceId, cancellationToken);
        await SyncAsync(invoice, detail.Status, cancellationToken);
        return MapDetail(invoice, detail);
    }

    public async Task<InvoiceDetail> WithdrawInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await LoadAsync(providerInvoiceId, cancellationToken);

        var withdrawn = await _provider.WithdrawInvoiceAsync(providerInvoiceId, cancellationToken);
        invoice.SyncStatus(withdrawn.Status);
        await _invoices.UpdateAsync(invoice, cancellationToken);

        _logger.LogInformation($"Withdrew bill {providerInvoiceId} ({withdrawn.Status}).");

        var detail = await _provider.GetInvoiceAsync(providerInvoiceId, cancellationToken);
        await SyncAsync(invoice, detail.Status, cancellationToken);
        return MapDetail(invoice, detail);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new InvoiceOperationException("The 'to' date must not be earlier than the 'from' date.");
        }

        var providerRecords = await _provider.ListInvoicesAsync(cancellationToken);
        var providerAllIds = new HashSet<string>(providerRecords.Select(r => r.Id), StringComparer.OrdinalIgnoreCase);
        var providerInRange = providerRecords
            .Where(r => r.CreatedDate.HasValue && r.CreatedDate.Value >= from && r.CreatedDate.Value <= to)
            .ToList();

        var localAll = await _invoices.ListAsync(cancellationToken);
        var localByProviderId = localAll
            .GroupBy(i => i.ProviderInvoiceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var entries = new List<ReconciliationEntry>();

        // The provider's own record of bills raised in the range, lined up against eShop's records.
        foreach (var record in providerInRange)
        {
            localByProviderId.TryGetValue(record.Id, out var local);
            var presentInEShop = local is not null;
            var isEShop = presentInEShop || EShopInvoiceNumber.IsEShopInvoice(record.Id);

            entries.Add(new ReconciliationEntry
            {
                InvoiceId = record.Id,
                IsEShopInvoice = isEShop,
                PresentAtProvider = true,
                PresentInEShop = presentInEShop,
                IsDiscrepancy = isEShop && !presentInEShop,
                Status = record.Status,
                OrderId = local?.OrderId,
                Amount = local?.Amount ?? record.TotalAmount,
                Currency = local?.Currency ?? record.Currency,
                DueDate = local?.DueDate ?? record.DueDate,
                CreatedDate = record.CreatedDate,
                CustomerName = local?.CustomerName ?? record.CustomerName
            });
        }

        // The reverse: bills eShop believes it raised in the range that the provider does not know about at all.
        foreach (var local in localAll.Where(i => i.CreatedAt >= from && i.CreatedAt <= to))
        {
            if (providerAllIds.Contains(local.ProviderInvoiceId))
            {
                continue; // The provider knows this bill (it appears above, or was raised just outside the window).
            }

            entries.Add(new ReconciliationEntry
            {
                InvoiceId = local.ProviderInvoiceId,
                IsEShopInvoice = true,
                PresentAtProvider = false,
                PresentInEShop = true,
                IsDiscrepancy = true,
                Status = local.Status,
                OrderId = local.OrderId,
                Amount = local.Amount,
                Currency = local.Currency,
                DueDate = local.DueDate,
                CreatedDate = local.CreatedAt,
                CustomerName = local.CustomerName
            });
        }

        var ordered = entries
            .OrderByDescending(e => e.CreatedDate ?? DateTimeOffset.MinValue)
            .ToList();

        var summary = new ReconciliationSummary
        {
            Total = ordered.Count,
            InSync = ordered.Count(e => e.IsEShopInvoice && e.PresentAtProvider && e.PresentInEShop),
            AtProviderNotInEShop = ordered.Count(e => e.IsEShopInvoice && e.PresentAtProvider && !e.PresentInEShop),
            InEShopNotAtProvider = ordered.Count(e => e.IsEShopInvoice && !e.PresentAtProvider && e.PresentInEShop),
            External = ordered.Count(e => !e.IsEShopInvoice)
        };

        return new ReconciliationReport
        {
            From = from,
            To = to,
            Entries = ordered,
            Summary = summary
        };
    }

    private async Task<Invoice> LoadOwnedAsync(string providerInvoiceId, string buyerId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var invoice = await _invoices.FirstOrDefaultAsync(new InvoiceByProviderIdSpecification(providerInvoiceId), cancellationToken);
        if (invoice is null || !OwnedBy(invoice.BuyerId, buyerId))
        {
            throw new InvoiceNotFoundException($"Bill {providerInvoiceId} was not found.");
        }
        return invoice;
    }

    private async Task<Invoice> LoadAsync(string providerInvoiceId, CancellationToken cancellationToken)
    {
        var invoice = await _invoices.FirstOrDefaultAsync(new InvoiceByProviderIdSpecification(providerInvoiceId), cancellationToken);
        if (invoice is null)
        {
            throw new InvoiceNotFoundException($"Bill {providerInvoiceId} was not found.");
        }
        return invoice;
    }

    private async Task SyncAsync(Invoice invoice, string status, CancellationToken cancellationToken)
    {
        invoice.SyncStatus(status);
        await _invoices.UpdateAsync(invoice, cancellationToken);
    }

    private static InvoiceDetail MapDetail(Invoice invoice, ProviderInvoice providerInvoice)
    {
        // A withdrawn bill must no longer hand out a way to pay it; the provider omits the link too.
        var paymentLink = invoice.IsWithdrawn ? null : providerInvoice.PaymentLink;

        return new InvoiceDetail
        {
            InvoiceId = invoice.ProviderInvoiceId,
            OrderId = invoice.OrderId,
            Status = invoice.Status,
            Amount = invoice.Amount,
            Currency = invoice.Currency,
            DueDate = invoice.DueDate,
            CustomerName = invoice.CustomerName,
            CustomerEmail = invoice.CustomerEmail,
            IsIssued = invoice.IsIssued,
            IsWithdrawn = invoice.IsWithdrawn,
            PaymentLink = paymentLink,
            History = providerInvoice.History
                .Select(h => new InvoiceHistoryEntry(h.Event, h.Date))
                .ToList()
        };
    }

    private static bool OwnedBy(string ownerBuyerId, string callerBuyerId) =>
        string.Equals(ownerBuyerId, callerBuyerId, StringComparison.OrdinalIgnoreCase);

    private static (string? Name, string? Email) DeriveCustomer(string buyerId)
    {
        // eShop shoppers sign in with their email address as their user name. Use it as the invented
        // fixture customer detail (the provider's test environment never contacts a real customer).
        var isEmail = buyerId.Contains('@', StringComparison.Ordinal);
        return (buyerId, isEmail ? buyerId : null);
    }
}
