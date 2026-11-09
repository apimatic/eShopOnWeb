using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class SavePaymentMethodRequest
{
    /// <summary>"card" or "bank".</summary>
    public string? Method { get; set; }
    public string? PersonName { get; set; }

    public string? CardNumber { get; set; }
    /// <summary>MMYY or MMYYYY; alternatively use <see cref="CardExpMonth"/> / <see cref="CardExpYear"/>.</summary>
    public string? CardExp { get; set; }
    public int? CardExpMonth { get; set; }
    public int? CardExpYear { get; set; }
    public string? CardCvv { get; set; }
    public string? AddressZip { get; set; }

    public string? BankName { get; set; }
    /// <summary>"checking" or "savings".</summary>
    public string? BankType { get; set; }
    /// <summary>"personal" or "business".</summary>
    public string? BankHolderType { get; set; }
    public string? BankAccount { get; set; }
    public string? BankRouting { get; set; }
}

public class PaymentMethodDto
{
    public string Id { get; set; } = "";
    public string Method { get; set; } = "";
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiration { get; set; }
    public string? BankName { get; set; }
    public string? BankType { get; set; }
    public string? BankHolderType { get; set; }
    public string? PersonName { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}

public class StaffPaymentMethodDto : PaymentMethodDto
{
    public bool Removed { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
}

public class ShopperService
{
    private readonly PaymentsDbContext _db;
    private readonly StaxGateway _stax;

    public ShopperService(PaymentsDbContext db, StaxGateway stax)
    {
        _db = db;
        _stax = stax;
    }

    public static string Normalize(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) throw new PaymentApiException(401, "The caller's identity is missing from the token.");
        return email.Trim().ToLowerInvariant();
    }

    public async Task<ShopperAccount> EnsureAccount(string email, CancellationToken ct = default)
    {
        var account = await _db.ShopperAccounts.FirstOrDefaultAsync(a => a.Email == email, ct);
        if (account != null) return account;

        StaxCustomer customer;
        try { customer = await _stax.FindOrCreateCustomer(email, FirstName(email), null, null, ct); }
        catch (StaxException ex) { throw ex.ToApi("Creating the shopper at Stax"); }

        account = new ShopperAccount { Email = email, StaxCustomerId = customer.Id };
        _db.ShopperAccounts.Add(account);
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // a concurrent request created the account first
            _db.Entry(account).State = EntityState.Detached;
            return await _db.ShopperAccounts.FirstAsync(a => a.Email == email, ct);
        }
        return account;
    }

    public static string FirstName(string email)
    {
        var local = email.Split('@')[0];
        return string.IsNullOrWhiteSpace(local) ? "Shopper" : local;
    }
}

public class PaymentMethodService
{
    private readonly PaymentsDbContext _db;
    private readonly StaxGateway _stax;
    private readonly ShopperService _shoppers;

    public PaymentMethodService(PaymentsDbContext db, StaxGateway stax, ShopperService shoppers)
    {
        _db = db;
        _stax = stax;
        _shoppers = shoppers;
    }

    public static PaymentMethodDto ToDto(StaxPaymentMethod p) => Fill(new PaymentMethodDto(), p);

    private static T Fill<T>(T dto, StaxPaymentMethod p) where T : PaymentMethodDto
    {
        dto.Id = p.Id;
        dto.Method = p.Method;
        dto.Brand = p.IsCard ? p.CardType : null;
        dto.Last4 = p.Last4;
        dto.Expiration = p.IsCard ? FormatExp(p.Expiration) : null;
        dto.BankName = p.IsCard ? null : p.BankName;
        dto.BankType = p.IsCard ? null : p.BankType;
        dto.BankHolderType = p.IsCard ? null : p.BankHolderType;
        dto.PersonName = p.PersonName;
        dto.CreatedAt = p.CreatedAt;
        return dto;
    }

    private static string? FormatExp(string? exp) =>
        exp is { Length: 6 } ? $"{exp[..2]}/{exp[2..]}" : exp;

    public async Task<PaymentMethodDto> Save(string email, SavePaymentMethodRequest r, CancellationToken ct = default)
    {
        var body = Validate(r);
        var account = await _shoppers.EnsureAccount(email, ct);
        body["customer_id"] = account.StaxCustomerId;

        StaxPaymentMethod pm;
        try { pm = await _stax.CreatePaymentMethod(body, ct); }
        catch (StaxException ex) { throw ex.ToApi("Saving the payment method"); }

        _db.SavedPaymentMethods.Add(new SavedPaymentMethod { Email = email, StaxCustomerId = account.StaxCustomerId, StaxPaymentMethodId = pm.Id });
        await _db.SaveChangesAsync(ct);
        return ToDto(pm);
    }

    public async Task<List<PaymentMethodDto>> List(string email, CancellationToken ct = default)
    {
        var account = await _shoppers.EnsureAccount(email, ct);
        try
        {
            var list = await _stax.ListPaymentMethods(account.StaxCustomerId, ct);
            return list.OrderBy(p => p.CreatedAt).Select(ToDto).ToList();
        }
        catch (StaxException ex) { throw ex.ToApi("Listing payment methods"); }
    }

    /// <summary>Loads a payment method the shopper owns and has not removed; 404 otherwise.</summary>
    public async Task<StaxPaymentMethod> GetOwnedActive(string email, string paymentMethodId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(paymentMethodId)) throw new PaymentApiException(400, "A payment method is required.");
        var account = await _shoppers.EnsureAccount(email, ct);
        StaxPaymentMethod? pm;
        try { pm = await _stax.GetPaymentMethod(paymentMethodId, ct); }
        catch (StaxException ex) { throw ex.ToApi("Looking up the payment method"); }
        if (pm == null || !string.Equals(pm.CustomerId, account.StaxCustomerId, StringComparison.OrdinalIgnoreCase))
            throw new PaymentApiException(404, "Payment method not found.");
        if (pm.DeletedAt != null) throw new PaymentApiException(404, "Payment method was removed.");
        return pm;
    }

    public async Task Remove(string email, string paymentMethodId, CancellationToken ct = default)
    {
        var pm = await GetOwnedActive(email, paymentMethodId, ct);
        try { await _stax.DeletePaymentMethod(pm.Id, ct); }
        catch (StaxException ex) { throw ex.ToApi("Removing the payment method"); }
    }

    /// <summary>Everything a shopper has ever saved, removed methods included, for support staff.</summary>
    public async Task<List<StaffPaymentMethodDto>> ForSupport(string email, CancellationToken ct = default)
    {
        var customerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var account = await _db.ShopperAccounts.FirstOrDefaultAsync(a => a.Email == email, ct);
        if (account != null) customerIds.Add(account.StaxCustomerId);
        try
        {
            var found = await _stax.FindCustomerByEmail(email, ct);
            if (found != null) customerIds.Add(found.Id);
        }
        catch (StaxException ex) { throw ex.ToApi("Looking up the shopper"); }
        if (customerIds.Count == 0) throw new PaymentApiException(404, "No shopper with that email has any payment methods.");

        var result = new Dictionary<string, StaffPaymentMethodDto>(StringComparer.OrdinalIgnoreCase);
        try
        {
            // 1. every method the shop ever saved for this shopper, removed or not
            var ids = new HashSet<string>(await _db.SavedPaymentMethods.Where(s => s.Email == email).Select(s => s.StaxPaymentMethodId).ToListAsync(ct),
                StringComparer.OrdinalIgnoreCase);
            foreach (var customerId in customerIds)
            {
                // 2. methods Stax lists as active (saved outside the shop, for instance)
                foreach (var p in await _stax.ListPaymentMethods(customerId, ct)) ids.Add(p.Id);
                // 3. methods used for transactions, which also finds ones removed before the shop tracked them
                foreach (var id in await _stax.PaymentMethodIdsFromTransactions(customerId, email, ct)) ids.Add(id);
            }

            foreach (var id in ids)
            {
                var pm = await _stax.GetPaymentMethod(id, ct);
                if (pm == null || !customerIds.Contains(pm.CustomerId)) continue;
                var dto = Fill(new StaffPaymentMethodDto(), pm);
                dto.Removed = pm.DeletedAt != null;
                dto.RemovedAt = pm.DeletedAt;
                result[pm.Id] = dto;
            }
        }
        catch (StaxException ex) { throw ex.ToApi("Loading the shopper's payment methods"); }

        return result.Values.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).ToList();
    }

    // ---- validation -----------------------------------------------------------------------------------------------

    private static Dictionary<string, object?> Validate(SavePaymentMethodRequest r)
    {
        var method = r.Method?.Trim().ToLowerInvariant();
        if (method != "card" && method != "bank") throw new PaymentApiException(400, "method must be \"card\" or \"bank\".");
        if (string.IsNullOrWhiteSpace(r.PersonName)) throw new PaymentApiException(400, "personName is required.");

        var body = new Dictionary<string, object?> { ["method"] = method, ["person_name"] = r.PersonName.Trim() };
        if (!string.IsNullOrWhiteSpace(r.AddressZip)) body["address_zip"] = r.AddressZip.Trim();

        if (method == "card")
        {
            var number = new string((r.CardNumber ?? "").Where(char.IsDigit).ToArray());
            if (number.Length is < 12 or > 19 || !Luhn(number)) throw new PaymentApiException(400, "cardNumber is not a valid card number.");
            var (month, year) = ParseExp(r);
            var lastDay = new DateTime(year, month, DateTime.DaysInMonth(year, month), 23, 59, 59, DateTimeKind.Utc);
            if (lastDay < DateTime.UtcNow) throw new PaymentApiException(400, "The card has expired.");
            body["card_number"] = number;
            body["card_exp"] = $"{month:00}{year % 100:00}";
            if (!string.IsNullOrWhiteSpace(r.CardCvv))
            {
                if (!r.CardCvv.All(char.IsDigit) || r.CardCvv.Length is < 3 or > 4) throw new PaymentApiException(400, "cardCvv is invalid.");
                body["card_cvv"] = r.CardCvv;
            }
        }
        else
        {
            var account = new string((r.BankAccount ?? "").Where(char.IsDigit).ToArray());
            var routing = new string((r.BankRouting ?? "").Where(char.IsDigit).ToArray());
            if (account.Length is < 4 or > 17) throw new PaymentApiException(400, "bankAccount is not a valid account number.");
            if (!ValidRouting(routing)) throw new PaymentApiException(400, "bankRouting is not a valid routing number.");
            var type = r.BankType?.Trim().ToLowerInvariant();
            if (type != "checking" && type != "savings") throw new PaymentApiException(400, "bankType must be \"checking\" or \"savings\".");
            var holder = r.BankHolderType?.Trim().ToLowerInvariant() ?? "personal";
            if (holder != "personal" && holder != "business") throw new PaymentApiException(400, "bankHolderType must be \"personal\" or \"business\".");
            body["bank_account"] = account;
            body["bank_routing"] = routing;
            body["bank_type"] = type;
            body["bank_holder_type"] = holder;
            body["bank_name"] = string.IsNullOrWhiteSpace(r.BankName) ? "Bank" : r.BankName.Trim();
        }
        return body;
    }

    private static (int month, int year) ParseExp(SavePaymentMethodRequest r)
    {
        int month, year;
        if (r.CardExpMonth.HasValue && r.CardExpYear.HasValue)
        {
            month = r.CardExpMonth.Value;
            year = r.CardExpYear.Value;
        }
        else
        {
            var digits = new string((r.CardExp ?? "").Where(char.IsDigit).ToArray());
            if (digits.Length is not (4 or 6)) throw new PaymentApiException(400, "cardExp must be MMYY or MMYYYY.");
            month = int.Parse(digits[..2], CultureInfo.InvariantCulture);
            year = int.Parse(digits[2..], CultureInfo.InvariantCulture);
        }
        if (year < 100) year += 2000;
        if (month is < 1 or > 12 || year is < 2000 or > 2200) throw new PaymentApiException(400, "The card expiration date is invalid.");
        return (month, year);
    }

    private static bool Luhn(string number)
    {
        var sum = 0;
        var alt = false;
        for (var i = number.Length - 1; i >= 0; i--)
        {
            var d = number[i] - '0';
            if (alt) { d *= 2; if (d > 9) d -= 9; }
            sum += d;
            alt = !alt;
        }
        return sum % 10 == 0;
    }

    private static bool ValidRouting(string r)
    {
        if (r.Length != 9) return false;
        int[] w = { 3, 7, 1, 3, 7, 1, 3, 7, 1 };
        var sum = 0;
        for (var i = 0; i < 9; i++) sum += (r[i] - '0') * w[i];
        return sum % 10 == 0;
    }

    public static void RequireEmail(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || !MailAddress.TryCreate(value.Trim(), out var a) || a.Address != value.Trim())
            throw new PaymentApiException(400, $"{field} must be a valid email address.");
    }
}
