using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class InvoiceService : IInvoiceService
{
    private readonly IRepository<Invoice> _invoiceRepository;
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IVisaInvoicingGateway _gateway;
    private readonly IAppLogger<InvoiceService> _logger;

    public InvoiceService(
        IRepository<Invoice> invoiceRepository,
        IReadRepository<Order> orderRepository,
        IVisaInvoicingGateway gateway,
        IAppLogger<InvoiceService> logger)
    {
        _invoiceRepository = invoiceRepository;
        _orderRepository = orderRepository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<OperationResult<InvoiceDetailView>> RaiseForOrderAsync(
        string buyerId, bool isOperator, int orderId, DateOnly dueDate, CustomerDetails? customerOverrides,
        CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            return OperationResult<InvoiceDetailView>.NotFound($"Order {orderId} was not found.");
        }

        // A bill can only be raised for an order the caller owns (operators may act on any order).
        if (!isOperator && !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            return OperationResult<InvoiceDetailView>.NotFound($"Order {orderId} was not found.");
        }

        var customerName = FirstNonEmpty(customerOverrides?.Name, order.BuyerId);
        var customerEmail = FirstNonEmpty(customerOverrides?.Email, DeriveEmail(order.BuyerId));
        var amount = order.Total();
        var invoiceNumber = GenerateInvoiceNumber(orderId);

        var draft = new GatewayInvoiceDraft(
            InvoiceNumber: invoiceNumber,
            Description: $"eShopOnWeb order {orderId}",
            DueDate: dueDate,
            CustomerName: customerName,
            CustomerEmail: customerEmail,
            Currency: InvoicingConstants.Currency,
            TotalAmount: amount,
            Lines: BuildLines(order));

        GatewayInvoice raised;
        try
        {
            raised = await _gateway.RaiseAsync(draft, cancellationToken);
        }
        catch (VisaInvoicingException ex)
        {
            _logger.LogWarning($"Raising a bill for order {orderId} was rejected by the provider: {ex.Message}");
            return ProviderFailure<InvoiceDetailView>(ex);
        }

        var invoice = new Invoice(
            orderId: order.Id,
            buyerId: order.BuyerId,
            providerInvoiceId: raised.Id,
            invoiceNumber: invoiceNumber,
            amount: amount,
            currency: InvoicingConstants.Currency,
            dueDate: dueDate,
            customerName: customerName,
            customerEmail: customerEmail,
            providerStatus: raised.Status ?? "DRAFT");

        await _invoiceRepository.AddAsync(invoice, cancellationToken);
        _logger.LogInformation($"Raised bill {invoice.ProviderInvoiceId} for order {orderId}.");

        return OperationResult<InvoiceDetailView>.Ok(BuildDetailView(invoice, raised));
    }

    public async Task<OperationResult<InvoiceDetailView>> GetAsync(
        string buyerId, bool isOperator, string invoiceId, CancellationToken cancellationToken = default)
    {
        var (invoice, notFound) = await LoadOwnedAsync(buyerId, isOperator, invoiceId, cancellationToken);
        if (notFound is not null)
        {
            return notFound;
        }

        GatewayInvoice provider;
        try
        {
            provider = await _gateway.GetAsync(invoice!.ProviderInvoiceId, cancellationToken);
        }
        catch (VisaInvoicingException ex)
        {
            _logger.LogWarning($"Reading bill {invoice!.ProviderInvoiceId} from the provider failed: {ex.Message}");
            return ProviderFailure<InvoiceDetailView>(ex);
        }

        await RefreshProviderStatusAsync(invoice!, provider.Status, cancellationToken);
        return OperationResult<InvoiceDetailView>.Ok(BuildDetailView(invoice!, provider));
    }

    public async Task<OperationResult<InvoiceDetailView>> CorrectAsync(
        string buyerId, bool isOperator, string invoiceId, DateOnly? dueDate, CustomerDetails? customerDetails,
        CancellationToken cancellationToken = default)
    {
        var (invoice, notFound) = await LoadOwnedAsync(buyerId, isOperator, invoiceId, cancellationToken);
        if (notFound is not null)
        {
            return notFound;
        }

        if (!invoice!.CanBeCorrected)
        {
            var reason = invoice.IsWithdrawn
                ? "This bill has been withdrawn and can no longer be corrected."
                : "This bill has already been put to the shopper and can no longer be corrected.";
            return OperationResult<InvoiceDetailView>.Conflict(reason);
        }

        var newDueDate = dueDate ?? invoice.DueDate;
        var newName = FirstNonEmpty(customerDetails?.Name, invoice.CustomerName);
        var newEmail = FirstNonEmpty(customerDetails?.Email, invoice.CustomerEmail);

        // What is billed still comes from the order, so the amount is re-derived, never taken from the caller.
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(invoice.OrderId), cancellationToken);
        var lines = order is not null ? BuildLines(order) : Array.Empty<GatewayInvoiceLine>();
        var amount = order?.Total() ?? invoice.Amount;

        var correction = new GatewayInvoiceCorrection(
            Description: $"eShopOnWeb order {invoice.OrderId}",
            DueDate: newDueDate,
            CustomerName: newName,
            CustomerEmail: newEmail,
            Currency: invoice.Currency,
            TotalAmount: amount,
            Lines: lines);

        GatewayInvoice corrected;
        try
        {
            corrected = await _gateway.CorrectAsync(invoice.ProviderInvoiceId, correction, cancellationToken);
        }
        catch (VisaInvoicingException ex)
        {
            _logger.LogWarning($"Correcting bill {invoice.ProviderInvoiceId} was rejected by the provider: {ex.Message}");
            return ProviderFailure<InvoiceDetailView>(ex);
        }

        invoice.ApplyCorrection(newDueDate, newName, newEmail);
        invoice.SetProviderStatus(corrected.Status ?? invoice.ProviderStatus);
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

        return OperationResult<InvoiceDetailView>.Ok(BuildDetailView(invoice, corrected));
    }

    public async Task<OperationResult<InvoiceDetailView>> IssueAsync(
        string buyerId, bool isOperator, string invoiceId, CancellationToken cancellationToken = default)
    {
        var (invoice, notFound) = await LoadOwnedAsync(buyerId, isOperator, invoiceId, cancellationToken);
        if (notFound is not null)
        {
            return notFound;
        }

        if (invoice!.IsWithdrawn)
        {
            return OperationResult<InvoiceDetailView>.Conflict("This bill has been withdrawn and can no longer be put to the shopper.");
        }

        if (invoice.IsIssued)
        {
            return OperationResult<InvoiceDetailView>.Conflict("This bill has already been put to the shopper.");
        }

        GatewayInvoice issued;
        try
        {
            issued = await _gateway.IssueAsync(invoice.ProviderInvoiceId, cancellationToken);
        }
        catch (VisaInvoicingException ex)
        {
            _logger.LogWarning($"Issuing bill {invoice.ProviderInvoiceId} was rejected by the provider: {ex.Message}");
            return ProviderFailure<InvoiceDetailView>(ex);
        }

        invoice.MarkIssued();
        invoice.SetProviderStatus(issued.Status ?? invoice.ProviderStatus);
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
        _logger.LogInformation($"Bill {invoice.ProviderInvoiceId} put to the shopper.");

        // Read the bill back so the response carries the way to pay it now that it has been issued.
        GatewayInvoice provider = issued;
        try
        {
            provider = await _gateway.GetAsync(invoice.ProviderInvoiceId, cancellationToken);
            invoice.SetProviderStatus(provider.Status ?? invoice.ProviderStatus);
            await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
        }
        catch (VisaInvoicingException ex)
        {
            _logger.LogWarning($"Re-reading issued bill {invoice.ProviderInvoiceId} failed: {ex.Message}");
        }

        return OperationResult<InvoiceDetailView>.Ok(BuildDetailView(invoice, provider));
    }

    public async Task<OperationResult<InvoiceDetailView>> WithdrawAsync(
        string buyerId, bool isOperator, string invoiceId, CancellationToken cancellationToken = default)
    {
        var (invoice, notFound) = await LoadOwnedAsync(buyerId, isOperator, invoiceId, cancellationToken);
        if (notFound is not null)
        {
            return notFound;
        }

        if (invoice!.IsWithdrawn)
        {
            return OperationResult<InvoiceDetailView>.Conflict("This bill has already been withdrawn.");
        }

        GatewayInvoice withdrawn;
        try
        {
            withdrawn = await _gateway.WithdrawAsync(invoice.ProviderInvoiceId, cancellationToken);
        }
        catch (VisaInvoicingException ex)
        {
            _logger.LogWarning($"Withdrawing bill {invoice.ProviderInvoiceId} was rejected by the provider: {ex.Message}");
            return ProviderFailure<InvoiceDetailView>(ex);
        }

        invoice.MarkWithdrawn();
        invoice.SetProviderStatus(withdrawn.Status ?? "CANCELED");
        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
        _logger.LogInformation($"Bill {invoice.ProviderInvoiceId} withdrawn.");

        return OperationResult<InvoiceDetailView>.Ok(BuildDetailView(invoice, withdrawn));
    }

    public async Task<OperationResult<IReadOnlyList<InvoiceListItemView>>> ListMineAsync(
        string buyerId, CancellationToken cancellationToken = default)
    {
        var invoices = await _invoiceRepository.ListAsync(new CustomerInvoicesSpecification(buyerId), cancellationToken);
        IReadOnlyList<InvoiceListItemView> items = invoices.Select(i => new InvoiceListItemView
        {
            InvoiceId = i.ProviderInvoiceId,
            OrderId = i.OrderId,
            State = i.State.ToString(),
            ProviderStatus = i.ProviderStatus,
            Amount = i.Amount,
            Currency = i.Currency,
            DueDate = i.DueDate
        }).ToList();

        return OperationResult<IReadOnlyList<InvoiceListItemView>>.Ok(items);
    }

    public async Task<OperationResult<ReconciliationReportView>> ReconcileAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            return OperationResult<ReconciliationReportView>.Invalid("'to' must not be earlier than 'from'.");
        }

        IReadOnlyList<GatewayInvoiceSummary> providerInvoices;
        try
        {
            providerInvoices = await _gateway.ListRaisedBetweenAsync(from, to, cancellationToken);
        }
        catch (VisaInvoicingException ex)
        {
            _logger.LogWarning($"Listing provider invoices for reconciliation failed: {ex.Message}");
            return ProviderFailure<ReconciliationReportView>(ex);
        }

        var localAll = await _invoiceRepository.ListAsync(cancellationToken);
        var localByProviderId = localAll
            .GroupBy(i => i.ProviderInvoiceId)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var entries = new List<ReconciliationEntryView>();
        var providerIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var summary in providerInvoices)
        {
            providerIds.Add(summary.Id);
            var matched = localByProviderId.TryGetValue(summary.Id, out var local);
            entries.Add(new ReconciliationEntryView
            {
                InvoiceId = summary.Id,
                Classification = (matched ? ReconciliationClassification.Matched : ReconciliationClassification.ProviderOnly).ToString(),
                BearsEShopMarker = (summary.InvoiceNumber ?? summary.Id)
                    .StartsWith(InvoicingConstants.EShopInvoiceNumberPrefix, StringComparison.OrdinalIgnoreCase),
                ProviderStatus = summary.Status,
                Amount = summary.TotalAmount,
                Currency = summary.Currency,
                CustomerName = summary.CustomerName,
                RaisedAt = summary.RaisedAt,
                OrderId = matched ? local!.OrderId : null,
                BuyerId = matched ? local!.BuyerId : null
            });
        }

        // Bills eShop believes it raised in the range that the provider did not return — the reverse gap.
        foreach (var local in localAll.Where(i => i.CreatedAt >= from && i.CreatedAt <= to))
        {
            if (providerIds.Contains(local.ProviderInvoiceId))
            {
                continue;
            }

            entries.Add(new ReconciliationEntryView
            {
                InvoiceId = local.ProviderInvoiceId,
                Classification = ReconciliationClassification.EShopOnly.ToString(),
                BearsEShopMarker = local.InvoiceNumber.StartsWith(InvoicingConstants.EShopInvoiceNumberPrefix, StringComparison.OrdinalIgnoreCase),
                ProviderStatus = local.ProviderStatus,
                Amount = local.Amount,
                Currency = local.Currency,
                CustomerName = local.CustomerName,
                RaisedAt = local.CreatedAt,
                OrderId = local.OrderId,
                BuyerId = local.BuyerId
            });
        }

        var report = new ReconciliationReportView
        {
            From = from,
            To = to,
            Entries = entries.OrderByDescending(e => e.RaisedAt ?? DateTimeOffset.MinValue).ToList(),
            Summary = new ReconciliationSummaryView
            {
                TotalProviderInvoicesInRange = providerInvoices.Count,
                Matched = entries.Count(e => e.Classification == ReconciliationClassification.Matched.ToString()),
                ProviderOnly = entries.Count(e => e.Classification == ReconciliationClassification.ProviderOnly.ToString()),
                EShopOnly = entries.Count(e => e.Classification == ReconciliationClassification.EShopOnly.ToString())
            }
        };

        return OperationResult<ReconciliationReportView>.Ok(report);
    }

    private async Task<(Invoice? invoice, OperationResult<InvoiceDetailView>? notFound)> LoadOwnedAsync(
        string buyerId, bool isOperator, string invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await _invoiceRepository.FirstOrDefaultAsync(new InvoiceByProviderIdSpecification(invoiceId), cancellationToken);
        if (invoice is null)
        {
            return (null, OperationResult<InvoiceDetailView>.NotFound($"Invoice {invoiceId} was not found."));
        }

        // A bill belongs to the shopper whose order it was raised against; one shopper never sees
        // another's. Report not-found rather than forbidden so ownership isn't revealed.
        if (!isOperator && !string.Equals(invoice.BuyerId, buyerId, StringComparison.Ordinal))
        {
            return (null, OperationResult<InvoiceDetailView>.NotFound($"Invoice {invoiceId} was not found."));
        }

        return (invoice, null);
    }

    private async Task RefreshProviderStatusAsync(Invoice invoice, string? providerStatus, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(providerStatus) && !string.Equals(providerStatus, invoice.ProviderStatus, StringComparison.Ordinal))
        {
            invoice.SetProviderStatus(providerStatus);
            await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
        }
    }

    private static IReadOnlyList<GatewayInvoiceLine> BuildLines(Order order) =>
        order.OrderItems.Select(oi => new GatewayInvoiceLine(
            ProductSku: $"CATALOG-{oi.ItemOrdered.CatalogItemId}",
            ProductName: oi.ItemOrdered.ProductName,
            Quantity: oi.Units,
            UnitPrice: oi.UnitPrice)).ToList();

    private InvoiceDetailView BuildDetailView(Invoice invoice, GatewayInvoice provider)
    {
        // The way to pay the bill is only handed out once it has been put to the shopper and not withdrawn.
        var paymentLink = invoice.IsIssued && !invoice.IsWithdrawn ? provider.PaymentLink : null;

        return new InvoiceDetailView
        {
            InvoiceId = invoice.ProviderInvoiceId,
            OrderId = invoice.OrderId,
            State = invoice.State.ToString(),
            ProviderStatus = provider.Status ?? invoice.ProviderStatus,
            Amount = invoice.Amount,
            Currency = invoice.Currency,
            DueDate = invoice.DueDate,
            CustomerName = invoice.CustomerName,
            CustomerEmail = invoice.CustomerEmail,
            PaymentLink = paymentLink,
            History = provider.History
                .Select(h => new InvoiceHistoryView(h.Event, h.Date))
                .ToList()
        };
    }

    private static OperationResult<T> ProviderFailure<T>(VisaInvoicingException ex)
    {
        // A refusal is an outcome of the state the bill is in — surface it as a conflict the caller
        // must be told about, not as a silent no-op and not as an internal error.
        if (ex.ProviderRejected)
        {
            var message = ex.ProviderReason is not null
                ? $"The provider refused this action for the bill's current state ({ex.ProviderReason})."
                : "The provider refused this action for the bill's current state.";
            return OperationResult<T>.Conflict(message);
        }

        return OperationResult<T>.ProviderError("The invoicing provider could not be reached. Please try again.");
    }

    private static string FirstNonEmpty(string? preferred, string fallback) =>
        string.IsNullOrWhiteSpace(preferred) ? fallback : preferred!.Trim();

    private static string DeriveEmail(string buyerId) =>
        buyerId.Contains('@', StringComparison.Ordinal)
            ? buyerId
            : $"{new string(buyerId.Where(char.IsLetterOrDigit).ToArray())}@example.com";

    private static string GenerateInvoiceNumber(int orderId)
    {
        // Provider invoice numbers are capped at 20 characters and double as eShop's origin marker.
        const int maxLength = 20;
        var random = Guid.NewGuid().ToString("N");
        var prefix = $"{InvoicingConstants.EShopInvoiceNumberPrefix}{orderId.ToString(CultureInfo.InvariantCulture)}-";
        if (prefix.Length >= maxLength - 3)
        {
            prefix = InvoicingConstants.EShopInvoiceNumberPrefix;
        }

        var remaining = maxLength - prefix.Length;
        return prefix + random.Substring(0, Math.Min(remaining, random.Length));
    }
}
