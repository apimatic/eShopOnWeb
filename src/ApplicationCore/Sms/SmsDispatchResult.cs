namespace Microsoft.eShopWeb.ApplicationCore.Sms;

/// <summary>
/// What the provider returned when a message was handed over for delivery or scheduling:
/// its identifier for the message and the initial status it assigned.
/// </summary>
/// <param name="ProviderMessageId">The provider's identifier for the message (Twilio Message SID).</param>
/// <param name="Status">The provider's initial status (e.g. queued, accepted, scheduled).</param>
public record SmsDispatchResult(string ProviderMessageId, string Status);
