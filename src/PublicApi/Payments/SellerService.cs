using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class SellerContact
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
}

public class OnboardSellerRequest
{
    private SellerContact? _contact;

    public string? Company { get; set; }
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? Email { get; set; }
    public string? Name { get; set; }
    public string? Phone { get; set; }

    /// <summary>The seller's contact: an object with name, email and phone, or just an e-mail address or a name.</summary>
    public JsonElement? Contact
    {
        get => null;
        set
        {
            if (value is not { } e) return;
            if (e.ValueKind == JsonValueKind.Object)
            {
                string? Pick(params string[] names)
                {
                    foreach (var p in e.EnumerateObject())
                        if (p.Value.ValueKind == JsonValueKind.String && names.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
                            return p.Value.GetString();
                    return null;
                }
                _contact = new SellerContact
                {
                    Name = Pick("name", "fullName", "contactName", "contact_name"),
                    Email = Pick("email", "contactEmail", "contact_email"),
                    Phone = Pick("phone", "contactPhone", "contact_phone")
                };
                var first = Pick("firstName", "firstname");
                var last = Pick("lastName", "lastname");
                if (string.IsNullOrWhiteSpace(_contact.Name) && (first != null || last != null))
                    _contact.Name = $"{first} {last}".Trim();
            }
            else if (e.ValueKind == JsonValueKind.String)
            {
                var text = e.GetString();
                _contact = text != null && text.Contains('@') ? new SellerContact { Email = text } : new SellerContact { Name = text };
            }
        }
    }

    public SellerContact Resolve() => new()
    {
        Name = FirstOf(_contact?.Name, ContactName, Name),
        Email = FirstOf(_contact?.Email, ContactEmail, Email),
        Phone = FirstOf(_contact?.Phone, ContactPhone, Phone)
    };

    private static string? FirstOf(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}

public record SellerDto(string? SellerId, string? Company, string? Status, string? MerchantId, string? ContactEmail);

public class SellerService
{
    private readonly StaxGateway _stax;

    public SellerService(StaxGateway stax) => _stax = stax;

    public async Task<SellerDto> Onboard(OnboardSellerRequest request, CancellationToken ct)
    {
        var company = request.Company?.Trim();
        if (string.IsNullOrEmpty(company))
            throw AppException.BadRequest("company is required.");
        var contact = request.Resolve();
        var email = contact.Email?.Trim();
        if (string.IsNullOrEmpty(email) || !IsEmail(email))
            throw AppException.BadRequest("contact.email must be a valid e-mail address.");

        if (!_stax.PlatformConfigured)
            throw new AppException(HttpStatusCode.Conflict, "Seller onboarding is not available: this shop has no Stax platform key (Stax:PlatformApiKey) configured.");

        var body = new Dictionary<string, object?>
        {
            ["company_name"] = company,
            ["contact_name"] = string.IsNullOrWhiteSpace(contact.Name) ? null : contact.Name.Trim(),
            ["contact_email"] = email,
            ["contact_phone"] = string.IsNullOrWhiteSpace(contact.Phone) ? null : contact.Phone.Trim()
        };
        var created = await _stax.CreateMerchant(body.Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value), ct);
        return new SellerDto(
            StaxGateway.Str(created, "id"),
            StaxGateway.Str(created, "company_name") ?? company,
            StaxGateway.Str(created, "status"),
            StaxGateway.Str(created, "mid"),
            StaxGateway.Str(created, "contact_email") ?? email);
    }

    private static bool IsEmail(string value)
    {
        try { return new MailAddress(value).Address == value; }
        catch (FormatException) { return false; }
    }
}
