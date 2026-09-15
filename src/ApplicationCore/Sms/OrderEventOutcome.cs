namespace Microsoft.eShopWeb.ApplicationCore.Sms;

/// <summary>The result of applying an operator order event (dispatch/cancel) to an order's notifications.</summary>
public enum OrderEventOutcome
{
    /// <summary>The event was applied and the shopper notified (or skipped for lack of a number on file).</summary>
    Notified,

    /// <summary>The order was already dispatched — the event was not applied again.</summary>
    AlreadyDispatched,

    /// <summary>The order was already cancelled — the event was not applied again.</summary>
    AlreadyCancelled
}
