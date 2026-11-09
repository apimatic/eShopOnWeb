using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.eShopWeb.PublicApi.Stax;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class SellerService
{
    private readonly StaxContext _db;
    private readonly StaxClient _stax;

    public SellerService(StaxContext db, StaxClient stax)
    {
        _db = db;
        _stax = stax;
    }

    /// <summary>Onboards a seller as a new Stax merchant under the shop's platform account.</summary>
    public async Task<SellerRecord> OnboardAsync(LooseBody body, CancellationToken ct)
    {
        var company = body.Str("company", "companyName", "name")
            ?? throw ApiException.BadRequest("'company' is required.");

        // The contact may be nested ({"contact": {...}}) or flat.
        var contact = body.Raw("contact") is { ValueKind: JsonValueKind.Object } c ? LooseBody.From(c) : body;
        var contactName = contact.Str("name", "contactName", "fullName")
            ?? (body.Raw("contact") is { ValueKind: JsonValueKind.String } s ? s.GetString() : null)
            ?? throw ApiException.BadRequest("The seller's contact name is required.");
        var contactEmail = contact.Str("email", "contactEmail")
            ?? throw ApiException.BadRequest("The seller's contact email is required.");
        if (!InvoiceScheduleService.IsValidEmail(contactEmail))
            throw ApiException.BadRequest("The seller's contact email is not a valid email address.");
        var phone = contact.Str("phone", "contactPhone");

        if (!_stax.HasPlatformKey)
        {
            throw ApiException.Unprocessable(
                "Sellers cannot be onboarded: the shop has no Stax platform account (Stax:PlatformApiKey) configured.");
        }

        var req = new Dictionary<string, object?>
        {
            ["company_name"] = company,
            ["contact_name"] = contactName,
            ["contact_email"] = contactEmail,
            ["reference"] = "eshop-seller",
        };
        if (phone != null) req["contact_phone"] = phone;

        var merchant = await _stax.CreateMerchantAsync(req, ct);
        var seller = new SellerRecord
        {
            StaxMerchantId = merchant["id"]?.GetValue<string>() ?? "",
            Company = company,
            ContactName = contactName,
            ContactEmail = contactEmail,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.Sellers.Add(seller);
        await _db.SaveChangesAsync(ct);
        return seller;
    }
}
