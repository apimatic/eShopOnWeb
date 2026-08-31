namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// State of the payment associated with an order.
/// </summary>
public enum OrderPaymentStatus
{
    /// <summary>No successful authorization exists yet.</summary>
    AwaitingPayment = 0,

    /// <summary>Funds are on hold with the payment processor.</summary>
    Authorized = 1,

    /// <summary>Funds have been captured (taken) at fulfilment.</summary>
    Captured = 2,

    /// <summary>The authorization was voided; no money moved.</summary>
    Voided = 3,

    /// <summary>The payment attempt failed permanently; a new attempt may be made.</summary>
    Failed = 4
}
