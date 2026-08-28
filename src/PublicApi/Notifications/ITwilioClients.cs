using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public interface ITwilioLookupClient
{
    Task<TwilioLookupResponse> LookupAsync(string phoneNumber, CancellationToken cancellationToken);
}

public interface ITwilioMessagingClient
{
    Task<TwilioMessage> SendAsync(string destination, string content, DateTimeOffset? sendAt, CancellationToken cancellationToken);
    Task<TwilioMessage> FetchAsync(string messageSid, CancellationToken cancellationToken);
    Task<TwilioMessage> CancelAsync(string messageSid, CancellationToken cancellationToken);
    Task<TwilioMessage> RedactAsync(string messageSid, CancellationToken cancellationToken);
    Task<IReadOnlyList<TwilioMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
