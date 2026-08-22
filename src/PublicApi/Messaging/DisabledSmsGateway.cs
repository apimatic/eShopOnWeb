using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace Microsoft.eShopWeb.PublicApi.Messaging;

internal sealed class DisabledSmsGateway : ISmsGateway
{
    public Task<SmsDispatchResult> SendAsync(string to, string body, CancellationToken cancellationToken) =>
        Task.FromResult(new SmsDispatchResult(false, null, "disabled", null, null, "Twilio is not configured."));

    public Task<SmsDispatchResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken) =>
        SendAsync(to, body, cancellationToken);

    public Task<SmsMessageSnapshot> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken) =>
        Task.FromResult(Failed());

    public Task<SmsMessageSnapshot> FetchAsync(string providerSid, CancellationToken cancellationToken) =>
        Task.FromResult(Failed());

    public Task<SmsMessageSnapshot> RedactContentAsync(string providerSid, CancellationToken cancellationToken) =>
        Task.FromResult(Failed());

    public Task<SmsListResult> ListSentFromConfiguredNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        Task.FromResult(new SmsListResult(false, Array.Empty<SmsMessageSnapshot>(), false, "Twilio is not configured."));

    private static SmsMessageSnapshot Failed() =>
        new(false, null, null, null, null, null, null, null, null, null, "Twilio is not configured.");
}

internal sealed class DisabledPhoneNumberLookup : IPhoneNumberLookup
{
    public Task<PhoneLookupResult> LookupAsync(string rawNumber, CancellationToken cancellationToken) =>
        Task.FromResult(PhoneLookupResult.ProviderFault("Twilio is not configured."));
}
