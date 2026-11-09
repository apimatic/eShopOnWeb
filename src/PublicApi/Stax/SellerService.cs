using System.Net.Mail;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Stax;

public class SellerContact
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
}

public class OnboardSellerRequest
{
    public string? Company { get; set; }
    public SellerContact? Contact { get; set; }
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
}

public class SellerDto
{
    public string MerchantId { get; set; } = string.Empty;
    public string? Company { get; set; }
    public string? Status { get; set; }
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
}

public class SellerService
{
    private readonly StaxClient _stax;

    public SellerService(StaxClient stax)
    {
        _stax = stax;
    }

    /// <summary>Onboards a seller as a new merchant. This is a partner-level call and needs the platform key.</summary>
    public async Task<SellerDto> OnboardAsync(OnboardSellerRequest request, CancellationToken ct)
    {
        var company = request.Company?.Trim();
        var name = (request.Contact?.Name ?? request.ContactName)?.Trim();
        var email = (request.Contact?.Email ?? request.ContactEmail)?.Trim();
        var phone = (request.Contact?.Phone ?? request.ContactPhone)?.Trim();

        if (string.IsNullOrEmpty(company)) throw ApiException.BadRequest("The seller's company is required.");
        if (string.IsNullOrEmpty(email) || !IsEmail(email)) throw ApiException.BadRequest("A valid contact email is required.");
        if (!_stax.HasPlatformKey)
        {
            throw ApiException.Unprocessable("Sellers can't be onboarded: this shop has no Stax platform API key (Stax:PlatformApiKey).");
        }

        var body = new JsonObject { ["company_name"] = company, ["contact_email"] = email };
        if (!string.IsNullOrEmpty(name)) body["contact_name"] = name;
        if (!string.IsNullOrEmpty(phone)) body["contact_phone"] = phone;

        JsonNode merchant;
        try
        {
            merchant = await _stax.PostAsPlatformAsync("merchant", body, ct);
        }
        catch (StaxException ex) when (ex.StaxStatusCode is 400 or 422)
        {
            throw ApiException.Unprocessable($"Stax refused the seller: {ex.Message}");
        }

        return new SellerDto
        {
            MerchantId = merchant.Str("id") ?? string.Empty,
            Company = merchant.Str("company_name"),
            Status = merchant.Str("status"),
            ContactName = merchant.Str("contact_name"),
            ContactEmail = merchant.Str("contact_email")
        };
    }

    private static bool IsEmail(string value)
    {
        try
        {
            return new MailAddress(value).Address == value;
        }
        catch (System.FormatException)
        {
            return false;
        }
    }
}
