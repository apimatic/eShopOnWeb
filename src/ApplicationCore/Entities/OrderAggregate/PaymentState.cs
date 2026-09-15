namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum PaymentStatus
{
    AwaitingPayment, AuthorizationPending, Authorized, AuthorizationVoided, CapturePending,
    Captured, RefundPending, PartiallyRefunded, Refunded, Cancelled
}

public enum FulfilmentStatus { Pending, Fulfilled, Cancelled }
