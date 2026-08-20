using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class DisabledSmsNotificationGateway : ISmsNotificationGateway
{
    public Task<SmsLookupResult> LookupAsync(string phoneNumber, CancellationToken ct) =>
        Task.FromResult(new SmsLookupResult(false, null, "SMS is not configured in this environment."));

    public Task<ProviderMessageResult> SendAsync(string to, string body, CancellationToken ct) =>
        Task.FromResult(Empty("disabled"));

    public Task<ProviderMessageResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct) =>
        Task.FromResult(Empty("disabled"));

    public Task<ProviderMessageResult> CancelScheduledAsync(string sid, CancellationToken ct) =>
        Task.FromResult(Empty("disabled"));

    public Task<ProviderMessageResult> FetchAsync(string sid, CancellationToken ct) =>
        Task.FromResult(Empty("disabled"));

    public Task<ProviderMessageResult> RedactBodyAsync(string sid, CancellationToken ct) =>
        Task.FromResult(Empty("disabled"));

    public Task<ProviderMessageListResult> ListSentFromConfiguredNumberAsync(
        DateTimeOffset fromInclusive, DateTimeOffset toExclusive, CancellationToken ct) =>
        Task.FromResult(new ProviderMessageListResult(Array.Empty<ProviderMessageResult>(), Truncated: false));

    private static ProviderMessageResult Empty(string status) =>
        new(null, status, null, null, null, null, null, null, null);
}
