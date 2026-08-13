using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Twilio;

/// <summary>
/// Talks to the provider's Lookup API — served from its own host, and therefore NOT governed by
/// the messaging-API base-url override — to decide whether a number is a usable destination and
/// to obtain its canonical form.
/// </summary>
public interface ITwilioLookupClient
{
    Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);
}
