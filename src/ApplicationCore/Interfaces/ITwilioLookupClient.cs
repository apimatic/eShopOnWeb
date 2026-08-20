using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Twilio Lookup v2 basic request (formatting + validation). Hosted at lookups.twilio.com,
/// not the messaging API.
/// </summary>
public interface ITwilioLookupClient
{
    Task<TwilioLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);
}

public record TwilioLookupResult(bool Valid, string? CanonicalPhoneNumber, string? ValidationError);
