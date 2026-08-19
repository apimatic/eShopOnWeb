using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A hand-written client for Twilio's Lookups v2 API (Twilio's <c>lookups_v2</c> OpenAPI
/// document is the authoritative contract). Lookups is served from its own host
/// (<c>https://lookups.twilio.com</c>) which the <c>Twilio:BaseUrl</c> messaging override
/// does not govern.
/// </summary>
public interface ITwilioPhoneLookupClient
{
    /// <summary>
    /// Looks a number up so it can be validated and canonicalised before it is stored.
    /// <c>GET /v2/PhoneNumbers/{PhoneNumber}</c>.
    /// </summary>
    Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);
}

/// <summary>A projection of Twilio's <c>lookups.v2.phone_number</c> resource.</summary>
public record PhoneLookupResult
{
    /// <summary>Whether the provider considers the number a usable destination.</summary>
    public bool Valid { get; init; }

    /// <summary>The provider's canonical E.164 form of the number.</summary>
    public string? PhoneNumber { get; init; }

    public IReadOnlyList<string> ValidationErrors { get; init; } = new List<string>();
}
