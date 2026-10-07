using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class GetAdyenRecordRequest : BaseRequest
{
    public GetAdyenRecordRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
}

/// <summary>
/// Everything Adyen returned for each payment and refund of one order, for support staff.
/// </summary>
public class AdyenRecordResponse : BaseResponse
{
    public AdyenRecordResponse(Guid correlationId) : base(correlationId)
    {
    }

    public AdyenRecordResponse()
    {
    }

    public int OrderId { get; set; }
    public string BuyerId { get; set; } = "";
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string PaymentStatus { get; set; } = "";
    public List<AdyenPaymentRecordDto> Payments { get; set; } = new();
    public List<AdyenRefundRecordDto> Refunds { get; set; } = new();
}

public class AdyenPaymentRecordDto
{
    public int AttemptNumber { get; set; }
    public string Reference { get; set; } = "";
    public string Status { get; set; } = "";
    public decimal Amount { get; set; }
    public long AmountInMinorUnits { get; set; }
    public string Currency { get; set; } = "";
    public string? PspReference { get; set; }
    public string? ResultCode { get; set; }
    public string? RefusalReason { get; set; }
    public string? RefusalReasonCode { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int? SettlesAttemptNumber { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public List<AdyenResponseDto> AdyenResponses { get; set; } = new();
}

public class AdyenRefundRecordDto
{
    public Guid RefundId { get; set; }
    public int Sequence { get; set; }
    public string Reference { get; set; } = "";
    public string Status { get; set; } = "";
    public decimal Amount { get; set; }
    public long AmountInMinorUnits { get; set; }
    public string Currency { get; set; } = "";
    public string? Reason { get; set; }
    public string PaymentPspReference { get; set; } = "";
    public string? PspReference { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public List<AdyenResponseDto> AdyenResponses { get; set; } = new();
}

public class AdyenResponseDto
{
    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary>Null when no response arrived (see <see cref="Note"/>).</summary>
    public int? HttpStatus { get; set; }

    /// <summary>The response body exactly as Adyen sent it, embedded as JSON (every field, known or not).</summary>
    public JsonElement? Body { get; set; }

    /// <summary>The body as text, only when it was not valid JSON.</summary>
    public string? RawBody { get; set; }

    public string? Note { get; set; }
}
