using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

public class PayPalCardDetails
{
    public string Number { get; set; } = "";
    public string Expiry { get; set; } = "";
    public string SecurityCode { get; set; } = "";
    public string? CardholderName { get; set; }
}

public class PayPalAuthorizeResult
{
    public string PayPalOrderId { get; set; } = "";
    public string AuthorizationId { get; set; } = "";
    public string AuthorizationStatus { get; set; } = "";
}

public class PayPalCaptureResult
{
    public string CaptureId { get; set; } = "";
    public string CaptureStatus { get; set; } = "";
    public string CapturedAmount { get; set; } = "";
    public string Currency { get; set; } = "";
    public string? PayPalFee { get; set; }
    public string? NetAmount { get; set; }
    public string? NewAuthorizationId { get; set; }
}

public class PayPalRefundResult
{
    public string RefundId { get; set; } = "";
    public string RefundStatus { get; set; } = "";
    public string? Amount { get; set; }
    public string? Currency { get; set; }
}

public class PayPalVaultResult
{
    public string PaymentTokenId { get; set; } = "";
    public string? PayPalCustomerId { get; set; }
    public string? Last4 { get; set; }
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
}

public class PayPalTransactionRecord
{
    public string? TransactionId { get; set; }
    public string? Status { get; set; }
    public string? Amount { get; set; }
    public string? Currency { get; set; }
    public string? Fee { get; set; }
    public string? InitiationDate { get; set; }
    public string? PayPalReferenceId { get; set; }
}

public class PayPalException : Exception
{
    public int? StatusCode { get; }
    public string? ErrorCode { get; }

    public PayPalException(string message, int? statusCode = null, string? errorCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }
}

public class PayPalAuthorizationRenewException : PayPalException
{
    public PayPalAuthorizationRenewException(string message)
        : base(message, statusCode: 422) { }
}
