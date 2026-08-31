using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

// ---- Requests --------------------------------------------------------------------------------------

public class RaiseInvoiceForOrderRequest : BaseRequest
{
    /// <summary>The calendar date the bill falls due.</summary>
    public DateOnly DueDate { get; set; }

    /// <summary>Customer details the bill carries; default to the order's buyer when omitted.</summary>
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
}

public class AmendInvoiceRequest : BaseRequest
{
    /// <summary>A corrected due date, or null to leave it unchanged.</summary>
    public DateOnly? DueDate { get; set; }

    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
}

// ---- Responses -------------------------------------------------------------------------------------

public class CreateInvoiceResponse : BaseResponse
{
    public CreateInvoiceResponse(Guid correlationId) : base(correlationId) { }
    public CreateInvoiceResponse() { }

    /// <summary>The provider's invoice identifier — a top-level field, and what every later action targets.</summary>
    public string InvoiceId { get; set; } = string.Empty;

    public int OrderId { get; set; }
    public string State { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset DueDate { get; set; }
    public string? ProviderStatus { get; set; }
}

public class GetInvoiceResponse : BaseResponse
{
    public GetInvoiceResponse(Guid correlationId) : base(correlationId) { }
    public GetInvoiceResponse() { }

    public string InvoiceId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset DueDate { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;

    /// <summary>The local lifecycle state: Draft, Issued or Withdrawn.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>The status the provider reports for the bill.</summary>
    public string? ProviderStatus { get; set; }

    /// <summary>The provider's account of how the bill reached its current state.</summary>
    public List<InvoiceHistoryDto> History { get; set; } = new();

    /// <summary>A top-level link the shopper can pay with — present only once the bill has been issued.</summary>
    public string? PaymentLink { get; set; }
}

public class UpdateInvoiceResponse : BaseResponse
{
    public UpdateInvoiceResponse(Guid correlationId) : base(correlationId) { }
    public UpdateInvoiceResponse() { }

    public string InvoiceId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset DueDate { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
}

public class IssueInvoiceResponse : BaseResponse
{
    public IssueInvoiceResponse(Guid correlationId) : base(correlationId) { }
    public IssueInvoiceResponse() { }

    public string InvoiceId { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }

    /// <summary>The link the shopper can pay with, now that the bill has been put to them.</summary>
    public string? PaymentLink { get; set; }
}

public class WithdrawInvoiceResponse : BaseResponse
{
    public WithdrawInvoiceResponse(Guid correlationId) : base(correlationId) { }
    public WithdrawInvoiceResponse() { }

    public string InvoiceId { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
}

public class MyInvoicesResponse : BaseResponse
{
    public MyInvoicesResponse(Guid correlationId) : base(correlationId) { }
    public MyInvoicesResponse() { }

    public List<InvoiceSummaryDto> Invoices { get; set; } = new();
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public ReconciliationSummaryDto Summary { get; set; } = new();
    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}

// ---- DTOs ------------------------------------------------------------------------------------------

public class InvoiceHistoryDto
{
    public string? Event { get; set; }
    public DateTimeOffset? Date { get; set; }
    public string? TransactionId { get; set; }
    public string? TransactionAmount { get; set; }
}

public class InvoiceSummaryDto
{
    public string InvoiceId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset DueDate { get; set; }

    /// <summary>Where the bill has got to: Draft, Issued or Withdrawn.</summary>
    public string State { get; set; } = string.Empty;

    public DateTimeOffset CreatedDate { get; set; }
}

public class ReconciliationEntryDto
{
    /// <summary>The provider invoice id this row lines up on.</summary>
    public string InvoiceId { get; set; } = string.Empty;

    /// <summary>Which side(s) know this bill: "Both", "ProviderOnly" (not eShop's), or "EShopOnly".</summary>
    public string Source { get; set; } = string.Empty;

    public bool KnownToEShop { get; set; }
    public bool KnownToProvider { get; set; }

    public string? ProviderStatus { get; set; }
    public DateTimeOffset? ProviderCreatedDate { get; set; }

    /// <summary>The eShop order the bill was raised against — present only when eShop knows the bill.</summary>
    public int? OrderId { get; set; }
    public string? EShopState { get; set; }
    public DateTimeOffset? EShopCreatedDate { get; set; }
}

public class ReconciliationSummaryDto
{
    public int ProviderInvoiceCount { get; set; }
    public int EShopInvoiceCount { get; set; }
    public int MatchedCount { get; set; }

    /// <summary>Bills the provider knows about that eShop does not — the provider account's other activity.</summary>
    public int ProviderOnlyCount { get; set; }

    /// <summary>Bills eShop believes it raised that the provider's record does not show in range.</summary>
    public int EShopOnlyCount { get; set; }
}
