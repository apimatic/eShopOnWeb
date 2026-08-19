namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The provider settings the notification dispatcher needs, surfaced as an ApplicationCore
/// abstraction so the domain layer stays independent of the Infrastructure options type.
/// </summary>
public interface ISmsConfiguration
{
    /// <summary>The account's configured sending number (E.164), used for immediate sends.</summary>
    string SenderNumber { get; }

    /// <summary>The Messaging Service SID, required to schedule the follow-up message.</summary>
    string MessagingServiceSid { get; }
}
