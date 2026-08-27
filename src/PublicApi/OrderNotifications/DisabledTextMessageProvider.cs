using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.OrderNotifications;

internal sealed class DisabledTextMessageProvider : ITextMessageProvider
{
    private static MessagingProviderException Disabled() =>
        new("The messaging provider is disabled in this environment.", 503);

    public Task<string?> ValidateAndCanonicalizeAsync(string number, CancellationToken cancellationToken) => Task.FromException<string?>(Disabled());
    public Task<ProviderMessageSnapshot> SendAsync(string destination, string body, CancellationToken cancellationToken) => Task.FromException<ProviderMessageSnapshot>(Disabled());
    public Task<ProviderMessageSnapshot> ScheduleAsync(string destination, string body, DateTimeOffset sendAt, CancellationToken cancellationToken) => Task.FromException<ProviderMessageSnapshot>(Disabled());
    public Task<ProviderMessageSnapshot> CancelAsync(string providerMessageSid, CancellationToken cancellationToken) => Task.FromException<ProviderMessageSnapshot>(Disabled());
    public Task<ProviderMessageSnapshot> FetchAsync(string providerMessageSid, CancellationToken cancellationToken) => Task.FromException<ProviderMessageSnapshot>(Disabled());
    public Task<ProviderMessageSnapshot> RedactAsync(string providerMessageSid, CancellationToken cancellationToken) => Task.FromException<ProviderMessageSnapshot>(Disabled());
    public Task<IReadOnlyList<ProviderMessageSnapshot>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) => Task.FromException<IReadOnlyList<ProviderMessageSnapshot>>(Disabled());
}
