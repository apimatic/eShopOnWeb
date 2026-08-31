using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates orders, eShop's own bill records, and the external invoicing provider, and enforces
/// shopper scoping. The amount a bill carries is always taken from the order, never from the caller.
/// </summary>
public class InvoicingService : IInvoicingService
{
    /// <summary>The provider account bills in USD; every bill uses it.</summary>
    private const string BillingCurrency = "USD";

    // eShopOnWeb's POST /api/orders carries only items and quantities, so — as the sample checkout
    // does — a fixed shipping address stands in for the order the bill is raised against.
    private static readonly Address DefaultShipToAddress =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private const string DefaultPictureUri = "eCatalog-item-default.png";

    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _catalogRepository;
    private readonly IRepository<Invoice> _invoiceRepository;
    private readonly IInvoiceProvider _provider;
    private readonly IAppLogger<InvoicingService> _logger;

    public InvoicingService(
        IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> catalogRepository,
        IRepository<Invoice> invoiceRepository,
        IInvoiceProvider provider,
        IAppLogger<InvoicingService> logger)
    {
        _orderRepository = orderRepository;
        _catalogRepository = catalogRepository;
        _invoiceRepository = invoiceRepository;
        _provider = provider;
        _logger = logger;
    }

    public async Task<OperationResult<PlacedOrderResult>> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, CancellationToken cancellationToken = default)
    {
        if (lines is null || lines.Count == 0)
        {
            return OperationResult<PlacedOrderResult>.Invalid("At least one order line is required.");
        }

        // Merge duplicate item lines and validate quantities up front.
        var merged = new Dictionary<int, int>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                return OperationResult<PlacedOrderResult>.Invalid(
                    $"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }
            merged[line.CatalogItemId] = merged.TryGetValue(line.CatalogItemId, out var q) ? q + line.Quantity : line.Quantity;
        }

        var items = new List<OrderItem>();
        foreach (var (catalogItemId, quantity) in merged)
        {
            var catalogItem = await _catalogRepository.GetByIdAsync(catalogItemId, cancellationToken);
            if (catalogItem is null)
            {
                return OperationResult<PlacedOrderResult>.Invalid(
                    $"Catalog item {catalogItemId} does not exist.");
            }

            var pictureUri = string.IsNullOrEmpty(catalogItem.PictureUri) ? DefaultPictureUri : catalogItem.PictureUri;
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, quantity));
        }

        var order = new Order(buyerId, DefaultShipToAddress, items);
        await _orderRepository.AddAsync(order, cancellationToken);

        _logger.LogInformation("Placed order {0} for buyer with {1} line(s).", order.Id, items.Count);
        return OperationResult<PlacedOrderResult>.Ok(
            new PlacedOrderResult(order.Id, order.Total(), BillingCurrency, items.Count));
    }

    public async Task<OperationResult<RaisedInvoiceResult>> RaiseInvoiceAsync(int orderId, DateOnly dueDate, CallerContext caller, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || !caller.CanAccess(order.BuyerId))
        {
            // Hide existence of another shopper's order.
            return OperationResult<RaisedInvoiceResult>.NotFound($"Order {orderId} was not found.");
        }

        if (dueDate < DateOnly.FromDateTime(DateTime.UtcNow.Date))
        {
            return OperationResult<RaisedInvoiceResult>.Invalid("The due date cannot be in the past.");
        }

        if (order.OrderItems.Count == 0)
        {
            return OperationResult<RaisedInvoiceResult>.Invalid($"Order {orderId} has no items to bill.");
        }

        var existing = await _invoiceRepository.FirstOrDefaultAsync(new ActiveInvoiceForOrderSpecification(orderId), cancellationToken);
        if (existing is not null)
        {
            return OperationResult<RaisedInvoiceResult>.Conflict(
                $"A bill has already been raised for order {orderId} (invoice {existing.ProviderInvoiceId}).");
        }

        var amount = order.Total();
        var lineItems = order.OrderItems
            .Select(oi => new ProviderLineItem(
                oi.ItemOrdered.ProductName,
                oi.ItemOrdered.CatalogItemId.ToString(CultureInfo.InvariantCulture),
                oi.Units,
                oi.UnitPrice))
            .ToList();

        var customerName = order.BuyerId;
        var customerEmail = ToEmail(order.BuyerId);
        var request = new RaiseInvoiceRequest(
            InvoiceReference: $"eShopOrder{orderId}",
            Description: $"eShopOnWeb order {orderId}",
            Amount: amount,
            Currency: BillingCurrency,
            DueDate: dueDate,
            CustomerName: customerName,
            CustomerEmail: customerEmail,
            LineItems: lineItems);

        ProviderInvoice raised;
        try
        {
            raised = await _provider.RaiseAsync(request, cancellationToken);
        }
        catch (InvoiceProviderException ex)
        {
            return FromProviderException<RaisedInvoiceResult>(ex);
        }

        var invoice = new Invoice(orderId, order.BuyerId, raised.Id, amount, BillingCurrency, dueDate, customerName, customerEmail);
        await _invoiceRepository.AddAsync(invoice, cancellationToken);

        _logger.LogInformation("Raised invoice {0} against order {1} (provider status {2}).", raised.Id, orderId, raised.Status ?? "unknown");
        return OperationResult<RaisedInvoiceResult>.Ok(
            new RaisedInvoiceResult(raised.Id, orderId, invoice.Status.ToString(), amount, BillingCurrency, dueDate));
    }

    public async Task<OperationResult<InvoiceDetailsResult>> GetInvoiceAsync(string invoiceId, CallerContext caller, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceRepository.FirstOrDefaultAsync(new InvoiceByProviderIdSpecification(invoiceId), cancellationToken);
        if (invoice is null || !caller.CanAccess(invoice.BuyerId))
        {
            return OperationResult<InvoiceDetailsResult>.NotFound($"Invoice {invoiceId} was not found.");
        }

        try
        {
            var details = await BuildDetailsAsync(invoice, cancellationToken);
            return OperationResult<InvoiceDetailsResult>.Ok(details);
        }
        catch (InvoiceProviderException ex)
        {
            return FromProviderException<InvoiceDetailsResult>(ex);
        }
    }

    public async Task<OperationResult<InvoiceDetailsResult>> CorrectInvoiceAsync(string invoiceId, InvoiceCorrection correction, CallerContext caller, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceRepository.FirstOrDefaultAsync(new InvoiceByProviderIdSpecification(invoiceId), cancellationToken);
        if (invoice is null || !caller.CanAccess(invoice.BuyerId))
        {
            return OperationResult<InvoiceDetailsResult>.NotFound($"Invoice {invoiceId} was not found.");
        }

        if (invoice.Status != InvoiceStatus.Raised)
        {
            var why = invoice.Status == InvoiceStatus.Issued ? "put to the shopper" : "withdrawn";
            return OperationResult<InvoiceDetailsResult>.Conflict(
                $"Invoice {invoiceId} cannot be corrected because it has been {why}.");
        }

        var newDueDate = correction.DueDate ?? invoice.DueDate;
        var newName = string.IsNullOrWhiteSpace(correction.CustomerName) ? invoice.CustomerName : correction.CustomerName.Trim();
        var newEmail = string.IsNullOrWhiteSpace(correction.CustomerEmail) ? invoice.CustomerEmail : correction.CustomerEmail.Trim();

        if (newDueDate < DateOnly.FromDateTime(DateTime.UtcNow.Date))
        {
            return OperationResult<InvoiceDetailsResult>.Invalid("The due date cannot be in the past.");
        }
        if (!IsValidEmail(newEmail))
        {
            return OperationResult<InvoiceDetailsResult>.Invalid("The customer email is not a valid email address.");
        }

        // The billed amount is not correctable: re-derive it (and the line items) from the order.
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(invoice.OrderId), cancellationToken);
        if (order is null)
        {
            return OperationResult<InvoiceDetailsResult>.Error($"The order {invoice.OrderId} backing invoice {invoiceId} could not be found.");
        }

        var lineItems = order.OrderItems
            .Select(oi => new ProviderLineItem(
                oi.ItemOrdered.ProductName,
                oi.ItemOrdered.CatalogItemId.ToString(CultureInfo.InvariantCulture),
                oi.Units,
                oi.UnitPrice))
            .ToList();

        var request = new UpdateInvoiceRequest(
            Description: $"eShopOnWeb order {invoice.OrderId}",
            Amount: invoice.Amount,
            Currency: BillingCurrency,
            DueDate: newDueDate,
            CustomerName: newName,
            CustomerEmail: newEmail,
            LineItems: lineItems);

        try
        {
            await _provider.UpdateAsync(invoiceId, request, cancellationToken);
        }
        catch (InvoiceProviderException ex)
        {
            return FromProviderException<InvoiceDetailsResult>(ex);
        }

        invoice.Correct(newDueDate, newName, newEmail);
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

        _logger.LogInformation("Corrected invoice {0}.", invoiceId);
        try
        {
            var details = await BuildDetailsAsync(invoice, cancellationToken);
            return OperationResult<InvoiceDetailsResult>.Ok(details);
        }
        catch (InvoiceProviderException ex)
        {
            return FromProviderException<InvoiceDetailsResult>(ex);
        }
    }

    public async Task<OperationResult<InvoiceDetailsResult>> IssueInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceRepository.FirstOrDefaultAsync(new InvoiceByProviderIdSpecification(invoiceId), cancellationToken);
        if (invoice is null)
        {
            return OperationResult<InvoiceDetailsResult>.NotFound($"Invoice {invoiceId} was not found.");
        }

        if (invoice.HasBeenWithdrawn)
        {
            return OperationResult<InvoiceDetailsResult>.Conflict($"Invoice {invoiceId} has been withdrawn and cannot be put to the shopper.");
        }
        if (invoice.HasBeenIssued)
        {
            return OperationResult<InvoiceDetailsResult>.Conflict($"Invoice {invoiceId} has already been put to the shopper.");
        }

        try
        {
            await _provider.IssueAsync(invoiceId, cancellationToken);
        }
        catch (InvoiceProviderException ex)
        {
            return FromProviderException<InvoiceDetailsResult>(ex);
        }

        invoice.MarkIssued();
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

        _logger.LogInformation("Issued invoice {0} to the shopper.", invoiceId);
        try
        {
            var details = await BuildDetailsAsync(invoice, cancellationToken);
            return OperationResult<InvoiceDetailsResult>.Ok(details);
        }
        catch (InvoiceProviderException ex)
        {
            return FromProviderException<InvoiceDetailsResult>(ex);
        }
    }

    public async Task<OperationResult<InvoiceDetailsResult>> WithdrawInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceRepository.FirstOrDefaultAsync(new InvoiceByProviderIdSpecification(invoiceId), cancellationToken);
        if (invoice is null)
        {
            return OperationResult<InvoiceDetailsResult>.NotFound($"Invoice {invoiceId} was not found.");
        }

        if (invoice.HasBeenWithdrawn)
        {
            return OperationResult<InvoiceDetailsResult>.Conflict($"Invoice {invoiceId} has already been withdrawn.");
        }

        try
        {
            await _provider.WithdrawAsync(invoiceId, cancellationToken);
        }
        catch (InvoiceProviderException ex)
        {
            return FromProviderException<InvoiceDetailsResult>(ex);
        }

        invoice.MarkWithdrawn();
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

        _logger.LogInformation("Withdrew invoice {0}.", invoiceId);
        try
        {
            var details = await BuildDetailsAsync(invoice, cancellationToken);
            return OperationResult<InvoiceDetailsResult>.Ok(details);
        }
        catch (InvoiceProviderException ex)
        {
            return FromProviderException<InvoiceDetailsResult>(ex);
        }
    }

    public async Task<OperationResult<IReadOnlyList<InvoiceSummaryResult>>> GetInvoicesForShopperAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var invoices = await _invoiceRepository.ListAsync(new InvoicesByBuyerSpecification(buyerId), cancellationToken);
        IReadOnlyList<InvoiceSummaryResult> summaries = invoices
            .Select(i => new InvoiceSummaryResult(
                i.ProviderInvoiceId, i.OrderId, i.Status.ToString(), i.Amount, i.Currency, i.DueDate, i.RaisedAt))
            .ToList();
        return OperationResult<IReadOnlyList<InvoiceSummaryResult>>.Ok(summaries);
    }

    public async Task<OperationResult<ReconciliationReport>> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (from > to)
        {
            return OperationResult<ReconciliationReport>.Invalid("'from' must be earlier than or equal to 'to'.");
        }

        IReadOnlyList<ProviderInvoiceSummary> providerInvoices;
        try
        {
            providerInvoices = await _provider.ListCreatedBetweenAsync(from, to, cancellationToken);
        }
        catch (InvoiceProviderException ex)
        {
            return FromProviderException<ReconciliationReport>(ex);
        }

        var eShopInvoices = await _invoiceRepository.ListAsync(new InvoicesRaisedBetweenSpecification(from, to), cancellationToken);
        var eShopByProviderId = eShopInvoices
            .GroupBy(i => i.ProviderInvoiceId)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var providerIds = new HashSet<string>(providerInvoices.Select(p => p.Id), StringComparer.Ordinal);

        var entries = new List<ReconciliationEntry>();

        foreach (var p in providerInvoices)
        {
            if (eShopByProviderId.TryGetValue(p.Id, out var local))
            {
                entries.Add(new ReconciliationEntry(
                    p.Id, ReconciliationSource.RecordedByBoth, p.Status, local.Status.ToString(),
                    local.OrderId, local.Amount, local.Currency, local.CustomerName, p.CreatedDate));
            }
            else
            {
                entries.Add(new ReconciliationEntry(
                    p.Id, ReconciliationSource.ProviderOnly, p.Status, null,
                    null, p.Amount, p.Currency, p.CustomerName, p.CreatedDate));
            }
        }

        foreach (var local in eShopInvoices)
        {
            if (!providerIds.Contains(local.ProviderInvoiceId))
            {
                entries.Add(new ReconciliationEntry(
                    local.ProviderInvoiceId, ReconciliationSource.EShopOnly, null, local.Status.ToString(),
                    local.OrderId, local.Amount, local.Currency, local.CustomerName, local.RaisedAt));
            }
        }

        var ordered = entries
            .OrderByDescending(e => e.CreatedDate ?? DateTimeOffset.MinValue)
            .ToList();

        var report = new ReconciliationReport(
            From: from,
            To: to,
            ProviderInvoiceCount: providerInvoices.Count,
            EShopInvoiceCount: eShopInvoices.Count,
            RecordedByBothCount: ordered.Count(e => e.Source == ReconciliationSource.RecordedByBoth),
            ProviderOnlyCount: ordered.Count(e => e.Source == ReconciliationSource.ProviderOnly),
            EShopOnlyCount: ordered.Count(e => e.Source == ReconciliationSource.EShopOnly),
            Entries: ordered);

        _logger.LogInformation(
            "Reconciled {0} provider invoice(s) against {1} eShop invoice(s) for range {2:o}..{3:o}.",
            providerInvoices.Count, eShopInvoices.Count, from, to);
        return OperationResult<ReconciliationReport>.Ok(report);
    }

    private async Task<InvoiceDetailsResult> BuildDetailsAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        var providerInvoice = await _provider.GetAsync(invoice.ProviderInvoiceId, cancellationToken);

        // The way to pay is only handed out once the bill has been put to the shopper, and never
        // after it has been withdrawn.
        var paymentLink = invoice.Status == InvoiceStatus.Issued ? providerInvoice.PaymentLink : null;

        var history = providerInvoice.History
            .Select(h => new InvoiceEvent(h.Event, h.Date))
            .ToList();

        return new InvoiceDetailsResult(
            InvoiceId: invoice.ProviderInvoiceId,
            OrderId: invoice.OrderId,
            Status: invoice.Status.ToString(),
            ProviderStatus: providerInvoice.Status,
            Amount: invoice.Amount,
            Currency: invoice.Currency,
            DueDate: invoice.DueDate,
            CustomerName: invoice.CustomerName,
            CustomerEmail: invoice.CustomerEmail,
            RaisedAt: invoice.RaisedAt,
            PaymentLink: paymentLink,
            History: history);
    }

    private static OperationResult<T> FromProviderException<T>(InvoiceProviderException ex)
    {
        if (ex.IsStateConflict)
        {
            return OperationResult<T>.Conflict(ex.Message);
        }
        return OperationResult<T>.Error(ex.Message);
    }

    private static string ToEmail(string buyerId)
    {
        if (IsValidEmail(buyerId))
        {
            return buyerId;
        }
        var local = new string(buyerId.Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrEmpty(local))
        {
            local = "customer";
        }
        return $"{local}@example.com";
    }

    private static bool IsValidEmail(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        var at = value.IndexOf('@');
        return at > 0 && at < value.Length - 1 && value.IndexOf('@', at + 1) < 0 && value.IndexOf(' ') < 0;
    }
}
