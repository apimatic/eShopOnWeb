using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// ITextMessageProvider over the hand-written Twilio clients. All shapes and
/// endpoints come from the OpenAPI documents under api-specs/twilio.
/// </summary>
public class TwilioTextMessageProvider : ITextMessageProvider
{
    private readonly TwilioMessagingClient _messagingClient;
    private readonly TwilioLookupClient _lookupClient;

    public TwilioTextMessageProvider(TwilioMessagingClient messagingClient, TwilioLookupClient lookupClient)
    {
        _messagingClient = messagingClient;
        _lookupClient = lookupClient;
    }

    public async Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var lookup = await _lookupClient.FetchPhoneNumberAsync(phoneNumber, cancellationToken);
        if (lookup == null)
        {
            return new PhoneNumberValidation(false, null, "The provider does not recognize this number.");
        }
        if (!lookup.Valid)
        {
            var reason = lookup.ValidationErrors is { Count: > 0 }
                ? string.Join(", ", lookup.ValidationErrors)
                : "The provider does not consider this a usable destination.";
            return new PhoneNumberValidation(false, null, reason);
        }

        // Store the provider's canonical (E.164) form, not what the caller typed.
        return new PhoneNumberValidation(true, lookup.PhoneNumber ?? phoneNumber, null);
    }

    public async Task<ProviderMessage> SendMessageAsync(string to, string body, CancellationToken cancellationToken = default)
        => Map(await _messagingClient.CreateMessageAsync(to, body, cancellationToken: cancellationToken));

    public async Task<ProviderMessage> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
        => Map(await _messagingClient.CreateMessageAsync(to, body, sendAt, cancellationToken));

    public async Task<ProviderMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default)
        => Map(await _messagingClient.FetchMessageAsync(messageSid, cancellationToken));

    public async Task<ProviderMessage> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default)
        => Map(await _messagingClient.CancelMessageAsync(messageSid, cancellationToken));

    public async Task RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
        => await _messagingClient.RedactMessageBodyAsync(messageSid, cancellationToken);

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var messages = await _messagingClient.ListMessagesAsync(from, to, cancellationToken);
        return messages.Select(Map).ToList();
    }

    private static ProviderMessage Map(TwilioMessageResource resource) => new(
        resource.Sid!,
        resource.Status ?? "unknown",
        resource.To,
        resource.From,
        resource.Body,
        resource.ErrorCode,
        resource.ErrorMessage,
        TwilioMessagingClient.ParseProviderDate(resource.DateCreated),
        TwilioMessagingClient.ParseProviderDate(resource.DateSent));
}
