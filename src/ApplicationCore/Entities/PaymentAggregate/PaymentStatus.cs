namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>The state of the money for one order, as far as this application knows it.</summary>
public enum PaymentStatus
{
    /// <summary>A payment row exists but no hold has been placed yet.</summary>
    PendingAuthorization = 0,

    /// <summary>Funds are held at the processor. Nothing has been taken.</summary>
    Authorized = 1,

    /// <summary>The hold was captured — the money has moved.</summary>
    Captured = 2,

    /// <summary>The hold was released without capturing.</summary>
    Voided = 3,

    /// <summary>Part of the captured amount has been returned.</summary>
    PartiallyRefunded = 4,

    /// <summary>The whole captured amount has been returned.</summary>
    Refunded = 5,

    /// <summary>The processor declined, and no funds are held.</summary>
    Failed = 6
}
