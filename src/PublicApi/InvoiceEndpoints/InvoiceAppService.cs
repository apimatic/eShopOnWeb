using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Invoicing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class InvoiceAppService : IInvoiceAppService
{
    private const string DateFormat = "yyyy-MM-dd";

    private readonly IRepository<Order> _orders;
    private readonly IReadRepository<CatalogItem> _catalog;
    private readonly IRepository<Invoice> _invoices;
    private readonly IInvoicingService _invoicing;
    private readonly VisaOptions _visaOptions;
    private readonly ILogger<InvoiceAppService> _logger;

    public InvoiceAppService(
        IRepository<Order> orders,
        IReadRepository<CatalogItem> catalog,
        IRepository<Invoice> invoices,
        IInvoicingService invoicing,
        IOptions<VisaOptions> visaOptions,
        ILogger<InvoiceAppService> logger)
    {
        _orders = orders;
        _catalog = catalog;
        _invoices = invoices;
        _invoicing = invoicing;
        _visaOptions = visaOptions.Value;
        _logger = logger;
    }

    private string Currency => string.IsNullOrWhiteSpace(_visaOptions.Currency) ? "USD" : _visaOptions.Currency;

    public async Task<OperationResult<CreateOrderResponse>> PlaceOrderAsync(string buyerId, CreateOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            return OperationResult<CreateOrderResponse>.BadRequest("An order must contain at least one item.");
        }

        var orderItems = new List<OrderItem>();
        foreach (var line in request.Items)
        {
            if (line.Quantity <= 0)
            {
                return OperationResult<CreateOrderResponse>.BadRequest($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }

            var catalogItem = await _catalog.GetByIdAsync(line.CatalogItemId, cancellationToken);
            if (catalogItem is null)
            {
                return OperationResult<CreateOrderResponse>.BadRequest($"Catalog item {line.CatalogItemId} does not exist.");
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        // Invoicing bills for the order; shipping is not relevant, so a placeholder billing
        // address satisfies the order aggregate's required ship-to address.
        var address = new Address("Digital order - no shipping", "N/A", "N/A", "US", "00000");
        var order = new Order(buyerId, address, orderItems);
        order = await _orders.AddAsync(order, cancellationToken);

        var response = new CreateOrderResponse
        {
            OrderId = order.Id,
            Total = order.Total(),
            OrderDate = order.OrderDate,
            Items = order.OrderItems.Select(oi => new OrderLineDto
            {
                CatalogItemId = oi.ItemOrdered.CatalogItemId,
                ProductName = oi.ItemOrdered.ProductName,
                Units = oi.Units,
                UnitPrice = oi.UnitPrice
            }).ToList()
        };

        return OperationResult<CreateOrderResponse>.Ok(response);
    }

    public async Task<OperationResult<InvoiceDto>> RaiseInvoiceAsync(string buyerId, RaiseInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        if (!TryParseDate(request.DueDate, out var dueDate))
        {
            return OperationResult<InvoiceDto>.BadRequest($"dueDate must be a calendar date in {DateFormat} format.");
        }

        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(request.OrderId), cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            // Do not reveal the existence of another shopper's order.
            return OperationResult<InvoiceDto>.NotFound($"Order {request.OrderId} was not found.");
        }

        var existing = await _invoices.FirstOrDefaultAsync(new InvoiceByOrderIdSpecification(order.Id), cancellationToken);
        if (existing is not null)
        {
            return OperationResult<InvoiceDto>.Conflict(
                $"A bill has already been raised for order {order.Id} (invoice {existing.ProviderInvoiceId}).");
        }

        var amount = order.Total();
        var customerName = buyerId;
        var customerEmail = buyerId;

        var providerRequest = new NewInvoiceRequest
        {
            OrderReference = ToOrderReference(order.Id),
            Description = $"eShopOnWeb order {order.Id}",
            DueDate = dueDate,
            Currency = Currency,
            TotalAmount = amount,
            CustomerName = customerName,
            CustomerEmail = customerEmail,
            CustomerId = buyerId,
            LineItems = order.OrderItems.Select(oi => new ProviderLineItem(
                oi.ItemOrdered.ProductName,
                oi.ItemOrdered.CatalogItemId.ToString(CultureInfo.InvariantCulture),
                oi.Units,
                oi.UnitPrice,
                oi.UnitPrice * oi.Units)).ToList()
        };

        ProviderInvoiceSnapshot snapshot;
        try
        {
            snapshot = await _invoicing.CreateInvoiceAsync(providerRequest, cancellationToken);
        }
        catch (InvoicingProviderException ex)
        {
            return FromProviderException<InvoiceDto>(ex);
        }

        var invoice = new Invoice(
            orderId: order.Id,
            buyerId: buyerId,
            providerInvoiceId: snapshot.Id,
            amount: amount,
            currency: Currency,
            dueDate: dueDate,
            customerName: customerName,
            customerEmail: customerEmail,
            status: snapshot.Status);

        invoice = await _invoices.AddAsync(invoice, cancellationToken);
        _logger.LogInformation("Raised bill {InvoiceId} for order {OrderId} (status {Status}).",
            invoice.ProviderInvoiceId, order.Id, invoice.Status);

        return OperationResult<InvoiceDto>.Ok(MapToDto(invoice, snapshot));
    }

    public async Task<OperationResult<InvoiceDto>> GetInvoiceAsync(string buyerId, string invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await FindOwnedAsync(invoiceId, buyerId, cancellationToken);
        if (invoice is null)
        {
            return OperationResult<InvoiceDto>.NotFound($"Invoice {invoiceId} was not found.");
        }

        ProviderInvoiceSnapshot snapshot;
        try
        {
            snapshot = await _invoicing.GetInvoiceAsync(invoice.ProviderInvoiceId, cancellationToken);
        }
        catch (InvoicingProviderException ex)
        {
            return FromProviderException<InvoiceDto>(ex);
        }

        await SyncStatusAsync(invoice, snapshot.Status, cancellationToken);
        return OperationResult<InvoiceDto>.Ok(MapToDto(invoice, snapshot));
    }

    public async Task<OperationResult<InvoiceDto>> CorrectInvoiceAsync(string buyerId, CorrectInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        var invoice = await FindOwnedAsync(request.InvoiceId, buyerId, cancellationToken);
        if (invoice is null)
        {
            return OperationResult<InvoiceDto>.NotFound($"Invoice {request.InvoiceId} was not found.");
        }

        var dueDate = invoice.DueDate;
        if (request.DueDate is not null)
        {
            if (!TryParseDate(request.DueDate, out dueDate))
            {
                return OperationResult<InvoiceDto>.BadRequest($"dueDate must be a calendar date in {DateFormat} format.");
            }
        }

        // Refresh the authoritative state before deciding whether the bill can still be corrected.
        ProviderInvoiceSnapshot current;
        try
        {
            current = await _invoicing.GetInvoiceAsync(invoice.ProviderInvoiceId, cancellationToken);
        }
        catch (InvoicingProviderException ex)
        {
            return FromProviderException<InvoiceDto>(ex);
        }

        await SyncStatusAsync(invoice, current.Status, cancellationToken);

        if (!invoice.IsCorrectable)
        {
            var reason = invoice.IsWithdrawn ? "it has been withdrawn" : "it has been put to the shopper";
            return OperationResult<InvoiceDto>.Conflict(
                $"Invoice {invoice.ProviderInvoiceId} can no longer be corrected because {reason}.");
        }

        var customerName = request.CustomerName ?? invoice.CustomerName;
        var customerEmail = request.CustomerEmail ?? invoice.CustomerEmail;

        // What is billed still comes from the order — re-send the order's amount and line items
        // unchanged; only the due date and customer details are being corrected.
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(invoice.OrderId), cancellationToken);
        var lineItems = order?.OrderItems.Select(oi => new ProviderLineItem(
            oi.ItemOrdered.ProductName,
            oi.ItemOrdered.CatalogItemId.ToString(CultureInfo.InvariantCulture),
            oi.Units,
            oi.UnitPrice,
            oi.UnitPrice * oi.Units)).ToList() ?? new List<ProviderLineItem>();

        var correction = new InvoiceCorrection
        {
            OrderReference = ToOrderReference(invoice.OrderId),
            Description = $"eShopOnWeb order {invoice.OrderId}",
            DueDate = dueDate,
            Currency = invoice.Currency,
            TotalAmount = invoice.Amount,
            CustomerName = customerName,
            CustomerEmail = customerEmail,
            CustomerId = invoice.BuyerId,
            LineItems = lineItems
        };

        ProviderInvoiceSnapshot snapshot;
        try
        {
            snapshot = await _invoicing.UpdateInvoiceAsync(invoice.ProviderInvoiceId, correction, cancellationToken);
        }
        catch (InvoicingProviderException ex)
        {
            return FromProviderException<InvoiceDto>(ex);
        }

        invoice.ApplyCorrection(dueDate, customerName, customerEmail);
        invoice.SyncStatus(snapshot.Status);
        await _invoices.UpdateAsync(invoice, cancellationToken);

        return OperationResult<InvoiceDto>.Ok(MapToDto(invoice, snapshot));
    }

    public async Task<OperationResult<InvoiceDto>> IssueInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoices.FirstOrDefaultAsync(new InvoiceByProviderIdSpecification(invoiceId), cancellationToken);
        if (invoice is null)
        {
            return OperationResult<InvoiceDto>.NotFound($"Invoice {invoiceId} was not found.");
        }

        if (invoice.IsWithdrawn)
        {
            return OperationResult<InvoiceDto>.Conflict($"Invoice {invoiceId} has been withdrawn and cannot be issued.");
        }

        ProviderInvoiceSnapshot snapshot;
        try
        {
            snapshot = await _invoicing.SendInvoiceAsync(invoice.ProviderInvoiceId, cancellationToken);
        }
        catch (InvoicingProviderException ex)
        {
            return FromProviderException<InvoiceDto>(ex);
        }

        invoice.SyncStatus(snapshot.Status);
        await _invoices.UpdateAsync(invoice, cancellationToken);
        _logger.LogInformation("Issued bill {InvoiceId} (status {Status}).", invoice.ProviderInvoiceId, invoice.Status);

        return OperationResult<InvoiceDto>.Ok(MapToDto(invoice, snapshot));
    }

    public async Task<OperationResult<InvoiceDto>> WithdrawInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoices.FirstOrDefaultAsync(new InvoiceByProviderIdSpecification(invoiceId), cancellationToken);
        if (invoice is null)
        {
            return OperationResult<InvoiceDto>.NotFound($"Invoice {invoiceId} was not found.");
        }

        if (invoice.IsWithdrawn)
        {
            // Already withdrawn — the goal (no longer payable) is already met.
            var already = await _invoicing.GetInvoiceAsync(invoice.ProviderInvoiceId, cancellationToken);
            return OperationResult<InvoiceDto>.Ok(MapToDto(invoice, already));
        }

        ProviderInvoiceSnapshot snapshot;
        try
        {
            snapshot = await _invoicing.CancelInvoiceAsync(invoice.ProviderInvoiceId, cancellationToken);
        }
        catch (InvoicingProviderException ex)
        {
            return FromProviderException<InvoiceDto>(ex);
        }

        invoice.SyncStatus(snapshot.Status);
        await _invoices.UpdateAsync(invoice, cancellationToken);
        _logger.LogInformation("Withdrew bill {InvoiceId} (status {Status}).", invoice.ProviderInvoiceId, invoice.Status);

        return OperationResult<InvoiceDto>.Ok(MapToDto(invoice, snapshot));
    }

    public async Task<IReadOnlyList<InvoiceSummaryDto>> GetMyInvoicesAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var invoices = await _invoices.ListAsync(new CustomerInvoicesSpecification(buyerId), cancellationToken);
        return invoices.Select(MapToSummary).ToList();
    }

    public async Task<OperationResult<ReconciliationReportDto>> ReconcileAsync(string from, string to, CancellationToken cancellationToken = default)
    {
        if (!TryParseInstant(from, out var fromInstant) || !TryParseInstant(to, out var toInstant))
        {
            return OperationResult<ReconciliationReportDto>.BadRequest("from and to must be ISO-8601 date-times.");
        }
        if (fromInstant > toInstant)
        {
            return OperationResult<ReconciliationReportDto>.BadRequest("from must not be after to.");
        }

        IReadOnlyList<ProviderInvoiceListItem> providerInvoices;
        try
        {
            providerInvoices = await _invoicing.ListInvoicesAsync(fromInstant, toInstant, cancellationToken);
        }
        catch (InvoicingProviderException ex)
        {
            return FromProviderException<ReconciliationReportDto>(ex);
        }

        // Everything eShop believes it raised (this run's records).
        var localInvoices = await _invoices.ListAsync(cancellationToken);
        var localByProviderId = localInvoices
            .Where(i => !string.IsNullOrEmpty(i.ProviderInvoiceId))
            .GroupBy(i => i.ProviderInvoiceId)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var report = new ReconciliationReportDto { From = fromInstant, To = toInstant };
        var providerIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in providerInvoices)
        {
            providerIds.Add(item.Id);
            var recognized = localByProviderId.TryGetValue(item.Id, out var local);

            report.ProviderInvoices.Add(new ReconciliationEntryDto
            {
                InvoiceId = item.Id,
                Status = item.Status,
                CreatedDate = item.CreatedDate,
                Amount = item.Amount,
                Currency = item.Currency,
                CustomerName = item.CustomerName,
                RecognizedByEShop = recognized,
                OrderId = recognized ? local!.OrderId : (int?)null,
                Source = recognized ? "eShop" : "external"
            });
        }

        // Bills eShop raised in the range that the provider has no record of.
        foreach (var local in localInvoices)
        {
            if (local.CreatedAt < fromInstant || local.CreatedAt > toInstant)
            {
                continue;
            }
            if (providerIds.Contains(local.ProviderInvoiceId))
            {
                continue;
            }

            report.MissingAtProvider.Add(new ReconciliationEntryDto
            {
                InvoiceId = local.ProviderInvoiceId,
                Status = local.Status,
                CreatedDate = local.CreatedAt,
                Amount = local.Amount,
                Currency = local.Currency,
                CustomerName = local.CustomerName,
                RecognizedByEShop = true,
                OrderId = local.OrderId,
                Source = "eShop (missing at provider)"
            });
        }

        report.ProviderInvoiceCount = report.ProviderInvoices.Count;
        report.RecognizedByEShopCount = report.ProviderInvoices.Count(e => e.RecognizedByEShop);
        report.ExternalCount = report.ProviderInvoices.Count(e => !e.RecognizedByEShop);
        report.MissingAtProviderCount = report.MissingAtProvider.Count;

        return OperationResult<ReconciliationReportDto>.Ok(report);
    }

    // ---- helpers -------------------------------------------------------------------------

    private async Task<Invoice?> FindOwnedAsync(string invoiceId, string buyerId, CancellationToken cancellationToken)
    {
        var invoice = await _invoices.FirstOrDefaultAsync(new InvoiceByProviderIdSpecification(invoiceId), cancellationToken);
        if (invoice is null || !string.Equals(invoice.BuyerId, buyerId, StringComparison.Ordinal))
        {
            return null; // not found, or belongs to another shopper — do not distinguish
        }
        return invoice;
    }

    private async Task SyncStatusAsync(Invoice invoice, string status, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(invoice.Status, status, StringComparison.OrdinalIgnoreCase))
        {
            invoice.SyncStatus(status);
            await _invoices.UpdateAsync(invoice, cancellationToken);
        }
    }

    private InvoiceDto MapToDto(Invoice invoice, ProviderInvoiceSnapshot snapshot)
    {
        invoice.SyncStatus(snapshot.Status);

        // Hand out a way to pay only once the bill has been put to the shopper and not withdrawn.
        var paymentLink = invoice.IsIssued && !invoice.IsWithdrawn ? snapshot.PaymentLink : null;

        return new InvoiceDto
        {
            InvoiceId = invoice.ProviderInvoiceId,
            OrderId = invoice.OrderId,
            Status = invoice.Status,
            Issued = invoice.IsIssued,
            Withdrawn = invoice.IsWithdrawn,
            Amount = invoice.Amount,
            Currency = invoice.Currency,
            DueDate = invoice.DueDate.ToString(DateFormat, CultureInfo.InvariantCulture),
            CustomerName = invoice.CustomerName,
            CustomerEmail = invoice.CustomerEmail,
            PaymentLink = paymentLink,
            History = snapshot.History.Select(h => new InvoiceHistoryDto { Event = h.Event, Date = h.Date }).ToList(),
            ProviderSubmitTimeUtc = snapshot.SubmitTimeUtc
        };
    }

    private static InvoiceSummaryDto MapToSummary(Invoice invoice) => new()
    {
        InvoiceId = invoice.ProviderInvoiceId,
        OrderId = invoice.OrderId,
        Status = invoice.Status,
        Issued = invoice.IsIssued,
        Withdrawn = invoice.IsWithdrawn,
        Amount = invoice.Amount,
        Currency = invoice.Currency,
        DueDate = invoice.DueDate.ToString(DateFormat, CultureInfo.InvariantCulture),
        CreatedAt = invoice.CreatedAt
    };

    private static OperationResult<T> FromProviderException<T>(InvoicingProviderException ex) =>
        ex.IsRefusal
            ? OperationResult<T>.Conflict(ex.Message)
            : OperationResult<T>.ProviderError(ex.Message);

    private static string ToOrderReference(int orderId) => $"ORDER{orderId}";

    private static bool TryParseDate(string? value, out DateTime date) =>
        DateTime.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static bool TryParseInstant(string? value, out DateTimeOffset instant) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out instant);
}
