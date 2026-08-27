using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A message as the provider sees it.</summary>
public class ProviderMessage
{
    public string Sid { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? To { get; set; }
    public string? From { get; set; }
    public string? Body { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? DateCreated { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}

public interface ISmsProvider
{
    /// <summary>Sends a message immediately. Throws SmsProviderException if the provider rejects the request.</summary>
    Task<ProviderMessage> SendMessageAsync(string to, string body, CancellationToken cancellationToken = default);

    /// <summary>Queues a message with the provider for delivery at a future time.</summary>
    Task<ProviderMessage> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Reads the provider's current authoritative state of one message. Returns null if unknown to the provider.</summary>
    Task<ProviderMessage?> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancels a message that has not yet gone out (provider status scheduled).</summary>
    Task CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Redacts the message body at the provider so the text is no longer retrievable there.</summary>
    Task RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's configured
    /// sending number within the given range, covering every page of the range.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public class SmsProviderException : Exception
{
    public SmsProviderException(string message) : base(message) { }
    public SmsProviderException(string message, Exception innerException) : base(message, innerException) { }
}
