using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class InvoiceService : IInvoiceService
{
    private readonly IRepository<Invoice> _invoiceRepository;
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IInvoiceProvider _invoiceProvider;
    private readonly IAppLogger<InvoiceService> _logger;

    public InvoiceService(
        IRepository<Invoice> invoiceRepository,
        IReadRepository<Order> orderRepository,
        IInvoiceProvider invoiceProvider,
        IAppLogger<InvoiceService> logger)
    {
        _invoiceRepository = invoiceRepository;
        _orderRepository = orderRepository;
        _invoiceProvider = invoiceProvider;
        _logger = logger;
    }

    public async Task<Invoice> RaiseInvoiceForOrderAsync(int orderId, string buyerId, DateOnly dueDate,
        string? customerName, string? customerEmail, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            // A shopper may only raise a bill against their own order; do not reveal others' orders.
            throw new OrderNotFoundException($"Order {orderId} was not found.");
        }

        var name = string.IsNullOrWhiteSpace(customerName) ? buyerId : customerName!.Trim();
        var email = string.IsNullOrWhiteSpace(customerEmail) ? buyerId : customerEmail!.Trim();
        var invoiceNumber = GenerateInvoiceNumber(orderId);
        var amount = order.Total();

        // Create eShop's record of the bill first (in memory), then raise it with the provider so that
        // the provider-assigned identifier can be recorded before anything is persisted.
        var invoice = new Invoice(orderId, buyerId, invoiceNumber, amount, InvoicingConstants.Currency,
            dueDate, name, email);

        var providerRequest = BuildProviderRequest(invoice, order);
        var providerState = await _invoiceProvider.RaiseAsync(providerRequest, cancellationToken);

        invoice.SetProviderInvoiceId(providerState.ProviderInvoiceId);
        await _invoiceRepository.AddAsync(invoice, cancellationToken);

        _logger.LogInformation(
            $"Raised bill {invoiceNumber} (provider id {providerState.ProviderInvoiceId}) for order {orderId}.");

        return invoice;
    }

    public async Task<InvoiceView> GetInvoiceAsync(string invoiceId, string requestingBuyerId, bool isOperator,
        CancellationToken cancellationToken = default)
    {
        var invoice = await LoadOwnedInvoiceAsync(invoiceId, requestingBuyerId, isOperator, cancellationToken);
        var providerState = await _invoiceProvider.GetAsync(invoice.ProviderInvoiceId, cancellationToken);
        return new InvoiceView(invoice, providerState);
    }

    public async Task<InvoiceView> CorrectInvoiceAsync(string invoiceId, string requestingBuyerId, bool isOperator,
        DateOnly? dueDate, string? customerName, string? customerEmail, CancellationToken cancellationToken = default)
    {
        var invoice = await LoadOwnedInvoiceAsync(invoiceId, requestingBuyerId, isOperator, cancellationToken);

        var newDueDate = dueDate ?? invoice.DueDate;
        var newName = string.IsNullOrWhiteSpace(customerName) ? invoice.CustomerName : customerName!.Trim();
        var newEmail = string.IsNullOrWhiteSpace(customerEmail) ? invoice.CustomerEmail : customerEmail!.Trim();

        // Enforce, before touching the provider, that a bill already put to the shopper or withdrawn
        // cannot be corrected — the caller is told rather than the change silently doing nothing.
        invoice.CorrectDetails(newDueDate, newName, newEmail);

        // What is billed still comes from the order, so the amount is never taken from the caller.
        var order = await RequireOrderAsync(invoice.OrderId, cancellationToken);
        var providerRequest = BuildProviderRequest(invoice, order);
        var providerState = await _invoiceProvider.UpdateAsync(invoice.ProviderInvoiceId, providerRequest, cancellationToken);

        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
        return new InvoiceView(invoice, providerState);
    }

    public async Task<InvoiceView> IssueInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await RequireInvoiceAsync(invoiceId, cancellationToken);

        if (invoice.Status == InvoiceStatus.Withdrawn)
        {
            throw new InvalidInvoiceOperationException(
                $"Invoice {invoice.InvoiceNumber} has been withdrawn and can no longer be issued to the customer.");
        }

        var providerState = await _invoiceProvider.IssueAsync(invoice.ProviderInvoiceId, cancellationToken);
        invoice.MarkIssued();
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

        _logger.LogInformation($"Issued bill {invoice.InvoiceNumber} to the customer.");
        return new InvoiceView(invoice, providerState);
    }

    public async Task<InvoiceView> WithdrawInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await RequireInvoiceAsync(invoiceId, cancellationToken);

        var providerState = await _invoiceProvider.WithdrawAsync(invoice.ProviderInvoiceId, cancellationToken);
        invoice.MarkWithdrawn();
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

        _logger.LogInformation($"Withdrew bill {invoice.InvoiceNumber}.");
        return new InvoiceView(invoice, providerState);
    }

    public async Task<IReadOnlyList<Invoice>> GetInvoicesForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _invoiceRepository.ListAsync(new InvoicesByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The reconciliation range end must not be before its start.", nameof(to));
        }

        var providerInvoices = await _invoiceProvider.ListRaisedBetweenAsync(from, to, cancellationToken);
        var allLocalInvoices = await _invoiceRepository.ListAsync(cancellationToken);
        var localInRange = allLocalInvoices
            .Where(i => i.CreatedDate >= from && i.CreatedDate <= to)
            .ToList();

        var localByProviderId = localInRange
            .Where(i => !string.IsNullOrEmpty(i.ProviderInvoiceId))
            .GroupBy(i => i.ProviderInvoiceId)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var entries = new List<ReconciliationEntry>();
        var matchedProviderIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var provider in providerInvoices)
        {
            if (localByProviderId.TryGetValue(provider.ProviderInvoiceId, out var local))
            {
                matchedProviderIds.Add(provider.ProviderInvoiceId);
                entries.Add(new ReconciliationEntry(
                    provider.ProviderInvoiceId,
                    local.InvoiceNumber,
                    IsEShopInvoice: true,
                    ReconciliationCategory.Matched,
                    provider.Status,
                    local.Status.ToString(),
                    local.OrderId,
                    local.BuyerId,
                    local.Amount,
                    local.Currency,
                    provider.CreatedDate ?? local.CreatedDate));
            }
            else
            {
                var isEShop = IsEShopReference(provider.CustomerReference);
                entries.Add(new ReconciliationEntry(
                    provider.ProviderInvoiceId,
                    provider.InvoiceNumber,
                    IsEShopInvoice: isEShop,
                    isEShop ? ReconciliationCategory.MissingFromEShop : ReconciliationCategory.ForeignToEShop,
                    provider.Status,
                    LocalStatus: null,
                    OrderId: null,
                    BuyerId: BuyerIdFromReference(provider.CustomerReference),
                    provider.TotalAmount,
                    provider.Currency,
                    provider.CreatedDate));
            }
        }

        foreach (var local in localInRange)
        {
            if (!string.IsNullOrEmpty(local.ProviderInvoiceId) && matchedProviderIds.Contains(local.ProviderInvoiceId))
            {
                continue;
            }

            entries.Add(new ReconciliationEntry(
                local.ProviderInvoiceId,
                local.InvoiceNumber,
                IsEShopInvoice: true,
                ReconciliationCategory.MissingFromProvider,
                ProviderStatus: null,
                local.Status.ToString(),
                local.OrderId,
                local.BuyerId,
                local.Amount,
                local.Currency,
                local.CreatedDate));
        }

        var ordered = entries
            .OrderByDescending(e => e.CreatedDate ?? DateTimeOffset.MinValue)
            .ToList();

        return new ReconciliationReport(
            from,
            to,
            ProviderInvoiceCount: providerInvoices.Count,
            EShopInvoiceCount: ordered.Count(e => e.IsEShopInvoice),
            ordered);
    }

    private async Task<Invoice> LoadOwnedInvoiceAsync(string invoiceId, string requestingBuyerId, bool isOperator,
        CancellationToken cancellationToken)
    {
        var invoice = await RequireInvoiceAsync(invoiceId, cancellationToken);

        if (!isOperator && !string.Equals(invoice.BuyerId, requestingBuyerId, StringComparison.Ordinal))
        {
            // Do not reveal that another shopper's bill exists.
            throw new InvoiceNotFoundException($"Invoice {invoiceId} was not found.");
        }

        return invoice;
    }

    private async Task<Invoice> RequireInvoiceAsync(string invoiceId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));
        var invoice = await _invoiceRepository.FirstOrDefaultAsync(new InvoiceByProviderIdSpec(invoiceId), cancellationToken);
        if (invoice is null)
        {
            throw new InvoiceNotFoundException($"Invoice {invoiceId} was not found.");
        }

        return invoice;
    }

    private async Task<Order> RequireOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException($"Order {orderId} backing this bill was not found.");
        }

        return order;
    }

    private static InvoiceProviderRequest BuildProviderRequest(Invoice invoice, Order order)
    {
        var lineItems = order.OrderItems
            .Select(item => new InvoiceProviderLineItem(
                item.ItemOrdered.ProductName,
                item.ItemOrdered.CatalogItemId.ToString(CultureInfo.InvariantCulture),
                item.Units,
                item.UnitPrice))
            .ToList();

        return new InvoiceProviderRequest(
            invoice.InvoiceNumber,
            $"eShopOnWeb order {order.Id}",
            invoice.DueDate,
            invoice.Currency,
            order.Total(),
            lineItems,
            invoice.CustomerName,
            invoice.CustomerEmail,
            $"{InvoicingConstants.MerchantReferencePrefix}{invoice.BuyerId}");
    }

    private static string GenerateInvoiceNumber(int orderId)
    {
        var suffix = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
        return $"{InvoicingConstants.InvoiceNumberPrefix}{orderId}-{suffix}";
    }

    private static bool IsEShopReference(string? customerReference) =>
        customerReference is not null &&
        customerReference.StartsWith(InvoicingConstants.MerchantReferencePrefix, StringComparison.Ordinal);

    private static string? BuyerIdFromReference(string? customerReference) =>
        IsEShopReference(customerReference)
            ? customerReference!.Substring(InvoicingConstants.MerchantReferencePrefix.Length)
            : null;
}
