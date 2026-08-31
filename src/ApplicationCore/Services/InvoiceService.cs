using System;
using System.Collections.Generic;
using System.Globalization;
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
    // The provider account bills in USD; eShopOnWeb prices its catalog without recording a currency.
    private const string Currency = "USD";

    private readonly IRepository<Invoice> _invoiceRepository;
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IInvoiceProvider _invoiceProvider;

    public InvoiceService(
        IRepository<Invoice> invoiceRepository,
        IReadRepository<Order> orderRepository,
        IInvoiceProvider invoiceProvider)
    {
        _invoiceRepository = invoiceRepository;
        _orderRepository = orderRepository;
        _invoiceProvider = invoiceProvider;
    }

    public async Task<Invoice> RaiseInvoiceAsync(int orderId, string buyerId, DateOnly dueDate, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);

        // A bill belongs to the shopper whose order it was raised against; another shopper's order is
        // reported as simply not found.
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new OrderNotFoundException(orderId);
        }

        if (!order.OrderItems.Any())
        {
            throw new InvalidOrderRequestException($"Order {orderId} has no items to bill.");
        }

        var lineItems = BuildLineItems(order);
        var total = order.Total();
        var (customerName, customerEmail) = DefaultCustomer(buyerId);
        var invoiceNumber = BuildInvoiceNumber(orderId);

        var request = new CreateProviderInvoiceRequest
        {
            InvoiceNumber = invoiceNumber,
            Description = $"eShopOnWeb order {orderId}",
            DueDate = dueDate,
            TotalAmount = total,
            Currency = Currency,
            CustomerName = customerName,
            CustomerEmail = customerEmail,
            LineItems = lineItems
        };

        var providerInvoice = await _invoiceProvider.CreateInvoiceAsync(request, cancellationToken);

        var invoice = new Invoice(
            orderId,
            buyerId,
            providerInvoice.Id,
            invoiceNumber,
            total,
            Currency,
            dueDate,
            customerName,
            customerEmail,
            providerInvoice.CreatedDate ?? DateTimeOffset.UtcNow,
            string.IsNullOrEmpty(providerInvoice.Status) ? "DRAFT" : providerInvoice.Status);

        await _invoiceRepository.AddAsync(invoice, cancellationToken);
        return invoice;
    }

    public async Task<InvoiceDetail> GetInvoiceAsync(string invoiceId, string buyerId, bool isOperator, CancellationToken cancellationToken = default)
    {
        var invoice = await FindOwnedInvoiceOrNullAsync(invoiceId, buyerId, isOperator, cancellationToken);

        if (invoice is null)
        {
            // Only an operator reaches here (a shopper would already have been refused): read a bill
            // eShop did not raise straight from the provider.
            var providerOnly = await _invoiceProvider.GetInvoiceAsync(invoiceId, cancellationToken);
            return new InvoiceDetail(null, providerOnly);
        }

        var provider = await _invoiceProvider.GetInvoiceAsync(invoiceId, cancellationToken);
        invoice.SyncProviderState(provider.Status, provider.PaymentLink);
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

        return new InvoiceDetail(invoice, provider);
    }

    public async Task<Invoice> CorrectInvoiceAsync(string invoiceId, string buyerId, DateOnly? dueDate, string? customerName, string? customerEmail, CancellationToken cancellationToken = default)
    {
        var invoice = await FindOwnedInvoiceOrNullAsync(invoiceId, buyerId, isOperator: false, cancellationToken)
            ?? throw new InvoiceNotFoundException(invoiceId);

        if (!invoice.CanBeCorrected)
        {
            var reason = invoice.LifecycleState == InvoiceLifecycleState.Issued
                ? "it has already been put to the shopper"
                : "it has been withdrawn";
            throw new InvoiceStateException($"Invoice {invoiceId} cannot be corrected because {reason}.");
        }

        var newDueDate = dueDate ?? invoice.DueDate;
        var newName = string.IsNullOrWhiteSpace(customerName) ? invoice.CustomerName : customerName.Trim();
        var newEmail = string.IsNullOrWhiteSpace(customerEmail) ? invoice.CustomerEmail : customerEmail.Trim();

        // What is billed still comes from the order, so the amount is re-derived rather than accepted
        // from the caller.
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(invoice.OrderId), cancellationToken)
            ?? throw new OrderNotFoundException(invoice.OrderId);

        var request = new UpdateProviderInvoiceRequest
        {
            Description = $"eShopOnWeb order {invoice.OrderId}",
            DueDate = newDueDate,
            TotalAmount = order.Total(),
            Currency = invoice.Currency,
            CustomerName = newName,
            CustomerEmail = newEmail,
            LineItems = BuildLineItems(order)
        };

        await _invoiceProvider.UpdateInvoiceAsync(invoiceId, request, cancellationToken);

        invoice.ApplyCorrections(newDueDate, newName, newEmail);
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
        return invoice;
    }

    public async Task<InvoiceDetail> IssueInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceRepository.FirstOrDefaultAsync(new InvoiceByProviderIdSpecification(invoiceId), cancellationToken)
            ?? throw new InvoiceNotFoundException(invoiceId);

        if (invoice.LifecycleState == InvoiceLifecycleState.Withdrawn)
        {
            throw new InvoiceStateException($"Invoice {invoiceId} has been withdrawn and cannot be put to the shopper.");
        }

        if (invoice.LifecycleState == InvoiceLifecycleState.Issued)
        {
            throw new InvoiceStateException($"Invoice {invoiceId} has already been put to the shopper.");
        }

        var provider = await _invoiceProvider.IssueInvoiceAsync(invoiceId, cancellationToken);
        invoice.MarkIssued(provider.PaymentLink, provider.Status);
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

        return new InvoiceDetail(invoice, provider);
    }

    public async Task<Invoice> WithdrawInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceRepository.FirstOrDefaultAsync(new InvoiceByProviderIdSpecification(invoiceId), cancellationToken)
            ?? throw new InvoiceNotFoundException(invoiceId);

        if (invoice.LifecycleState == InvoiceLifecycleState.Withdrawn)
        {
            throw new InvoiceStateException($"Invoice {invoiceId} has already been withdrawn.");
        }

        // The provider may legitimately refuse (e.g. a bill that has been paid). That refusal is
        // surfaced to the caller by the provider layer as an InvoiceProviderException.
        var provider = await _invoiceProvider.WithdrawInvoiceAsync(invoiceId, cancellationToken);
        invoice.MarkWithdrawn(provider.Status);
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
        return invoice;
    }

    public async Task<IReadOnlyList<Invoice>> GetMyInvoicesAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var invoices = await _invoiceRepository.ListAsync(new CustomerInvoicesSpecification(buyerId), cancellationToken);
        return invoices;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var providerSummaries = await _invoiceProvider.ListInvoicesCreatedBetweenAsync(from, to, cancellationToken);
        var localInvoices = await _invoiceRepository.ListAsync(new InvoicesCreatedBetweenSpecification(from, to), cancellationToken);

        var localByProviderId = localInvoices
            .GroupBy(i => i.ProviderInvoiceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var entries = new List<ReconciliationEntry>();
        var matchedLocalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var summary in providerSummaries)
        {
            localByProviderId.TryGetValue(summary.Id, out var local);
            if (local is not null)
            {
                matchedLocalIds.Add(local.ProviderInvoiceId);
            }

            entries.Add(new ReconciliationEntry
            {
                InvoiceId = summary.Id,
                Source = local is not null ? ReconciliationSource.Both : ReconciliationSource.ProviderOnly,
                ProviderStatus = summary.Status,
                EShopStatus = local?.LifecycleState.ToString(),
                CreatedDate = summary.CreatedDate ?? local?.CreatedDate,
                Amount = summary.TotalAmount ?? local?.TotalAmount,
                Currency = summary.Currency ?? local?.Currency,
                CustomerName = summary.CustomerName ?? local?.CustomerName,
                OrderId = local?.OrderId
            });
        }

        // Bills eShop believes it raised in range but the provider's record does not show — the reverse
        // mismatch — are surfaced too.
        foreach (var local in localInvoices)
        {
            if (matchedLocalIds.Contains(local.ProviderInvoiceId))
            {
                continue;
            }

            entries.Add(new ReconciliationEntry
            {
                InvoiceId = local.ProviderInvoiceId,
                Source = ReconciliationSource.EShopOnly,
                ProviderStatus = null,
                EShopStatus = local.LifecycleState.ToString(),
                CreatedDate = local.CreatedDate,
                Amount = local.TotalAmount,
                Currency = local.Currency,
                CustomerName = local.CustomerName,
                OrderId = local.OrderId
            });
        }

        var ordered = entries
            .OrderByDescending(e => e.CreatedDate ?? DateTimeOffset.MinValue)
            .ToList();

        return new ReconciliationReport { From = from, To = to, Entries = ordered };
    }

    private async Task<Invoice?> FindOwnedInvoiceOrNullAsync(string invoiceId, string buyerId, bool isOperator, CancellationToken cancellationToken)
    {
        var invoice = await _invoiceRepository.FirstOrDefaultAsync(new InvoiceByProviderIdSpecification(invoiceId), cancellationToken);

        if (invoice is null)
        {
            if (isOperator)
            {
                return null;
            }

            throw new InvoiceNotFoundException(invoiceId);
        }

        // One shopper must never see or correct another's bill.
        if (!isOperator && !string.Equals(invoice.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new InvoiceNotFoundException(invoiceId);
        }

        return invoice;
    }

    private static IReadOnlyList<ProviderLineItem> BuildLineItems(Order order)
    {
        return order.OrderItems
            .Select(item => new ProviderLineItem(
                item.ItemOrdered.CatalogItemId.ToString(CultureInfo.InvariantCulture),
                item.ItemOrdered.ProductName,
                item.Units,
                item.UnitPrice))
            .ToList();
    }

    private static (string Name, string Email) DefaultCustomer(string buyerId)
    {
        // The seeded shoppers are identified by email (e.g. demouser@microsoft.com); use that as the
        // customer detail. If the identity is not an email, synthesize a placeholder so the provider —
        // which requires an email for an itemized invoice — accepts it. These are correctable later.
        var looksLikeEmail = buyerId.Contains('@') && !buyerId.StartsWith('@') && !buyerId.EndsWith('@');
        if (looksLikeEmail)
        {
            return (buyerId, buyerId);
        }

        var localPart = new string(buyerId.Where(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-').ToArray());
        if (string.IsNullOrEmpty(localPart))
        {
            localPart = "customer";
        }

        return (buyerId, $"{localPart}@example.com");
    }

    private static string BuildInvoiceNumber(int orderId)
    {
        // The provider uses this as the invoice id; it must be unique and at most 20 characters, and
        // is also passed as the (alphanumeric-only) transaction reference number.
        var candidate = $"E{orderId}{DateTime.UtcNow:MMddHHmmssfff}";
        return candidate.Length <= 20 ? candidate : candidate[..20];
    }
}
