using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioMessagingClient
{
    Task<ProviderMessage> CreateMessageAsync(CreateProviderMessageRequest request, CancellationToken cancellationToken = default);
    Task<ProviderMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<ProviderMessage> UpdateMessageAsync(string messageSid, UpdateProviderMessageRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(ListProviderMessagesRequest request, CancellationToken cancellationToken = default);
}

public class CreateProviderMessageRequest
{
    public string To { get; init; } = string.Empty;
    public string? From { get; init; }
    public string? Body { get; init; }
    public string? MessagingServiceSid { get; init; }
    public string? ScheduleType { get; init; }
    public DateTimeOffset? SendAt { get; init; }
}

public class UpdateProviderMessageRequest
{
    public string? Body { get; init; }
    public string? Status { get; init; }
}

public class ListProviderMessagesRequest
{
    public string? From { get; init; }
    public DateTimeOffset? DateSentAfter { get; init; }
    public DateTimeOffset? DateSentBefore { get; init; }
}

public class ProviderMessage
{
    public string? Sid { get; init; }
    public string? Status { get; init; }
    public string? Body { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset? DateCreated { get; init; }
    public DateTimeOffset? DateSent { get; init; }
    public DateTimeOffset? DateUpdated { get; init; }
}
