using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// The shop's payment rules: saved payment methods, orders, checkout, paying, refunds, reconciliation and account closing.
/// </summary>
public class PaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();

    private readonly CatalogContext _db;
    private readonly StaxGateway _stax;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(CatalogContext db, StaxGateway stax, UserManager<ApplicationUser> users, ILogger<PaymentService> logger)
    {
        _db = db;
        _stax = stax;
        _users = users;
        _logger = logger;
    }

    // ================================================================ identity

    /// <summary>The shopper behind the JWT. The JWT carries the account's user name (its e-mail); the account must still exist.</summary>
    public async Task<string> RequireShopperAsync(ClaimsPrincipal principal)
    {
        var name = principal.FindFirstValue(ClaimTypes.Name) ?? principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new PaymentException((int)HttpStatusCode.Unauthorized, "The token does not identify a shopper.");
        }
        if (await _users.FindByNameAsync(name) == null)
        {
            throw new PaymentException((int)HttpStatusCode.Unauthorized, "This shop account does not exist (it may have been closed).");
        }
        return name;
    }

    private static string Key(string email) => email.Trim().ToLowerInvariant();

    private async Task<ShopperProfile?> FindProfileAsync(string email, CancellationToken ct)
    {
        var key = Key(email);
        return await _db.ShopperProfiles.FirstOrDefaultAsync(p => p.Email == key, ct);
    }

    private async Task<ShopperProfile> EnsureProfileAsync(string email, string? personName, CancellationToken ct)
    {
        var profile = await FindProfileAsync(email, ct);
        if (profile != null) return profile;

        var existing = (await _stax.FindCustomersByEmail(email, ct)).FirstOrDefault();
        string customerId;
        if (existing.ValueKind == JsonValueKind.Object)
        {
            customerId = StaxGateway.Str(existing, "id")!;
        }
        else
        {
            var (first, last) = SplitName(personName, email);
            var created = await _stax.CreateCustomer(email, first, last, null, ct);
            customerId = StaxGateway.Str(created, "id")!;
        }
        profile = new ShopperProfile { Email = Key(email), StaxCustomerId = customerId };
        _db.ShopperProfiles.Add(profile);
        await _db.SaveChangesAsync(ct);
        return profile;
    }

    private static (string, string) SplitName(string? personName, string email)
    {
        var name = string.IsNullOrWhiteSpace(personName) ? email.Split('@')[0] : personName!.Trim();
        var parts = name.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 ? (parts[0], parts[1]) : (parts.Length == 1 ? parts[0] : "Shopper", "Shopper");
    }

    // ================================================================ payment methods

    public static PaymentMethodDto ToDto(JsonElement pm, bool includeRemoved = false)
    {
        var method = StaxGateway.Str(pm, "method");
        var isBank = string.Equals(method, "bank", StringComparison.OrdinalIgnoreCase);
        var meta = pm.ValueKind == JsonValueKind.Object && pm.TryGetProperty("meta", out var m) ? m : default;
        return new PaymentMethodDto
        {
            Id = StaxGateway.Str(pm, "id") ?? string.Empty,
            Type = isBank ? "bank" : "card",
            Brand = isBank ? null : StaxGateway.Str(pm, "card_type"),
            Last4 = StaxGateway.Str(pm, "card_last_four") ?? (meta.ValueKind == JsonValueKind.Object ? StaxGateway.Str(meta, "accountDisplay") : null),
            Nickname = StaxGateway.Str(pm, "nickname"),
            PersonName = StaxGateway.Str(pm, "person_name"),
            CardExp = isBank ? null : (StaxGateway.Str(pm, "card_exp") is { Length: 6 } exp ? exp.Substring(0, 2) + exp.Substring(4, 2) : StaxGateway.Str(pm, "card_exp")),
            BankType = isBank ? StaxGateway.Str(pm, "bank_type") : null,
            BankHolderType = isBank ? StaxGateway.Str(pm, "bank_holder_type") : null,
            CreatedAt = StaxGateway.Str(pm, "created_at"),
            RemovedAt = includeRemoved ? StaxGateway.Str(pm, "deleted_at") : null,
        };
    }

    public async Task<PaymentMethodDto> SavePaymentMethodAsync(string email, SavePaymentMethodRequest request, CancellationToken ct)
    {
        var type = (request.Type ?? request.Method ?? string.Empty).Trim().ToLowerInvariant();
        if (type == "ach") type = "bank";
        if (type != "card" && type != "bank")
        {
            throw PaymentException.BadRequest("type must be 'card' or 'bank'.");
        }
        if (string.IsNullOrWhiteSpace(request.PersonName))
        {
            throw PaymentException.BadRequest("personName is required.");
        }

        var body = new Dictionary<string, object?> { ["method"] = type, ["person_name"] = request.PersonName!.Trim() };
        if (type == "card")
        {
            var number = new string((request.CardNumber ?? string.Empty).Where(char.IsDigit).ToArray());
            if (number.Length < 12) throw PaymentException.BadRequest("cardNumber is required.");
            var exp = NormalizeExpiry(request);
            if (exp == null) throw PaymentException.BadRequest("The card expiry is required as cardExp (MMYY) or expMonth and expYear.");
            body["card_number"] = number;
            body["card_exp"] = exp;
            if (!string.IsNullOrWhiteSpace(request.Cvv)) body["card_cvv"] = request.Cvv!.Trim();
            if (!string.IsNullOrWhiteSpace(request.AddressZip)) body["address_zip"] = request.AddressZip!.Trim();
        }
        else
        {
            var account = new string((request.BankAccount ?? string.Empty).Where(char.IsDigit).ToArray());
            var routing = new string((request.BankRouting ?? string.Empty).Where(char.IsDigit).ToArray());
            if (account.Length == 0) throw PaymentException.BadRequest("bankAccount is required.");
            if (routing.Length != 9) throw PaymentException.BadRequest("bankRouting must be a 9 digit routing number.");
            var bankType = (request.BankType ?? "checking").Trim().ToLowerInvariant();
            var holder = (request.BankHolderType ?? "personal").Trim().ToLowerInvariant();
            if (bankType != "checking" && bankType != "savings") throw PaymentException.BadRequest("bankType must be 'checking' or 'savings'.");
            if (holder != "personal" && holder != "business") throw PaymentException.BadRequest("bankHolderType must be 'personal' or 'business'.");
            body["bank_account"] = account;
            body["bank_routing"] = routing;
            body["bank_type"] = bankType;
            body["bank_holder_type"] = holder;
        }

        var profile = await EnsureProfileAsync(email, request.PersonName, ct);
        body["customer_id"] = profile.StaxCustomerId;
        var created = await _stax.CreatePaymentMethod(body, ct);
        return ToDto(created);
    }

    private static string? NormalizeExpiry(SavePaymentMethodRequest r)
    {
        string? month = null, year = null;
        if (!string.IsNullOrWhiteSpace(r.CardExp))
        {
            var digits = new string(r.CardExp.Where(char.IsDigit).ToArray());
            if (digits.Length == 4) { month = digits.Substring(0, 2); year = digits.Substring(2, 2); }
            else if (digits.Length == 6) { month = digits.Substring(0, 2); year = digits.Substring(4, 2); }
        }
        else if (!string.IsNullOrWhiteSpace(r.ExpMonth) && !string.IsNullOrWhiteSpace(r.ExpYear))
        {
            month = r.ExpMonth!.Trim().PadLeft(2, '0');
            year = r.ExpYear!.Trim();
            if (year.Length == 4) year = year.Substring(2);
        }
        if (month == null || year == null || month.Length != 2 || year.Length != 2) return null;
        return month + year;
    }

    public async Task<List<PaymentMethodDto>> ListPaymentMethodsAsync(string email, CancellationToken ct)
    {
        var profile = await FindProfileAsync(email, ct);
        if (profile == null) return new List<PaymentMethodDto>();
        var methods = await _stax.ListActivePaymentMethods(profile.StaxCustomerId, ct);
        return methods.Select(m => ToDto(m)).ToList();
    }

    /// <summary>A saved, still usable payment method that belongs to this shopper.</summary>
    private async Task<JsonElement> RequireOwnPaymentMethodAsync(string email, string paymentMethodId, CancellationToken ct)
    {
        var profile = await FindProfileAsync(email, ct);
        if (profile == null) throw PaymentException.NotFound("Payment method not found.");
        JsonElement pm;
        try
        {
            pm = await _stax.GetPaymentMethod(paymentMethodId, ct);
        }
        catch (PaymentException ex) when (ex.StatusCode == 404)
        {
            throw PaymentException.NotFound("Payment method not found.");
        }
        if (!string.Equals(StaxGateway.Str(pm, "customer_id"), profile.StaxCustomerId, StringComparison.OrdinalIgnoreCase)
            || StaxGateway.Str(pm, "deleted_at") != null)
        {
            throw PaymentException.NotFound("Payment method not found.");
        }
        return pm;
    }

    public async Task DeletePaymentMethodAsync(string email, string paymentMethodId, CancellationToken ct)
    {
        var pm = await RequireOwnPaymentMethodAsync(email, paymentMethodId, ct);
        await _stax.DeletePaymentMethod(StaxGateway.Str(pm, "id")!, ct);
    }

    public async Task<ShopperPaymentMethodsResponse> ListPaymentMethodsForSupportAsync(string email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email)) throw PaymentException.BadRequest("email is required.");
        var customerIds = new List<string>();
        var profile = await FindProfileAsync(email, ct);
        if (profile != null) customerIds.Add(profile.StaxCustomerId);
        foreach (var c in await _stax.FindCustomersByEmail(email.Trim(), ct))
        {
            var id = StaxGateway.Str(c, "id");
            if (id != null && !customerIds.Contains(id, StringComparer.OrdinalIgnoreCase)) customerIds.Add(id);
        }
        if (customerIds.Count == 0) throw PaymentException.NotFound($"No shopper with e-mail '{email}' was found.");

        var response = new ShopperPaymentMethodsResponse { Email = email.Trim() };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var customerId in customerIds)
        {
            foreach (var pm in await _stax.ListAllPaymentMethods(customerId, ct))
            {
                var dto = ToDto(pm, includeRemoved: true);
                if (seen.Add(dto.Id)) response.PaymentMethods.Add(dto);
            }
        }
        response.PaymentMethods = response.PaymentMethods.OrderBy(p => p.CreatedAt, StringComparer.Ordinal).ToList();
        return response;
    }

    // ================================================================ orders

    public async Task<OrderDto> CreateOrderAsync(string email, CreateOrderRequest request, CancellationToken ct)
    {
        if (request.Items == null || request.Items.Count == 0) throw PaymentException.BadRequest("An order needs at least one item.");
        var wanted = new Dictionary<int, int>();
        foreach (var item in request.Items)
        {
            if (item.Quantity < 1 || item.Quantity > 1000) throw PaymentException.BadRequest("quantity must be between 1 and 1000.");
            wanted[item.CatalogItemId] = wanted.GetValueOrDefault(item.CatalogItemId) + item.Quantity;
        }
        var ids = wanted.Keys.ToList();
        var catalog = await _db.CatalogItems.Where(c => ids.Contains(c.Id)).ToListAsync(ct);
        var missing = ids.Where(i => catalog.All(c => c.Id != i)).ToList();
        if (missing.Count > 0) throw PaymentException.NotFound($"Catalog item(s) not found: {string.Join(", ", missing)}.");

        var lines = catalog.Select(c => new OrderItem(
            new CatalogItemOrdered(c.Id, c.Name, string.IsNullOrWhiteSpace(c.PictureUri) ? "none" : c.PictureUri),
            c.Price, wanted[c.Id])).ToList();
        var a = request.ShipToAddress;
        var address = new Address(a?.Street ?? string.Empty, a?.City ?? string.Empty, a?.State ?? string.Empty, a?.Country ?? string.Empty, a?.ZipCode ?? string.Empty);
        var order = new Order(email, address, lines);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);
        return BuildOrderDto(order, null, new OrderDto());
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _db.Orders.Include(o => o.OrderItems).FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order == null) throw PaymentException.NotFound($"Order {orderId} not found.");
        return order;
    }

    private async Task<Order> LoadOwnOrderAsync(string email, int orderId, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);
        if (!string.Equals(order.BuyerId, email, StringComparison.OrdinalIgnoreCase))
        {
            throw PaymentException.NotFound($"Order {orderId} not found.");
        }
        return order;
    }

    private static T BuildOrderDto<T>(Order order, OrderPayment? payment, T dto) where T : OrderDto
    {
        dto.OrderId = order.Id;
        dto.OrderDate = order.OrderDate;
        dto.BuyerId = order.BuyerId;
        dto.Items = order.OrderItems.Select(i => new OrderItemDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units,
        }).ToList();
        dto.Total = order.Total();
        if (payment == null)
        {
            dto.PaymentStatus = "unpaid";
            dto.Paid = false;
            dto.RefundStatus = RefundStatuses.None;
            return dto;
        }
        dto.Paid = payment.Status == PaymentStatuses.Paid;
        dto.PaymentStatus = payment.Status;
        dto.PaymentMethodId = payment.PaymentMethodId;
        dto.PaymentMethodType = payment.MethodType;
        dto.Fee = payment.Fee;
        dto.AmountCharged = payment.ChargedTotal;
        dto.PaidAt = payment.PaidAt;
        dto.PaymentError = payment.Status == PaymentStatuses.Failed ? payment.LastError : null;
        dto.RefundStatus = payment.RefundStatus;
        return dto;
    }

    private Task<OrderPayment?> FindPaymentAsync(int orderId, CancellationToken ct) =>
        _db.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == orderId, ct);

    public async Task<OrderDto> GetOrderAsync(string email, int orderId, CancellationToken ct)
    {
        var order = await LoadOwnOrderAsync(email, orderId, ct);
        var payment = await FindPaymentAsync(orderId, ct);
        if (payment != null) await TryReconcileAsync(payment, ct);
        return BuildOrderDto(order, payment, new OrderDto());
    }

    public async Task<AdminOrderDto> GetOrderForStaffAsync(int orderId, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);
        var payment = await FindPaymentAsync(orderId, ct);
        if (payment != null) await TryReconcileAsync(payment, ct);
        var dto = BuildOrderDto(order, payment, new AdminOrderDto());
        if (payment != null)
        {
            dto.StaxTransactionId = payment.StaxTransactionId;
            dto.RefundTransactionId = payment.RefundTransactionId;
            dto.RefundRequestedAt = payment.RefundRequestedAt;
            dto.RefundedAt = payment.RefundedAt;
            dto.RefundError = payment.RefundStatus == RefundStatuses.Pending ? payment.RefundLastError : null;
            dto.PaymentError = payment.LastError;
        }
        return dto;
    }

    // ================================================================ checkout & pay

    private async Task<(Order order, JsonElement pm, string type, decimal subtotal, decimal fee)> PrepareAsync(
        string email, int orderId, string? paymentMethodId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(paymentMethodId)) throw PaymentException.BadRequest("paymentMethodId is required.");
        var order = await LoadOwnOrderAsync(email, orderId, ct);
        var subtotal = order.Total();
        if (subtotal <= 0) throw PaymentException.Unprocessable("This order has nothing to pay.");
        var pm = await RequireOwnPaymentMethodAsync(email, paymentMethodId!, ct);
        var type = string.Equals(StaxGateway.Str(pm, "method"), "bank", StringComparison.OrdinalIgnoreCase) ? "bank" : "card";
        // Only card payments carry the shop's passed-on processing fee.
        var fee = type == "card" ? await _stax.GetSurcharge(StaxGateway.Str(pm, "id")!, subtotal, ct) : 0m;
        return (order, pm, type, subtotal, fee);
    }

    private static void EnsureNotAlreadyPaid(OrderPayment? payment)
    {
        if (payment == null) return;
        if (payment.RefundStatus != RefundStatuses.None) throw PaymentException.Conflict("This order has been refunded.");
        if (payment.Status == PaymentStatuses.Paid) throw PaymentException.Conflict("This order is already paid.");
        if (payment.Status == PaymentStatuses.Processing) throw PaymentException.Conflict("A payment for this order is already being processed by the bank.");
    }

    public async Task<CheckoutResponse> CheckoutAsync(string email, int orderId, string? paymentMethodId, CancellationToken ct)
    {
        var (order, pm, type, subtotal, fee) = await PrepareAsync(email, orderId, paymentMethodId, ct);
        EnsureNotAlreadyPaid(await FindPaymentAsync(orderId, ct));
        return new CheckoutResponse
        {
            OrderId = order.Id,
            PaymentMethodId = StaxGateway.Str(pm, "id")!,
            PaymentMethodType = type,
            Subtotal = subtotal,
            Fee = fee,
            Total = subtotal + fee,
        };
    }

    public async Task<PayResponse> PayAsync(string email, int orderId, string? paymentMethodId, CancellationToken ct)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var (order, pm, type, subtotal, fee) = await PrepareAsync(email, orderId, paymentMethodId, ct);
            var payment = await FindPaymentAsync(orderId, ct);
            EnsureNotAlreadyPaid(payment);

            var pmId = StaxGateway.Str(pm, "id")!;
            var tx = await _stax.Charge(pmId, subtotal, $"eShopOnWeb order {order.Id}", ct);
            var txId = StaxGateway.Str(tx, "id") ?? string.Empty;
            var success = StaxGateway.Bool(tx, "success") ?? false;
            if (!success)
            {
                var message = StaxGateway.Str(tx, "message");
                if (string.IsNullOrWhiteSpace(message) && tx.TryGetProperty("response", out var resp)) message = StaxGateway.Str(resp, "message");
                throw new PaymentException(402, "The payment was declined" + (string.IsNullOrWhiteSpace(message) ? "." : ": " + message));
            }

            if (payment == null)
            {
                payment = new OrderPayment { OrderId = order.Id, BuyerId = order.BuyerId };
                _db.OrderPayments.Add(payment);
            }
            payment.PaymentMethodId = pmId;
            payment.MethodType = type;
            payment.Subtotal = subtotal;
            payment.Fee = fee;
            payment.ChargedTotal = subtotal + fee;
            payment.StaxTransactionId = txId;
            payment.LastError = null;
            payment.CreatedAt = DateTimeOffset.UtcNow;
            // A card is authorised on the spot; a bank payment goes through only once the bank settles it.
            ApplyTransaction(payment, tx);
            await _db.SaveChangesAsync(ct);

            return new PayResponse
            {
                OrderId = order.Id,
                PaymentMethodId = pmId,
                PaymentMethodType = type,
                Subtotal = subtotal,
                Fee = fee,
                Total = subtotal + fee,
                Paid = payment.Status == PaymentStatuses.Paid,
                PaymentStatus = payment.Status,
                TransactionId = txId,
            };
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Sets the payment's status from the Stax transaction. Returns true when something changed.</summary>
    private static bool ApplyTransaction(OrderPayment payment, JsonElement tx)
    {
        var before = payment.Status;
        var success = StaxGateway.Bool(tx, "success") ?? true;
        var voided = StaxGateway.Bool(tx, "is_voided") ?? false;
        var status = (StaxGateway.Str(tx, "status") ?? string.Empty).ToUpperInvariant();
        var failedStatus = status.Contains("FAIL") || status.Contains("RETURN") || status.Contains("DECLIN") || status.Contains("REJECT");
        var isBank = payment.MethodType == "bank";
        var settled = StaxGateway.Str(tx, "settled_at") != null;

        if (!success || failedStatus || (voided && payment.RefundStatus == RefundStatuses.None))
        {
            payment.Status = PaymentStatuses.Failed;
            payment.LastError = StaxGateway.Str(tx, "error_description") ?? StaxGateway.Str(tx, "message") ?? "The payment did not go through.";
        }
        else if (!isBank || settled)
        {
            payment.Status = PaymentStatuses.Paid;
            payment.PaidAt ??= DateTimeOffset.UtcNow;
        }
        else
        {
            payment.Status = PaymentStatuses.Processing;
        }
        return before != payment.Status;
    }

    // ================================================================ reconciliation

    private async Task TryReconcileAsync(OrderPayment payment, CancellationToken ct)
    {
        try
        {
            await ReconcileAsync(payment, ct);
        }
        catch (Exception ex) when (ex is PaymentException)
        {
            _logger.LogWarning(ex, "Could not refresh payment of order {OrderId} from Stax", payment.OrderId);
        }
    }

    /// <summary>Brings one payment up to date with Stax: bank settlement, and refunds that Stax could not take yet.</summary>
    public async Task ReconcileAsync(OrderPayment payment, CancellationToken ct)
    {
        if (payment.Status == PaymentStatuses.Processing && !string.IsNullOrEmpty(payment.StaxTransactionId))
        {
            var tx = await _stax.GetTransaction(payment.StaxTransactionId, ct);
            if (ApplyTransaction(payment, tx))
            {
                _logger.LogInformation("Payment of order {OrderId} is now {Status}", payment.OrderId, payment.Status);
            }
            if (payment.Status == PaymentStatuses.Failed && payment.RefundStatus == RefundStatuses.Pending)
            {
                payment.RefundStatus = RefundStatuses.NotRequired;
            }
            await _db.SaveChangesAsync(ct);
        }

        if (payment.RefundStatus == RefundStatuses.Pending)
        {
            await AttemptRefundAsync(payment, throwOnError: false, ct);
        }
    }

    public async Task ReconcileAllAsync(CancellationToken ct)
    {
        var open = await _db.OrderPayments
            .Where(p => p.Status == PaymentStatuses.Processing || p.RefundStatus == RefundStatuses.Pending)
            .ToListAsync(ct);
        foreach (var payment in open)
        {
            try
            {
                await ReconcileAsync(payment, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Reconciling order {OrderId} failed", payment.OrderId);
            }
        }
    }

    // ================================================================ refunds

    public async Task<RefundResponse> RefundOrderAsync(int orderId, CancellationToken ct)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            await LoadOrderAsync(orderId, ct);
            var payment = await FindPaymentAsync(orderId, ct);
            if (payment == null) throw PaymentException.Conflict("This order has no payment to refund.");
            if (payment.RefundStatus == RefundStatuses.Refunded) throw PaymentException.Conflict("This order has already been refunded.");
            if (payment.RefundStatus == RefundStatuses.Pending) throw PaymentException.Conflict("A refund for this order has already been requested and is pending.");
            await TryReconcileAsync(payment, ct);
            if (payment.Status == PaymentStatuses.Failed) throw PaymentException.Conflict("This order's payment did not go through, so there is nothing to refund.");

            payment.RefundStatus = RefundStatuses.Pending;
            payment.RefundRequestedAt = DateTimeOffset.UtcNow;
            payment.RefundLastError = null;
            await _db.SaveChangesAsync(ct);

            try
            {
                await AttemptRefundAsync(payment, throwOnError: true, ct);
            }
            catch
            {
                payment.RefundStatus = RefundStatuses.None;
                payment.RefundRequestedAt = null;
                await _db.SaveChangesAsync(ct);
                throw;
            }

            return new RefundResponse
            {
                OrderId = orderId,
                RefundStatus = payment.RefundStatus,
                Amount = payment.ChargedTotal,
                Message = payment.RefundStatus == RefundStatuses.Pending
                    ? "The refund is accepted. Stax cannot return a bank payment until it has had time to settle; the shop retries on its own and the shopper is refunded the whole amount without further action."
                    : "The whole amount was refunded to the shopper.",
            };
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task AttemptRefundAsync(OrderPayment payment, bool throwOnError, CancellationToken ct)
    {
        try
        {
            var result = await _stax.Refund(payment.StaxTransactionId, ct);
            payment.RefundStatus = RefundStatuses.Refunded;
            payment.RefundedAt = DateTimeOffset.UtcNow;
            payment.RefundLastError = null;
            payment.RefundTransactionId = LastChildId(result) ?? payment.StaxTransactionId;
        }
        catch (PaymentException ex)
        {
            var text = ex.Message;
            if (text.Contains("already been fully refunded", StringComparison.OrdinalIgnoreCase))
            {
                payment.RefundStatus = RefundStatuses.Refunded;
                payment.RefundedAt ??= DateTimeOffset.UtcNow;
                payment.RefundLastError = null;
            }
            else if (text.Contains("unavailable until", StringComparison.OrdinalIgnoreCase)
                     || text.Contains("business day", StringComparison.OrdinalIgnoreCase)
                     || text.Contains("not settled", StringComparison.OrdinalIgnoreCase))
            {
                // Bank payments cannot be returned until a few days after the charge: keep the refund pending and retry.
                payment.RefundStatus = RefundStatuses.Pending;
                payment.RefundLastError = text;
            }
            else
            {
                payment.RefundLastError = text;
                if (throwOnError)
                {
                    throw;
                }
            }
        }
        await _db.SaveChangesAsync(ct);
    }

    private static string? LastChildId(JsonElement tx)
    {
        if (tx.ValueKind == JsonValueKind.Object && tx.TryGetProperty("child_transactions", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            var last = children.EnumerateArray().LastOrDefault();
            if (last.ValueKind == JsonValueKind.Object) return StaxGateway.Str(last, "id");
        }
        return null;
    }

    // ================================================================ closing an account

    public async Task CloseAccountAsync(string email, CancellationToken ct)
    {
        var user = await _users.FindByNameAsync(email) ?? throw new PaymentException((int)HttpStatusCode.Unauthorized, "This shop account does not exist.");

        var payments = await _db.OrderPayments.Where(p => p.BuyerId == email).ToListAsync(ct);
        foreach (var p in payments) await TryReconcileAsync(p, ct);
        if (payments.Any(p => p.Status == PaymentStatuses.Processing || p.RefundStatus == RefundStatuses.Pending))
        {
            throw PaymentException.Conflict("The account cannot be closed while a bank payment is still settling or a refund is still pending.");
        }

        // Stax first: if it fails nothing is erased in the shop and closing can be retried.
        var customerIds = new List<string>();
        var profile = await FindProfileAsync(email, ct);
        if (profile != null) customerIds.Add(profile.StaxCustomerId);
        foreach (var c in await _stax.FindCustomersByEmail(email, ct))
        {
            var id = StaxGateway.Str(c, "id");
            if (id != null && !customerIds.Contains(id, StringComparer.OrdinalIgnoreCase)) customerIds.Add(id);
        }
        foreach (var customerId in customerIds)
        {
            try
            {
                foreach (var pm in await _stax.ListActivePaymentMethods(customerId, ct))
                {
                    await _stax.DeletePaymentMethod(StaxGateway.Str(pm, "id")!, ct);
                }
                await _stax.ScrubCustomer(customerId, ct);
                await _stax.DeleteCustomer(customerId, ct);
            }
            catch (PaymentException ex) when (ex.StatusCode == 404)
            {
                // already gone at Stax
            }
        }

        var orders = await _db.Orders.Include(o => o.OrderItems).Where(o => o.BuyerId == email).ToListAsync(ct);
        _db.OrderPayments.RemoveRange(payments);
        _db.Orders.RemoveRange(orders);
        var baskets = await _db.Baskets.Include(b => b.Items).Where(b => b.BuyerId == email).ToListAsync(ct);
        _db.Baskets.RemoveRange(baskets);
        if (profile != null) _db.ShopperProfiles.Remove(profile);
        await _db.SaveChangesAsync(ct);

        var result = await _users.DeleteAsync(user);
        if (!result.Succeeded)
        {
            throw new PaymentException((int)HttpStatusCode.InternalServerError, "The account's data was erased but the login could not be removed: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }
}
