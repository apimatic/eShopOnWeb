using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>Onboards a seller as a new merchant through Stax's partner (platform) API.</summary>
public class SellerService
{
    private readonly StaxClient _stax;

    public SellerService(StaxClient stax)
    {
        _stax = stax;
    }

    public async Task<object> OnboardAsync(Body body, CancellationToken ct = default)
    {
        var contact = body.Child("contact");
        var company = body.Str("company", "companyName", "businessName");
        var contactName = contact?.Str("name", "fullName") ?? body.Str("contactName", "name")
                          ?? (body.Get("contact") is { ValueKind: System.Text.Json.JsonValueKind.String } c ? c.GetString() : null);
        var email = contact?.Str("email") ?? body.Str("contactEmail", "email");
        var phone = contact?.Str("phone", "phoneNumber") ?? body.Str("contactPhone", "phone");

        if (company == null) throw ApiException.BadRequest("company is required.");
        if (contactName == null && email == null) throw ApiException.BadRequest("contact needs a name or an email.");
        if (email != null && !email.Contains('@')) throw ApiException.BadRequest("contact email is not valid.");

        if (!_stax.HasPlatformKey)
            throw ApiException.Unprocessable("Sellers cannot be onboarded: this shop has no Stax platform key (Stax:PlatformApiKey).");

        var registration = new System.Collections.Generic.Dictionary<string, object?>
        {
            ["business_legal_name"] = company,
            ["business_dba"] = company
        };
        if (email != null) { registration["email"] = email; registration["business_email"] = email; }
        if (phone != null) registration["business_phone_number"] = phone;
        if (contactName != null)
        {
            var parts = contactName.Split(' ', 2, System.StringSplitOptions.RemoveEmptyEntries);
            registration["owner_first_name"] = parts.Length > 0 ? parts[0] : contactName;
            if (parts.Length > 1) registration["owner_last_name"] = parts[1];
        }

        var result = await _stax.PostAsPlatformAsync("admin/enroll", new
        {
            skip_account_page = false,
            registration
        }, ct);

        var merchant = result["merchant"] ?? result;
        return new
        {
            merchantId = merchant["id"]?.GetValue<string>(),
            status = merchant["status"]?.GetValue<string>(),
            company,
            contactEmail = email
        };
    }
}
