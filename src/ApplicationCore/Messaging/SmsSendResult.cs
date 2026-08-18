namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

/// <summary>
/// What the provider returned when it accepted a message to send or schedule: its identifier and the
/// initial delivery status.
/// </summary>
public sealed record SmsSendResult(string MessageSid, string? Status);
