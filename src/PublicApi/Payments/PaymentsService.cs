using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using StaxFattMerchantApi.Core.ErrorResponse;
using StaxFattMerchantApi.Core.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>The shop's payment rules, on top of <see cref="StaxService"/> and the local order store.</summary>
public class PaymentsService
{
    private readonly PaymentsContext _db;
    private readonly StaxService _stax;
    private readonly IReadRepository<CatalogItem> _catalog;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ILogger<PaymentsService> _logger;

    public PaymentsService(PaymentsContext db, StaxService stax, IReadRepository<CatalogItem> catalog,
        UserManager<ApplicationUser> users, ILogger<PaymentsService> logger)
    {
        _db = db;
        _stax = stax;
        _catalog = catalog;
        _users = users;
        _logger = logger;
    }

    // ================================================================ identity

    /// <summary>The caller's email from the JWT; fails when the account no longer exists (closed).</summary>
    public async Task<string> RequireShopper(ClaimsPrincipal principal)
    {
        var name = principal.Identity?.Name
                   ?? principal.FindFirstValue(ClaimTypes.Email)
                   ?? principal.FindFirstValue("email");
        if (string.IsNullOrWhiteSpace(name))
            throw new ApiProblem(401, "The token carries no identity.");
        if (await _users.FindByNameAsync(name) is null)
            throw new ApiProblem(401, "This account is closed.");
        return name.Trim();
    }

    public static string ValidEmail(string? email)
    {
        email = email?.Trim();
        if (string.IsNullOrEmpty(email) || !Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            throw ApiProblem.BadRequest("A valid email address is required.");
        return email;
    }

    // ================================================================ payment methods

    private async Task<Guid> CustomerFor(string email, string? personName, CancellationToken ct)
    {
        using var _ = await KeyedLocks.Acquire("customer:" + email);
        var existing = await _stax.FindCustomers(email, ct);
        if (existing.Count > 0 && existing[0].GuidOf("id") is { } id) return id;

        string? first = null, last = null;
        if (!string.IsNullOrWhiteSpace(personName))
        {
            var parts = personName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            first = parts[0];
            last = parts.Length > 1 ? parts[1] : null;
        }
        var created = await _stax.CreateCustomer(email, first ?? email.Split('@')[0], last, null, ct);
        return created.GuidOf("id") ?? throw new ApiProblem(424, "Stax created the customer but did not return its id.");
    }

    private static string? Pick(JsonElement e, params string[] names)
    {
        foreach (var n in names)
            if (e.Str(n) is { Length: > 0 } s) return s.Trim();
        return null;
    }

    public async Task<object> SavePaymentMethod(string email, JsonElement input, CancellationToken ct)
    {
        if (input.ValueKind != JsonValueKind.Object) throw ApiProblem.BadRequest("A JSON object is required.");
        var kind = (Pick(input, "type", "method", "kind") ?? "").ToLowerInvariant();
        if (kind == "ach" || kind == "bank_account" || kind == "bankaccount") kind = "bank";
        if (kind == "credit_card" || kind == "creditcard" || kind == "credit" || kind == "debit") kind = "card";
        if (kind is not ("card" or "bank"))
            throw ApiProblem.BadRequest("type must be \"card\" or \"bank\".");

        var name = Pick(input, "name", "personName", "person_name", "holderName", "holder_name", "cardholderName", "accountHolderName");
        if (name is null) throw ApiProblem.BadRequest("The name of the card or account holder is required.");
        var zip = Pick(input, "zip", "addressZip", "address_zip", "postalCode", "postal_code", "zipCode");

        var body = new Dictionary<string, object?>
        {
            ["method"] = kind,
            ["person_name"] = name,
        };
        if (zip is not null) body["address_zip"] = zip;
        foreach (var (inKey, outKey) in new[]
                 {
                     ("address1", "address_1"), ("address_1", "address_1"), ("address2", "address_2"), ("address_2", "address_2"),
                     ("city", "address_city"), ("addressCity", "address_city"), ("address_city", "address_city"),
                     ("state", "address_state"), ("addressState", "address_state"), ("address_state", "address_state"),
                     ("country", "address_country"), ("addressCountry", "address_country"), ("address_country", "address_country")
                 })
            if (Pick(input, inKey) is { } v) body[outKey] = v;

        if (kind == "card")
        {
            var number = Regex.Replace(Pick(input, "cardNumber", "card_number", "number", "pan") ?? "", @"[\s-]", "");
            if (!Regex.IsMatch(number, @"^\d{12,19}$")) throw ApiProblem.BadRequest("A valid card number is required.");
            body["card_number"] = number;
            body["card_exp"] = ParseExpiry(input);
            if (Pick(input, "cvv", "cvc", "cardCvv", "card_cvv", "securityCode") is { } cvv) body["card_cvv"] = cvv;
        }
        else
        {
            var account = Pick(input, "accountNumber", "account_number", "bankAccount", "bank_account", "account");
            var routing = Pick(input, "routingNumber", "routing_number", "bankRouting", "bank_routing", "routing");
            if (account is null || !Regex.IsMatch(account, @"^\d{4,17}$")) throw ApiProblem.BadRequest("A valid bank account number is required.");
            if (routing is null || !Regex.IsMatch(routing, @"^\d{9}$")) throw ApiProblem.BadRequest("A valid 9-digit routing number is required.");
            var accountType = (Pick(input, "accountType", "account_type", "bankType", "bank_type") ?? "checking").ToLowerInvariant();
            var holderType = (Pick(input, "holderType", "holder_type", "bankHolderType", "bank_holder_type", "accountHolderType") ?? "personal").ToLowerInvariant();
            if (accountType is not ("checking" or "savings")) throw ApiProblem.BadRequest("accountType must be \"checking\" or \"savings\".");
            if (holderType is not ("personal" or "business")) throw ApiProblem.BadRequest("holderType must be \"personal\" or \"business\".");
            body["bank_account"] = account;
            body["bank_routing"] = routing;
            body["bank_type"] = accountType;
            body["bank_holder_type"] = holderType;
        }

        var customerId = await CustomerFor(email, name, ct);
        body["customer_id"] = customerId;
        var created = await _stax.CreatePaymentMethod(body, ct);
        var id = created.GuidOf("id") ?? throw new ApiProblem(424, "Stax saved the payment method but did not return its id.");

        _db.PaymentMethods.Add(new PaymentMethodRecord { Id = id, BuyerEmail = email, StaxCustomerId = customerId, Method = kind });
        await _db.SaveChangesAsync(ct);
        return PaymentMethodDto(created);
    }

    private static string ParseExpiry(JsonElement input)
    {
        var month = Pick(input, "expMonth", "exp_month", "expiryMonth", "expirationMonth");
        var year = Pick(input, "expYear", "exp_year", "expiryYear", "expirationYear");
        if (month is null || year is null)
        {
            var combined = Pick(input, "expiry", "expiration", "expirationDate", "cardExp", "card_exp", "exp", "expiryDate");
            if (combined is null) throw ApiProblem.BadRequest("The card expiry is required.");
            var m = Regex.Match(combined, @"^(\d{1,2})\s*[/\- ]?\s*(\d{2}|\d{4})$");
            if (!m.Success) throw ApiProblem.BadRequest("The card expiry must look like MM/YY or MM/YYYY.");
            month = m.Groups[1].Value;
            year = m.Groups[2].Value;
        }
        if (!int.TryParse(month, out var mm) || mm is < 1 or > 12 || !int.TryParse(year, out var yy))
            throw ApiProblem.BadRequest("The card expiry is not valid.");
        if (yy < 100) yy += 2000;
        if (new DateTime(yy, mm, 1).AddMonths(1) <= DateTime.UtcNow)
            throw ApiProblem.Unprocessable("The card has expired.");
        return mm.ToString("00", CultureInfo.InvariantCulture) + (yy % 100).ToString("00", CultureInfo.InvariantCulture);
    }

    public async Task<List<object>> ListPaymentMethods(string email, CancellationToken ct)
    {
        var result = new List<object>();
        foreach (var customer in await _stax.FindCustomers(email, ct))
        {
            if (customer.GuidOf("id") is not { } customerId) continue;
            foreach (var pm in await _stax.ListCustomerPaymentMethods(customerId, ct))
                result.Add(PaymentMethodDto(pm));
        }
        return result;
    }

    /// <summary>The payment method when it is live and belongs to this shopper; otherwise a 404.</summary>
    public async Task<JsonElement> OwnedPaymentMethod(string email, Guid paymentMethodId, CancellationToken ct)
    {
        var pm = await _stax.GetPaymentMethod(paymentMethodId, ct);
        if (pm is null || pm.Value.Str("deleted_at") is not null) throw ApiProblem.NotFound("No such payment method.");
        var owner = pm.Value.GuidOf("customer_id");
        var mine = (await _stax.FindCustomers(email, ct)).Select(c => c.GuidOf("id")).ToList();
        if (owner is null || !mine.Contains(owner)) throw ApiProblem.NotFound("No such payment method.");
        return pm.Value;
    }

    public async Task RemovePaymentMethod(string email, Guid paymentMethodId, CancellationToken ct)
    {
        await OwnedPaymentMethod(email, paymentMethodId, ct);
        await _stax.DeletePaymentMethod(paymentMethodId, ct);
    }

    public static object PaymentMethodDto(JsonElement pm)
    {
        var method = pm.Str("method") ?? "card";
        var exp = pm.Str("card_exp");
        string? expiry = null;
        if (method == "card" && exp is { Length: >= 4 })
            expiry = exp.Substring(0, 2) + "/" + (exp.Length == 6 ? exp.Substring(2) : "20" + exp.Substring(2, 2));
        return new
        {
            id = pm.GuidOf("id"),
            paymentMethodId = pm.GuidOf("id"),
            type = method,
            method,
            brand = method == "card" ? pm.Str("card_type") : null,
            lastFour = pm.Str("card_last_four"),
            holderName = pm.Str("person_name"),
            nickname = pm.Str("nickname"),
            expiry,
            accountType = method == "bank" ? pm.Str("bank_type") : null,
            holderType = method == "bank" ? pm.Str("bank_holder_type") : null,
            createdAt = Iso(pm.Time("created_at")),
            removedAt = Iso(pm.Time("deleted_at"))
        };
    }

    public static string? Iso(DateTime? t) => t?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>Support view: everything a shopper ever saved, removed ones included.</summary>
    public async Task<List<object>> SupportPaymentMethods(string email, CancellationToken ct)
    {
        var records = await _db.PaymentMethods.Where(r => r.BuyerEmail == email).ToListAsync(ct);
        var customerIds = (await _stax.FindCustomers(email, ct)).Select(c => c.GuidOf("id")).OfType<Guid>().ToHashSet();
        foreach (var r in records) customerIds.Add(r.StaxCustomerId);
        var all = await _stax.AllPaymentMethods(customerIds, records.Select(r => r.Id), ct);
        return all.OrderBy(p => p.Time("created_at")).Select(PaymentMethodDto).ToList();
    }

    // ================================================================ orders

    public async Task<ShopOrder> PlaceOrder(string email, JsonElement input, CancellationToken ct)
    {
        if (input.ValueKind != JsonValueKind.Object) throw ApiProblem.BadRequest("A JSON object is required.");
        var itemsEl = input.Get("items") ?? input.Get("lines") ?? input.Get("orderItems");
        if (itemsEl is not { ValueKind: JsonValueKind.Array } || itemsEl.Value.GetArrayLength() == 0)
            throw ApiProblem.BadRequest("items must list at least one catalog item.");

        var wanted = new Dictionary<int, int>();
        foreach (var it in itemsEl.Value.EnumerateArray())
        {
            var idText = Pick(it, "catalogItemId", "catalog_item_id", "itemId", "item_id", "productId", "id");
            if (!int.TryParse(idText, out var itemId)) throw ApiProblem.BadRequest("Each item needs a numeric catalogItemId.");
            var qtyText = Pick(it, "quantity", "qty", "units");
            var qty = 1;
            if (qtyText is not null && !int.TryParse(qtyText, out qty)) throw ApiProblem.BadRequest("quantity must be a whole number.");
            if (qty is < 1 or > 1000) throw ApiProblem.BadRequest("quantity must be between 1 and 1000.");
            wanted[itemId] = wanted.GetValueOrDefault(itemId) + qty;
        }

        var order = new ShopOrder { BuyerEmail = email };
        foreach (var (itemId, qty) in wanted)
        {
            var item = await _catalog.GetByIdAsync(itemId, ct);
            if (item is null) throw ApiProblem.NotFound("Catalog item " + itemId + " does not exist.");
            order.Lines.Add(new ShopOrderLine { CatalogItemId = itemId, ItemName = item.Name, UnitPrice = item.Price, Quantity = qty });
        }
        order.Subtotal = Math.Round(order.Lines.Sum(l => l.UnitPrice * l.Quantity), 2);
        if (order.Subtotal <= 0) throw ApiProblem.Unprocessable("The order total must be more than zero.");
        order.Total = order.Subtotal;

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);
        return order;
    }

    public async Task<ShopOrder> OwnedOrder(string email, Guid orderId, CancellationToken ct)
    {
        var order = await _db.Orders.Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.BuyerEmail == email && !o.Anonymized, ct);
        return order ?? throw ApiProblem.NotFound("No such order.");
    }

    public async Task<ShopOrder> AnyOrder(Guid orderId, CancellationToken ct)
    {
        var order = await _db.Orders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == orderId, ct);
        return order ?? throw ApiProblem.NotFound("No such order.");
    }

    // ================================================================ checkout and pay

    public record Quote(decimal Amount, decimal Fee, decimal Total, string Method);

    private async Task<Quote> QuoteFor(ShopOrder order, JsonElement pm, CancellationToken ct)
    {
        var method = pm.Str("method") ?? "card";
        var fee = 0m;
        if (method == "card" && pm.GuidOf("id") is { } pmId)
            fee = await _stax.CardFee(pmId, order.Subtotal, ct);
        return new Quote(order.Subtotal, fee, order.Subtotal + fee, method);
    }

    private static void EnsurePayable(ShopOrder order)
    {
        if (order.RefundStatus != RefundState.NONE && order.RefundStatus != RefundState.FAILED)
            throw ApiProblem.Conflict("This order has been refunded.");
        if (order.PaymentStatus is PaymentState.PAID or PaymentState.PENDING)
            throw ApiProblem.Conflict("This order is already paid.");
        if (order.PaymentStatus == PaymentState.PROCESSING)
            throw ApiProblem.Conflict("A payment for this order is already being processed.");
    }

    public async Task<(ShopOrder Order, Quote Quote)> Checkout(string email, Guid orderId, Guid paymentMethodId, CancellationToken ct)
    {
        var order = await OwnedOrder(email, orderId, ct);
        EnsurePayable(order);
        var pm = await OwnedPaymentMethod(email, paymentMethodId, ct);
        return (order, await QuoteFor(order, pm, ct));
    }

    public async Task<ShopOrder> Pay(string email, Guid orderId, Guid paymentMethodId, CancellationToken ct)
    {
        using var _ = await KeyedLocks.Acquire("order:" + orderId);
        var order = await OwnedOrder(email, orderId, ct);
        EnsurePayable(order);
        var pm = await OwnedPaymentMethod(email, paymentMethodId, ct);
        var quote = await QuoteFor(order, pm, ct);

        order.PaymentStatus = PaymentState.PROCESSING;
        order.PaymentMethodId = paymentMethodId;
        order.PaymentMethodKind = quote.Method;
        order.PaymentError = null;
        await _db.SaveChangesAsync(ct);

        JsonElement txn;
        try
        {
            txn = await _stax.Charge(paymentMethodId, quote.Total, new Dictionary<string, object?>
            {
                ["reference"] = order.Id.ToString(),
                ["orderId"] = order.Id.ToString(),
                ["subtotal"] = quote.Amount,
                ["fee"] = quote.Fee
            }, ct);
        }
        catch (ApiException<RawError> ex)
        {
            // Stax said no: nothing was charged.
            var reason = ApiProblem.DescribeBody(ex.Error.ReadAsString());
            order.PaymentStatus = PaymentState.UNPAID;
            order.PaymentError = reason;
            await _db.SaveChangesAsync(CancellationToken.None);
            var code = (int)ex.StatusCode;
            throw new ApiProblem(code is 400 or 402 or 422 ? 402 : 424, "The payment was declined: " + reason, ex);
        }
        catch
        {
            // The outcome is unknown (timeout, connection loss): release the order, a retry is the shopper's call.
            order.PaymentStatus = PaymentState.UNPAID;
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        order.StaxTransactionId = txn.GuidOf("id");
        order.PaymentSubmittedAt = DateTime.UtcNow;
        if (txn.Bool("success") == false)
        {
            order.PaymentStatus = PaymentState.UNPAID;
            order.PaymentError = txn.Str("message") ?? txn.Str("error_description") ?? "declined";
            await _db.SaveChangesAsync(CancellationToken.None);
            throw new ApiProblem(402, "The payment was declined: " + order.PaymentError);
        }

        // What Stax actually charged is what the order shows.
        order.Fee = quote.Fee;
        order.Total = txn.Dec("total") ?? quote.Total;
        order.Fee = Math.Max(0m, order.Total - order.Subtotal);
        ApplyTransaction(order, txn);
        await _db.SaveChangesAsync(CancellationToken.None);
        return order;
    }

    // ================================================================ payment status

    /// <summary>Settles the order's payment state from what Stax says about its transaction.</summary>
    public static void ApplyTransaction(ShopOrder order, JsonElement txn)
    {
        var status = (txn.Str("status") ?? "").ToUpperInvariant();
        var failed = txn.Bool("success") == false
                     || status is "FAILED" or "DECLINED" or "RETURNED" or "ERROR" or "REJECTED" or "VOIDED";
        if (failed)
        {
            if (order.PaymentStatus != PaymentState.PAID)
            {
                order.PaymentStatus = PaymentState.FAILED;
                order.PaymentError = txn.Str("message") ?? txn.Str("error_description") ?? "the payment did not go through";
            }
            return;
        }

        var isBank = (order.PaymentMethodKind ?? txn.Str("method")) == "bank";
        var settled = !isBank
                      || txn.Str("settled_at") is not null
                      || status == "SETTLED"
                      || txn.Bool("is_refundable") == true;
        if (settled)
        {
            order.PaymentStatus = PaymentState.PAID;
            order.PaidAt ??= txn.Time("settled_at") ?? DateTime.UtcNow;
            order.PaymentError = null;
        }
        else
        {
            order.PaymentStatus = PaymentState.PENDING;
        }
    }

    /// <summary>Brings a pending bank payment up to date; safe to call any time.</summary>
    public async Task<bool> SyncPayment(ShopOrder order, CancellationToken ct)
    {
        if (order.PaymentStatus != PaymentState.PENDING || order.StaxTransactionId is not { } txnId) return false;
        using var _ = await KeyedLocks.Acquire("order:" + order.Id);
        await _db.Entry(order).ReloadAsync(ct);
        if (order.PaymentStatus != PaymentState.PENDING) return true;
        var txn = await _stax.GetTransaction(txnId, ct);
        ApplyTransaction(order, txn);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ================================================================ refunds

    private static bool TooEarlyForBank(string message) =>
        Regex.IsMatch(message, @"unavailable until|business day|not (yet )?settled|settle", RegexOptions.IgnoreCase);

    public async Task<ShopOrder> RequestRefund(Guid orderId, string staff, CancellationToken ct)
    {
        using var _ = await KeyedLocks.Acquire("order:" + orderId);
        var order = await AnyOrder(orderId, ct);
        if (order.StaxTransactionId is null || order.PaymentStatus is PaymentState.UNPAID or PaymentState.PROCESSING)
            throw ApiProblem.Conflict("This order has no payment to refund.");
        if (order.RefundStatus is RefundState.PENDING or RefundState.REFUNDED)
            throw ApiProblem.Conflict(order.RefundStatus == RefundState.REFUNDED
                ? "This order has already been refunded."
                : "A refund for this order is already in progress.");
        if (order.PaymentStatus == PaymentState.FAILED)
            throw ApiProblem.Conflict("The payment for this order did not go through, so there is nothing to refund.");

        order.RefundRequestedAt = DateTime.UtcNow;
        order.RefundRequestedBy = staff;
        order.RefundAmount = order.Total;
        order.RefundStatus = RefundState.PENDING;
        order.RefundNote = null;
        order.RefundAttempts = 0;

        try
        {
            await AttemptRefund(order, ct);
        }
        catch
        {
            // The refund was refused outright: nothing is owed or queued.
            order.RefundStatus = RefundState.NONE;
            order.RefundRequestedAt = null;
            order.RefundRequestedBy = null;
            order.RefundAmount = null;
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        await _db.SaveChangesAsync(ct);
        return order;
    }

    /// <summary>
    /// Asks Stax to give the whole amount back. A bank payment cannot be reversed for the first business days:
    /// the order then stays "pending" and the background sync retries until Stax accepts.
    /// </summary>
    private async Task AttemptRefund(ShopOrder order, CancellationToken ct)
    {
        var txnId = order.StaxTransactionId!.Value;
        order.RefundAttempts++;

        var txn = await _stax.GetTransaction(txnId, ct);
        if ((txn.Dec("total_refunded") ?? 0m) >= order.Total)
        {
            MarkRefunded(order);
            return;
        }

        try
        {
            var after = await _stax.Refund(txnId, ct);
            if ((after.Dec("total_refunded") ?? 0m) >= order.Total) MarkRefunded(order);
            else Postpone(order, "Stax accepted the refund but has not returned the whole amount yet.");
        }
        catch (ApiException<RawError> ex)
        {
            var reason = ApiProblem.DescribeBody(ex.Error.ReadAsString());
            var isBank = (order.PaymentMethodKind ?? "") == "bank";
            if (isBank && (TooEarlyForBank(reason) || order.PaymentStatus == PaymentState.PENDING))
            {
                Postpone(order, reason);
                return;
            }

            if (!isBank)
            {
                // A card payment that has not settled yet is voided instead of refunded.
                try
                {
                    await _stax.Void(txnId, ct);
                    MarkRefunded(order);
                    return;
                }
                catch (ApiException<RawError>)
                {
                }
            }

            throw ApiProblem.FromException(ex);
        }
    }

    private static void MarkRefunded(ShopOrder order)
    {
        order.RefundStatus = RefundState.REFUNDED;
        order.RefundedAt = DateTime.UtcNow;
        order.RefundNextAttemptAt = null;
        order.RefundNote = null;
    }

    private static void Postpone(ShopOrder order, string note)
    {
        order.RefundStatus = RefundState.PENDING;
        order.RefundNote = note;
        order.RefundNextAttemptAt = DateTime.UtcNow.AddMinutes(10);
    }

    /// <summary>Retries a queued refund (background sync).</summary>
    public async Task RetryRefund(ShopOrder order, CancellationToken ct)
    {
        if (order.RefundStatus != RefundState.PENDING || order.StaxTransactionId is null) return;
        if (order.RefundNextAttemptAt is { } due && due > DateTime.UtcNow) return;
        using var _ = await KeyedLocks.Acquire("order:" + order.Id);
        await _db.Entry(order).ReloadAsync(ct);
        if (order.RefundStatus != RefundState.PENDING) return;
        try
        {
            await AttemptRefund(order, ct);
        }
        catch (ApiProblem p) when (p.Status != 424)
        {
            order.RefundStatus = RefundState.FAILED;
            order.RefundNote = p.Message;
            order.RefundNextAttemptAt = null;
        }
        catch (Exception ex) when (ApiProblem.IsStaxFailure(ex))
        {
            order.RefundNextAttemptAt = DateTime.UtcNow.AddMinutes(10);
            order.RefundNote = ApiProblem.FromException(ex).Message;
        }
        await _db.SaveChangesAsync(ct);
    }

    // ================================================================ order views

    public static object OrderDto(ShopOrder o, bool staff = false)
    {
        var paid = o.PaymentStatus == PaymentState.PAID;
        var status = o.RefundStatus == RefundState.REFUNDED ? "refunded"
            : o.RefundStatus == RefundState.PENDING ? "refund_pending"
            : o.PaymentStatus switch
            {
                PaymentState.PAID => "paid",
                PaymentState.PENDING => "payment_pending",
                PaymentState.FAILED => "payment_failed",
                PaymentState.PROCESSING => "processing",
                _ => "awaiting_payment"
            };
        var items = o.Lines.OrderBy(l => l.Id).Select(l => new
        {
            catalogItemId = l.CatalogItemId,
            name = l.ItemName,
            unitPrice = l.UnitPrice,
            quantity = l.Quantity,
            lineTotal = l.UnitPrice * l.Quantity
        }).ToList();

        var common = new Dictionary<string, object?>
        {
            ["id"] = o.Id,
            ["orderId"] = o.Id,
            ["status"] = status,
            ["paid"] = paid,
            ["paymentStatus"] = o.PaymentStatus,
            ["items"] = items,
            ["subtotal"] = o.Subtotal,
            ["fee"] = o.Fee,
            ["total"] = o.Total,
            ["currency"] = o.Currency,
            ["paymentMethodId"] = o.PaymentMethodId,
            ["paymentMethod"] = o.PaymentMethodKind,
            ["createdAt"] = Iso(o.CreatedAt),
            ["paidAt"] = Iso(o.PaidAt),
            ["refundStatus"] = o.RefundStatus
        };
        if (staff)
        {
            common["buyerEmail"] = o.BuyerEmail;
            common["transactionId"] = o.StaxTransactionId;
            common["paymentError"] = o.PaymentError;
            common["refundAmount"] = o.RefundAmount;
            common["refundRequestedAt"] = Iso(o.RefundRequestedAt);
            common["refundRequestedBy"] = o.RefundRequestedBy;
            common["refundedAt"] = Iso(o.RefundedAt);
            common["refundNote"] = o.RefundNote;
        }
        return common;
    }

    // ================================================================ closing an account

    public async Task CloseAccount(string email, CancellationToken ct)
    {
        using var _ = await KeyedLocks.Acquire("close:" + email);
        var orders = await _db.Orders.Where(o => o.BuyerEmail == email).ToListAsync(ct);
        if (orders.Any(o => o.PaymentStatus == PaymentState.PROCESSING))
            throw ApiProblem.Conflict("A payment is being processed; try again in a moment.");

        // 1. Stax: every payment method (live or removed) loses its personal details and is removed,
        //    then the customer record is blanked and deleted.
        var records = await _db.PaymentMethods.Where(r => r.BuyerEmail == email).ToListAsync(ct);
        var customers = await _stax.FindCustomers(email, ct);
        var customerIds = customers.Select(c => c.GuidOf("id")).OfType<Guid>().ToHashSet();
        foreach (var r in records) customerIds.Add(r.StaxCustomerId);

        var methods = await _stax.AllPaymentMethods(customerIds, records.Select(r => r.Id), ct);
        foreach (var pm in methods)
        {
            if (pm.GuidOf("id") is not { } pmId) continue;
            if (pm.Str("deleted_at") is null)
            {
                await _stax.ScrubPaymentMethod(pmId, ct);
                await _stax.DeletePaymentMethod(pmId, ct);
            }
            else
            {
                // Stax no longer lets anyone edit a removed payment method; scrubbing it is best effort.
                try
                {
                    await _stax.ScrubPaymentMethod(pmId, ct);
                }
                catch (ApiException<RawError> ex) when ((int)ex.StatusCode is 404 or 422)
                {
                }
            }
        }

        var schedules = (await _stax.ListInvoiceSchedules(ct))
            .Where(s => s.GuidOf("customer_id") is { } c && customerIds.Contains(c));
        foreach (var s in schedules)
            if (s.GuidOf("id") is { } sid) await _stax.DeleteInvoiceSchedule(sid, ct);

        foreach (var customer in customers)
            if (customer.GuidOf("id") is { } cid) await _stax.EraseCustomer(cid, ct);

        // 2. The shop: payment records go; orders go too, except ones whose money is still in flight,
        //    which stay as anonymous ledger entries so the sync can finish them.
        _db.PaymentMethods.RemoveRange(records);
        foreach (var o in orders)
        {
            var inFlight = o.PaymentStatus == PaymentState.PENDING || o.RefundStatus == RefundState.PENDING;
            if (inFlight)
            {
                o.BuyerEmail = "closed-" + Guid.NewGuid().ToString("N");
                o.Anonymized = true;
                o.PaymentMethodId = null;
            }
            else
            {
                _db.Orders.Remove(o);
            }
        }
        await _db.SaveChangesAsync(ct);

        var user = await _users.FindByNameAsync(email);
        if (user is not null)
        {
            var deleted = await _users.DeleteAsync(user);
            if (!deleted.Succeeded)
                throw new ApiProblem(409, "The account could not be closed: " + string.Join("; ", deleted.Errors.Select(e => e.Description)));
        }
    }
}
