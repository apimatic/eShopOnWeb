using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Validates and canonicalizes numbers through the provider's Lookups v2 API. A number the provider
/// does not consider valid is rejected; a valid one yields the provider's canonical E.164 form.
/// </summary>
public class TwilioPhoneNumberValidator : IPhoneNumberValidator
{
    private readonly TwilioLookupClient _client;

    public TwilioPhoneNumberValidator(TwilioLookupClient client)
    {
        _client = client;
    }

    public async Task<PhoneNumberValidationResult> ValidateAsync(string rawNumber, CancellationToken cancellationToken = default)
    {
        var lookup = await _client.LookupAsync(rawNumber, cancellationToken);

        if (!lookup.Valid || string.IsNullOrEmpty(lookup.PhoneNumber))
        {
            var errors = lookup.ValidationErrors is { Count: > 0 }
                ? (IReadOnlyList<string>)lookup.ValidationErrors
                : new List<string> { "NOT_A_VALID_DESTINATION" };
            return PhoneNumberValidationResult.Invalid(errors);
        }

        return PhoneNumberValidationResult.Valid(lookup.PhoneNumber!);
    }
}
