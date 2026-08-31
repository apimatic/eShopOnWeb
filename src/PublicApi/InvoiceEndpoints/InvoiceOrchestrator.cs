using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class InvoiceOrchestrator : IInvoiceOrchestrator
{
    private const string DefaultPictureUri = "eCatalog-item-default.png";
    private const string AdministratorRole = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Invoice> _invoiceRepository;
    private readonly IRepository<CatalogItem> _catalogRepository;
    private readonly IInvoicingService _invoicingService;
    private readonly IUriComposer _uriComposer;

    public InvoiceOrchestrator(
        IRepository<Order> orderRepository,
        IRepository<Invoice> invoiceRepository,
        IRepository<CatalogItem> catalogRepository,
        IInvoicingService invoicingService,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _invoiceRepository = invoiceRepository;
        _catalogRepository = catalogRepository;
        _invoicingService = invoicingService;
        _uriComposer = uriComposer;
    }

    public async Task<IResult> CreateOrderAsync(CreateOrderRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        var buyerId = UserName(user);
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        if (request.Items is null || request.Items.Count == 0)
            return BadRequest("An order must contain at least one item.");
        if (request.Items.Any(item => item.Quantity <= 0))
            return BadRequest("Every item quantity must be greater than zero.");

        var ids = request.Items.Select(item => item.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(ids), ct);
        var missing = ids.Except(catalogItems.Select(c => c.Id)).ToArray();
        if (missing.Length > 0)
            return BadRequest($"Unknown catalog item id(s): {string.Join(", ", missing)}.");

        var orderItems = request.Items.Select(item =>
        {
            var catalogItem = catalogItems.First(c => c.Id == item.CatalogItemId);
            var pictureUri = string.IsNullOrEmpty(catalogItem.PictureUri)
                ? DefaultPictureUri
                : _uriComposer.ComposePicUri(catalogItem.PictureUri);
            if (string.IsNullOrEmpty(pictureUri))
                pictureUri = DefaultPictureUri;

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, item.Quantity);
        }).ToList();

        var order = new Order(buyerId, BuildAddress(request.ShipToAddress), orderItems);
        await _orderRepository.AddAsync(order, ct);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Total = order.Total(),
            Items = orderItems.Select(oi => new OrderLineDto
            {
                CatalogItemId = oi.ItemOrdered.CatalogItemId,
                ProductName = oi.ItemOrdered.ProductName,
                Quantity = oi.Units,
                UnitPrice = oi.UnitPrice
            }).ToList()
        };

        return Results.Created($"api/orders/{order.Id}", response);
    }

    public async Task<IResult> RaiseInvoiceAsync(int orderId, RaiseInvoiceForOrderRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        var buyerId = UserName(user);
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order is null || !IsOwnerOrAdmin(order.BuyerId, user))
            return Results.NotFound(); // hide the existence of another shopper's order

        var existing = await _invoiceRepository.ListAsync(new InvoicesForOrderSpecification(orderId), ct);
        if (existing.Any(i => i.Status != InvoiceStatus.Withdrawn))
            return Conflict($"Order {orderId} already has a bill.");

        var dueDate = ToUtcMidnight(request.DueDate);
        var customerName = Coalesce(request.CustomerName, order.BuyerId);
        var customerEmail = Coalesce(request.CustomerEmail, order.BuyerId);

        var command = new RaiseInvoiceCommand
        {
            Description = $"eShopOnWeb order {orderId}",
            TotalAmount = order.Total(),
            Currency = InvoicingDefaults.Currency,
            DueDate = dueDate,
            CustomerName = customerName,
            CustomerEmail = customerEmail,
            // The provider requires a globally-unique invoice number across the (shared) account, so a bare
            // order id collides. Keep the order reference and add a unique suffix.
            InvoiceNumber = $"ESHOP-{orderId}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            LineItems = order.OrderItems
                .Select(oi => new InvoiceLineItemDetail(
                    oi.ItemOrdered.CatalogItemId.ToString(CultureInfo.InvariantCulture),
                    oi.ItemOrdered.ProductName, oi.Units, oi.UnitPrice))
                .ToList()
        };

        var provider = await _invoicingService.RaiseInvoiceAsync(command, ct);
        if (string.IsNullOrEmpty(provider.ProviderInvoiceId))
            throw new InvoicingProviderException("The invoicing provider did not return an invoice identifier.");

        var invoice = new Invoice(orderId, order.BuyerId, provider.ProviderInvoiceId, order.Total(),
            InvoicingDefaults.Currency, dueDate, customerName, customerEmail);
        await _invoiceRepository.AddAsync(invoice, ct);

        var response = new CreateInvoiceResponse(request.CorrelationId())
        {
            InvoiceId = invoice.ProviderInvoiceId,
            OrderId = orderId,
            State = invoice.Status.ToString(),
            Amount = invoice.Amount,
            Currency = invoice.Currency,
            DueDate = invoice.DueDate,
            ProviderStatus = provider.Status
        };

        return Results.Created($"api/invoices/{invoice.ProviderInvoiceId}", response);
    }

    public async Task<IResult> GetInvoiceAsync(string invoiceId, ClaimsPrincipal user, CancellationToken ct)
    {
        var invoice = await _invoiceRepository.FirstOrDefaultAsync(new InvoiceByProviderIdSpecification(invoiceId), ct);
        if (invoice is null || !IsOwnerOrAdmin(invoice.BuyerId, user))
            return Results.NotFound();

        var provider = await _invoicingService.GetInvoiceAsync(invoiceId, ct);

        var response = new GetInvoiceResponse
        {
            InvoiceId = invoice.ProviderInvoiceId,
            OrderId = invoice.OrderId,
            Amount = invoice.Amount,
            Currency = invoice.Currency,
            DueDate = invoice.DueDate,
            CustomerName = invoice.CustomerName,
            CustomerEmail = invoice.CustomerEmail,
            State = invoice.Status.ToString(),
            ProviderStatus = provider.Status,
            History = provider.History.Select(h => new InvoiceHistoryDto
            {
                Event = h.Event,
                Date = h.Date,
                TransactionId = h.TransactionId,
                TransactionAmount = h.TransactionAmount
            }).ToList(),
            // The way to pay is only handed out once the bill has been put to the shopper, and never
            // once it has been withdrawn.
            PaymentLink = invoice.IsIssued ? provider.PaymentLink : null
        };

        return Results.Ok(response);
    }

    public async Task<IResult> AmendInvoiceAsync(string invoiceId, AmendInvoiceRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        var invoice = await _invoiceRepository.FirstOrDefaultAsync(new InvoiceByProviderIdSpecification(invoiceId), ct);
        if (invoice is null || !IsOwnerOrAdmin(invoice.BuyerId, user))
            return Results.NotFound();

        if (!invoice.IsDraft)
            return Conflict("This bill has already been put to the shopper or withdrawn, so it can no longer be corrected.");

        var dueDate = request.DueDate.HasValue ? ToUtcMidnight(request.DueDate.Value) : invoice.DueDate;
        var customerName = Coalesce(request.CustomerName, invoice.CustomerName);
        var customerEmail = Coalesce(request.CustomerEmail, invoice.CustomerEmail);

        // The amount is not correctable here — it comes from the order and is re-sent unchanged.
        var command = new AmendInvoiceCommand
        {
            Description = $"eShopOnWeb order {invoice.OrderId}",
            TotalAmount = invoice.Amount,
            Currency = invoice.Currency,
            DueDate = dueDate,
            CustomerName = customerName,
            CustomerEmail = customerEmail
        };

        var provider = await _invoicingService.AmendInvoiceAsync(invoiceId, command, ct);

        invoice.Amend(dueDate, customerName, customerEmail);
        await _invoiceRepository.UpdateAsync(invoice, ct);

        var response = new UpdateInvoiceResponse
        {
            InvoiceId = invoice.ProviderInvoiceId,
            OrderId = invoice.OrderId,
            Amount = invoice.Amount,
            Currency = invoice.Currency,
            DueDate = invoice.DueDate,
            CustomerName = invoice.CustomerName,
            CustomerEmail = invoice.CustomerEmail,
            State = invoice.Status.ToString(),
            ProviderStatus = provider.Status
        };

        return Results.Ok(response);
    }

    public async Task<IResult> IssueInvoiceAsync(string invoiceId, CancellationToken ct)
    {
        var invoice = await _invoiceRepository.FirstOrDefaultAsync(new InvoiceByProviderIdSpecification(invoiceId), ct);
        if (invoice is null)
            return Results.NotFound();

        if (invoice.IsWithdrawn)
            return Conflict("A withdrawn bill cannot be issued.");
        if (invoice.IsIssued)
            return Conflict("This bill has already been issued.");

        var provider = await _invoicingService.IssueInvoiceAsync(invoiceId, ct);

        var paymentLink = provider.PaymentLink;
        var providerStatus = provider.Status;
        if (string.IsNullOrEmpty(paymentLink))
        {
            // The send response did not carry the link; read it back from the provider.
            var refreshed = await _invoicingService.GetInvoiceAsync(invoiceId, ct);
            paymentLink = refreshed.PaymentLink;
            providerStatus = refreshed.Status ?? providerStatus;
        }

        invoice.MarkIssued(paymentLink);
        await _invoiceRepository.UpdateAsync(invoice, ct);

        var response = new IssueInvoiceResponse
        {
            InvoiceId = invoice.ProviderInvoiceId,
            State = invoice.Status.ToString(),
            ProviderStatus = providerStatus,
            PaymentLink = paymentLink
        };

        return Results.Ok(response);
    }

    public async Task<IResult> WithdrawInvoiceAsync(string invoiceId, CancellationToken ct)
    {
        var invoice = await _invoiceRepository.FirstOrDefaultAsync(new InvoiceByProviderIdSpecification(invoiceId), ct);
        if (invoice is null)
            return Results.NotFound();

        if (invoice.IsWithdrawn)
            return Conflict("This bill has already been withdrawn.");

        var provider = await _invoicingService.WithdrawInvoiceAsync(invoiceId, ct);

        invoice.MarkWithdrawn();
        await _invoiceRepository.UpdateAsync(invoice, ct);

        var response = new WithdrawInvoiceResponse
        {
            InvoiceId = invoice.ProviderInvoiceId,
            State = invoice.Status.ToString(),
            ProviderStatus = provider.Status
        };

        return Results.Ok(response);
    }

    public async Task<IResult> GetMyInvoicesAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var buyerId = UserName(user);
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var invoices = await _invoiceRepository.ListAsync(new CustomerInvoicesSpecification(buyerId), ct);

        var response = new MyInvoicesResponse
        {
            Invoices = invoices.Select(i => new InvoiceSummaryDto
            {
                InvoiceId = i.ProviderInvoiceId,
                OrderId = i.OrderId,
                Amount = i.Amount,
                Currency = i.Currency,
                DueDate = i.DueDate,
                State = i.Status.ToString(),
                CreatedDate = i.CreatedDate
            }).ToList()
        };

        return Results.Ok(response);
    }

    public async Task<IResult> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (to < from)
            return BadRequest("'to' must be on or after 'from'.");

        var providerInvoices = await _invoicingService.ListInvoicesAsync(from, to, ct);
        var localInvoices = await _invoiceRepository.ListAsync(new InvoicesByCreatedDateRangeSpecification(from, to), ct);

        var localByProviderId = localInvoices
            .Where(i => !string.IsNullOrEmpty(i.ProviderInvoiceId))
            .GroupBy(i => i.ProviderInvoiceId)
            .ToDictionary(g => g.Key, g => g.First());

        var entries = new List<ReconciliationEntryDto>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var providerInvoice in providerInvoices)
        {
            seen.Add(providerInvoice.ProviderInvoiceId);
            localByProviderId.TryGetValue(providerInvoice.ProviderInvoiceId, out var local);

            entries.Add(new ReconciliationEntryDto
            {
                InvoiceId = providerInvoice.ProviderInvoiceId,
                KnownToProvider = true,
                KnownToEShop = local is not null,
                Source = local is not null ? "Both" : "ProviderOnly",
                ProviderStatus = providerInvoice.Status,
                ProviderCreatedDate = providerInvoice.CreatedDate,
                OrderId = local?.OrderId,
                EShopState = local?.Status.ToString(),
                EShopCreatedDate = local?.CreatedDate
            });
        }

        foreach (var local in localInvoices)
        {
            if (seen.Contains(local.ProviderInvoiceId))
                continue;

            entries.Add(new ReconciliationEntryDto
            {
                InvoiceId = local.ProviderInvoiceId,
                KnownToProvider = false,
                KnownToEShop = true,
                Source = "EShopOnly",
                OrderId = local.OrderId,
                EShopState = local.Status.ToString(),
                EShopCreatedDate = local.CreatedDate
            });
        }

        var response = new ReconciliationResponse
        {
            From = from,
            To = to,
            Summary = new ReconciliationSummaryDto
            {
                ProviderInvoiceCount = providerInvoices.Count,
                EShopInvoiceCount = localInvoices.Count,
                MatchedCount = entries.Count(e => e.Source == "Both"),
                ProviderOnlyCount = entries.Count(e => e.Source == "ProviderOnly"),
                EShopOnlyCount = entries.Count(e => e.Source == "EShopOnly")
            },
            Entries = entries
                .OrderByDescending(e => e.ProviderCreatedDate ?? e.EShopCreatedDate ?? DateTimeOffset.MinValue)
                .ToList()
        };

        return Results.Ok(response);
    }

    // ---- helpers -----------------------------------------------------------------------------------

    private static string? UserName(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.Name);

    private static bool IsOwnerOrAdmin(string ownerBuyerId, ClaimsPrincipal user) =>
        user.IsInRole(AdministratorRole) || string.Equals(ownerBuyerId, UserName(user), StringComparison.Ordinal);

    private static DateTimeOffset ToUtcMidnight(DateOnly date) =>
        new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero);

    private static string Coalesce(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static Address BuildAddress(AddressRequest? address)
    {
        return new Address(
            Coalesce(address?.Street, "N/A"),
            Coalesce(address?.City, "N/A"),
            Coalesce(address?.State, "N/A"),
            Coalesce(address?.Country, "N/A"),
            Coalesce(address?.ZipCode, "00000"));
    }

    private static IResult Conflict(string message) =>
        Results.Conflict(new BlazorShared.Models.ErrorDetails { StatusCode = StatusCodes.Status409Conflict, Message = message });

    private static IResult BadRequest(string message) =>
        Results.BadRequest(new BlazorShared.Models.ErrorDetails { StatusCode = StatusCodes.Status400BadRequest, Message = message });
}
