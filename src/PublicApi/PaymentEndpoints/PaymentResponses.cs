using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
}

public class PaymentResponse
{
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public string? PaymentInstrumentId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public bool IdempotentReplay { get; set; }
}

public class FulfilmentResponse
{
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? TransferId { get; set; }
    public string? TransferState { get; set; }
    public decimal CapturedAmount { get; set; }
    public decimal FinixFee { get; set; }
    public decimal NetProceeds { get; set; }
    public string Currency { get; set; } = string.Empty;
    public bool IdempotentReplay { get; set; }
}

public class CancelResponse
{
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public string? Message { get; set; }
    public bool IdempotentReplay { get; set; }
}

public class RefundResponse
{
    public int RefundId { get; set; }
    public int OrderId { get; set; }
    public string? FinixRefundTransferId { get; set; }
    public string? FinixRefundState { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal TotalRefunded { get; set; }
    public decimal RemainingRefundable { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public bool IdempotentReplay { get; set; }
}

public class MyOrdersResponse
{
    public List<OrderSummary> Orders { get; set; } = new();
}

public class PaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? CardholderName { get; set; }
    public string? FinixPaymentInstrumentId { get; set; }
}

public class PaymentMethodListResponse
{
    public List<PaymentMethodResponse> PaymentMethods { get; set; } = new();
}
