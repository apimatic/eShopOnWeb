using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Twilio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioMessagingClient
{
    string FromNumber { get; }
    Task<TwilioMessage> CreateMessageAsync(CreateTwilioMessageRequest request, CancellationToken cancellationToken = default);
    Task<TwilioMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<TwilioMessage> UpdateMessageAsync(string messageSid, string? body, string? status, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TwilioMessage>> ListMessagesAsync(string from, DateTimeOffset fromSent, DateTimeOffset toSent, CancellationToken cancellationToken = default);
}
