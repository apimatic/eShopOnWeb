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

public class InvoiceService : IInvoiceService
{
    // The provider account bills in USD; every bill eShop raises uses it rather than a
    // per-call choice. eShop's catalog does not record a currency of its own.
    private const string BillingCurrency = "USD";

    private readonly IRepository<Invoice> _invoiceRepository;
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IInvoiceProvider _invoiceProvider;
    private readonly IAppLogger<InvoiceService> _logger;

    public InvoiceService(IRepository<Invoice> invoiceRepository,
        IReadRepository<Order> orderRepository,
        IInvoiceProvider invoiceProvider,
        IAppLogger<InvoiceService> logger)
    {
        _invoiceRepository = invoiceRepository;
        _orderRepository = orderRepository;
        _invoiceProvider = invoiceProvider;
        _logger = logger;
    }

    public async Task<InvoiceView> RaiseInvoiceForOrderAsync(int orderId, string buyerId, bool isOperator,
        DateOnly dueDate, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || (!isOperator && !OwnsOrder(order, buyerId)))
        {
            // Hide the existence of orders that are not the caller's.
            throw new OrderNotFoundException(orderId);
        }

        var existing = await _invoiceRepository.FirstOrDefaultAsync(new InvoiceByOrderSpecification(orderId), cancellationToken);
        if (existing is not null)
        {
            throw new InvalidInvoiceOperationException(
                $"An invoice ({existing.ProviderInvoiceId}) has already been raised for order {orderId}.");
        }

        var customerName = order.BuyerId;
        var customerEmail = order.BuyerId;
        var draft = BuildDraft(order, dueDate, customerName, customerEmail);

        var providerResult = await _invoiceProvider.CreateDraftAsync(draft, cancellationToken);

        var invoice = new Invoice(
            orderId: order.Id,
            buyerId: order.BuyerId,
            providerInvoiceId: providerResult.Id,
            currencyCode: BillingCurrency,
            dueDate: dueDate,
            amount: order.Total(),
            status: string.IsNullOrWhiteSpace(providerResult.Status) ? InvoiceStatus.Draft : providerResult.Status,
            customerName: customerName,
            customerEmail: customerEmail);

        await _invoiceRepository.AddAsync(invoice, cancellationToken);

        _logger.LogInformation($"Raised invoice {invoice.ProviderInvoiceId} for order {orderId} (status {invoice.Status}).");

        return ToView(invoice, providerResult);
    }

    public async Task<InvoiceView> GetInvoiceAsync(string invoiceId, string callerBuyerId, bool isOperator,
        CancellationToken cancellationToken = default)
    {
        var invoice = await LoadOwnedInvoiceAsync(invoiceId, callerBuyerId, isOperator, cancellationToken);

        var providerResult = await _invoiceProvider.GetAsync(invoice.ProviderInvoiceId, cancellationToken);
        await SyncStatusAsync(invoice, providerResult.Status, cancellationToken);

        return ToView(invoice, providerResult);
    }

    public async Task<InvoiceView> CorrectInvoiceAsync(string invoiceId, string callerBuyerId, bool isOperator,
        DateOnly? dueDate, string? customerName, string? customerEmail, CancellationToken cancellationToken = default)
    {
        var invoice = await LoadOwnedInvoiceAsync(invoiceId, callerBuyerId, isOperator, cancellationToken);

        // The provider owns the truth of where the bill stands; refresh before deciding.
        var current = await _invoiceProvider.GetAsync(invoice.ProviderInvoiceId, cancellationToken);
        await SyncStatusAsync(invoice, current.Status, cancellationToken);

        if (!invoice.IsDraft)
        {
            throw new InvalidInvoiceOperationException(
                invoice.IsWithdrawn
                    ? $"Invoice {invoiceId} has been withdrawn and can no longer be corrected."
                    : $"Invoice {invoiceId} has already been put to the shopper and can no longer be corrected.");
        }

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(invoice.OrderId), cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException(invoice.OrderId);
        }

        var newDueDate = dueDate ?? invoice.DueDate;
        var newName = customerName ?? invoice.CustomerName;
        var newEmail = customerEmail ?? invoice.CustomerEmail;

        // Amount and line items always come from the order, never from the caller.
        var draft = BuildDraft(order, newDueDate, newName, newEmail);
        var providerResult = await _invoiceProvider.UpdateAsync(invoice.ProviderInvoiceId, draft, cancellationToken);

        invoice.ApplyCorrection(newDueDate, newName, newEmail);
        invoice.SyncStatus(providerResult.Status);
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

        return ToView(invoice, providerResult);
    }

    public async Task<InvoiceView> IssueInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await LoadInvoiceAsync(invoiceId, cancellationToken);

        // PublishAsync puts the bill to the shopper and reads the invoice back, so the returned
        // result already carries the payment link the caller now needs.
        var providerResult = await _invoiceProvider.PublishAsync(invoice.ProviderInvoiceId, cancellationToken);
        await SyncStatusAsync(invoice, providerResult.Status, cancellationToken);

        _logger.LogInformation($"Issued invoice {invoice.ProviderInvoiceId} (status {invoice.Status}).");

        return ToView(invoice, providerResult);
    }

    public async Task<InvoiceView> WithdrawInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await LoadInvoiceAsync(invoiceId, cancellationToken);

        var providerResult = await _invoiceProvider.CancelAsync(invoice.ProviderInvoiceId, cancellationToken);
        await SyncStatusAsync(invoice, providerResult.Status, cancellationToken);

        _logger.LogInformation($"Withdrew invoice {invoice.ProviderInvoiceId} (status {invoice.Status}).");

        return ToView(invoice, providerResult);
    }

    public async Task<IReadOnlyList<InvoiceSummaryView>> GetInvoicesForBuyerAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var invoices = await _invoiceRepository.ListAsync(new CustomerInvoicesSpecification(buyerId), cancellationToken);

        var summaries = new List<InvoiceSummaryView>(invoices.Count);
        foreach (var invoice in invoices)
        {
            // Reflect where each bill has got to by asking the provider, degrading to the last
            // known status if the provider cannot be reached for a particular bill.
            try
            {
                var providerResult = await _invoiceProvider.GetAsync(invoice.ProviderInvoiceId, cancellationToken);
                await SyncStatusAsync(invoice, providerResult.Status, cancellationToken);
            }
            catch (InvoiceProviderException ex)
            {
                _logger.LogWarning($"Could not refresh invoice {invoice.ProviderInvoiceId} from provider: {ex.Message}");
            }

            summaries.Add(new InvoiceSummaryView
            {
                InvoiceId = invoice.ProviderInvoiceId,
                OrderId = invoice.OrderId,
                Status = invoice.Status,
                CurrencyCode = invoice.CurrencyCode,
                Amount = invoice.Amount,
                DueDate = invoice.DueDate,
                CreatedAt = invoice.CreatedAt
            });
        }

        return summaries;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new InvalidInvoiceOperationException("The reconciliation range's 'to' must not be earlier than its 'from'.");
        }

        var providerInvoices = await _invoiceProvider.ListCreatedBetweenAsync(from, to, cancellationToken);
        var eShopInvoices = await _invoiceRepository.ListAsync(new InvoicesCreatedBetweenSpecification(from, to), cancellationToken);

        var eShopByProviderId = eShopInvoices
            .GroupBy(i => i.ProviderInvoiceId)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var entries = new List<ReconciliationEntry>();
        var seenEShop = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // The provider's own record, lined up against what eShop believes it raised.
        foreach (var providerInvoice in providerInvoices)
        {
            eShopByProviderId.TryGetValue(providerInvoice.Id, out var local);
            var recognized = local is not null;
            if (recognized)
            {
                seenEShop.Add(providerInvoice.Id);
            }

            entries.Add(new ReconciliationEntry
            {
                InvoiceId = providerInvoice.Id,
                Source = recognized ? ReconciliationSource.Matched : ReconciliationSource.ProviderOnly,
                RecognizedByEShop = recognized,
                ProviderStatus = providerInvoice.Status,
                OrderId = local?.OrderId,
                BuyerId = local?.BuyerId,
                Amount = local?.Amount ?? providerInvoice.Amount,
                CurrencyCode = local?.CurrencyCode ?? providerInvoice.CurrencyCode,
                CustomerName = local?.CustomerName ?? providerInvoice.CustomerName,
                ProviderCreatedDate = providerInvoice.CreatedDate,
                EShopCreatedAt = local?.CreatedAt
            });
        }

        // Bills eShop raised in the range that the provider's record did not return.
        foreach (var local in eShopInvoices)
        {
            if (seenEShop.Contains(local.ProviderInvoiceId))
            {
                continue;
            }

            entries.Add(new ReconciliationEntry
            {
                InvoiceId = local.ProviderInvoiceId,
                Source = ReconciliationSource.EShopOnly,
                RecognizedByEShop = true,
                ProviderStatus = null,
                OrderId = local.OrderId,
                BuyerId = local.BuyerId,
                Amount = local.Amount,
                CurrencyCode = local.CurrencyCode,
                CustomerName = local.CustomerName,
                ProviderCreatedDate = null,
                EShopCreatedAt = local.CreatedAt
            });
        }

        return new ReconciliationReport
        {
            From = from,
            To = to,
            ProviderInvoiceCount = providerInvoices.Count,
            EShopInvoiceCount = eShopInvoices.Count,
            MatchedCount = entries.Count(e => e.Source == ReconciliationSource.Matched),
            ProviderOnlyCount = entries.Count(e => e.Source == ReconciliationSource.ProviderOnly),
            EShopOnlyCount = entries.Count(e => e.Source == ReconciliationSource.EShopOnly),
            Entries = entries
        };
    }

    private async Task<Invoice> LoadOwnedInvoiceAsync(string invoiceId, string callerBuyerId, bool isOperator,
        CancellationToken cancellationToken)
    {
        var invoice = await LoadInvoiceAsync(invoiceId, cancellationToken);

        if (!isOperator && !string.Equals(invoice.BuyerId, callerBuyerId, StringComparison.OrdinalIgnoreCase))
        {
            // Never reveal that another shopper's bill exists.
            throw new InvoiceNotFoundException(invoiceId);
        }

        return invoice;
    }

    private async Task<Invoice> LoadInvoiceAsync(string invoiceId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));

        var invoice = await _invoiceRepository.FirstOrDefaultAsync(new InvoiceByProviderIdSpecification(invoiceId), cancellationToken);
        if (invoice is null)
        {
            throw new InvoiceNotFoundException(invoiceId);
        }

        return invoice;
    }

    private async Task SyncStatusAsync(Invoice invoice, string status, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(status) || string.Equals(status, invoice.Status, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        invoice.SyncStatus(status);
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
    }

    private static bool OwnsOrder(Order order, string buyerId) =>
        string.Equals(order.BuyerId, buyerId, StringComparison.OrdinalIgnoreCase);

    private static ProviderInvoiceDraft BuildDraft(Order order, DateOnly dueDate, string? customerName, string? customerEmail)
    {
        var lineItems = order.OrderItems.Select(item => new ProviderInvoiceLineItem(
            productName: item.ItemOrdered.ProductName,
            productSku: item.ItemOrdered.CatalogItemId.ToString(),
            quantity: item.Units,
            unitPrice: item.UnitPrice,
            totalAmount: item.UnitPrice * item.Units)).ToList();

        return new ProviderInvoiceDraft(
            description: $"eShopOnWeb order #{order.Id}",
            dueDate: dueDate,
            currencyCode: BillingCurrency,
            totalAmount: order.Total(),
            customerName: customerName,
            customerEmail: customerEmail,
            lineItems: lineItems);
    }

    private static InvoiceView ToView(Invoice invoice, ProviderInvoiceResult providerResult)
    {
        var history = providerResult.History
            .Select(e => new InvoiceHistoryEntry { Event = e.Name, Date = e.Date })
            .ToList();

        return new InvoiceView
        {
            InvoiceId = invoice.ProviderInvoiceId,
            OrderId = invoice.OrderId,
            BuyerId = invoice.BuyerId,
            Status = invoice.Status,
            CurrencyCode = invoice.CurrencyCode,
            Amount = invoice.Amount,
            DueDate = invoice.DueDate,
            CustomerName = invoice.CustomerName,
            CustomerEmail = invoice.CustomerEmail,
            PaymentLink = providerResult.PaymentLink,
            History = history
        };
    }
}
