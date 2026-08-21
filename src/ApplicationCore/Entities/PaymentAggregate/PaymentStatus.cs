namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The money lifecycle owned jointly with PayPal. Mirrors what PayPal reports for the hold, the
/// capture and the refunds, so a later request can act on state the payment already carries.
/// </summary>
public enum PaymentStatus
{
    /// <summary>A PayPal order exists for the amount but no hold has been placed yet.</summary>
    Created = 0,

    /// <summary>Funds are held (authorized) but not captured.</summary>
    Authorized = 1,

    /// <summary>Funds were captured at fulfilment.</summary>
    Captured = 2,

    /// <summary>The authorization was voided (funds released) before capture.</summary>
    Voided = 3,

    /// <summary>A captured payment was refunded in part.</summary>
    PartiallyRefunded = 4,

    /// <summary>A captured payment was refunded in full.</summary>
    Refunded = 5
}
