namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// State of the payment attached to an order, mirroring the processor-side lifecycle
/// authorize -&gt; capture / void, with refunds tracked separately.
/// </summary>
public enum OrderPaymentStatus
{
    /// <summary>Created locally; no successful authorization at the processor yet.</summary>
    AuthorizationPending = 0,

    /// <summary>Funds are on hold at the processor.</summary>
    Authorized = 1,

    /// <summary>Funds were captured at fulfilment.</summary>
    Captured = 2,

    /// <summary>The hold was released (order cancelled before fulfilment).</summary>
    Voided = 3,

    /// <summary>The last authorization attempt was declined or failed.</summary>
    Failed = 4
}
