using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

// ---- Requests ---------------------------------------------------------------------------

/// <summary>A single catalog line to order.</summary>
public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; } = 1;
}

/// <summary>Body for POST /api/orders — place an order from catalog items.</summary>
public class CreateOrderRequest : BaseRequest
{
    public List<OrderItemRequest> Items { get; set; } = new();
}

/// <summary>
/// Body for POST /api/orders/{orderId}/invoice. Carries only the calendar date the bill falls
/// due; what is billed comes from the order itself. <see cref="OrderId"/> is bound from the route.
/// </summary>
public class RaiseInvoiceRequest : BaseRequest
{
    public int OrderId { get; set; }

    /// <summary>The calendar date the bill falls due, as an ISO calendar date (yyyy-MM-dd).</summary>
    public string DueDate { get; set; } = string.Empty;
}

/// <summary>
/// Body for PATCH /api/invoices/{invoiceId}. Any field left null is left unchanged. The billed
/// amount is not correctable here — it always comes from the order. <see cref="InvoiceId"/> is
/// bound from the route.
/// </summary>
public class CorrectInvoiceRequest : BaseRequest
{
    public string InvoiceId { get; set; } = string.Empty;

    /// <summary>New due date (yyyy-MM-dd), or null to leave unchanged.</summary>
    public string? DueDate { get; set; }

    public string? CustomerName { get; set; }

    public string? CustomerEmail { get; set; }
}

// ---- Responses --------------------------------------------------------------------------

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Units { get; set; }
    public decimal UnitPrice { get; set; }
}

public class CreateOrderResponse
{
    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public List<OrderLineDto> Items { get; set; } = new();
}

public class InvoiceHistoryDto
{
    public string? Event { get; set; }
    public DateTimeOffset? Date { get; set; }
}

/// <summary>The full view of a bill returned by create/get/correct/issue/withdraw.</summary>
public class InvoiceDto
{
    /// <summary>The provider's identifier for the bill — this is what operator endpoints act on.</summary>
    public string InvoiceId { get; set; } = string.Empty;

    public int OrderId { get; set; }

    /// <summary>Current status as reported by the provider (DRAFT, CREATED, SENT, PARTIAL, PAID, CANCELED).</summary>
    public string Status { get; set; } = string.Empty;

    public bool Issued { get; set; }
    public bool Withdrawn { get; set; }

    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;

    /// <summary>The calendar date the bill falls due (yyyy-MM-dd).</summary>
    public string DueDate { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;

    /// <summary>
    /// How the shopper can pay the bill. Present only once the bill has been put to the shopper
    /// and not withdrawn; null otherwise.
    /// </summary>
    public string? PaymentLink { get; set; }

    /// <summary>The provider's own account of how the bill reached its current state.</summary>
    public List<InvoiceHistoryDto> History { get; set; } = new();

    public DateTimeOffset? ProviderSubmitTimeUtc { get; set; }
}

/// <summary>A compact view of a bill used by GET /api/my-invoices.</summary>
public class InvoiceSummaryDto
{
    public string InvoiceId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool Issued { get; set; }
    public bool Withdrawn { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string DueDate { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

// ---- Reconciliation ---------------------------------------------------------------------

public class ReconciliationEntryDto
{
    /// <summary>The provider's invoice identifier — what operator endpoints act on.</summary>
    public string InvoiceId { get; set; } = string.Empty;
    public string? Status { get; set; }
    public DateTimeOffset? CreatedDate { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string? CustomerName { get; set; }

    /// <summary>True when eShop recognises this bill as its own; false for other activity on the shared account.</summary>
    public bool RecognizedByEShop { get; set; }

    /// <summary>The eShop order this bill was raised against, when recognised.</summary>
    public int? OrderId { get; set; }

    /// <summary>Human-readable classification: "eShop", "external", or "eShop (missing at provider)".</summary>
    public string Source { get; set; } = string.Empty;
}

public class ReconciliationReportDto
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    public int ProviderInvoiceCount { get; set; }
    public int RecognizedByEShopCount { get; set; }
    public int ExternalCount { get; set; }
    public int MissingAtProviderCount { get; set; }

    /// <summary>Every bill the provider recorded in the range, each flagged as eShop's or external.</summary>
    public List<ReconciliationEntryDto> ProviderInvoices { get; set; } = new();

    /// <summary>Bills eShop believes it raised in the range but the provider has no record of.</summary>
    public List<ReconciliationEntryDto> MissingAtProvider { get; set; } = new();
}
