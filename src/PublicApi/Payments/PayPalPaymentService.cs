using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public interface IPayPalPaymentService
{
    Task<AuthorizationWithAdditionalData> AuthorizeAsync(decimal amount, string currency, string? cardNumber, string? expiry, string? securityCode, string? name, string? vaultId, string key, CancellationToken ct);
    Task<CapturedPayment> CaptureAsync(string authorizationId, string key, CancellationToken ct);
    Task<PaymentAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken ct);
    Task<PaymentAuthorization> VoidAsync(string authorizationId, string key, CancellationToken ct);
    Task<Refund> RefundAsync(string captureId, decimal? amount, string key, CancellationToken ct);
    Task<PaymentTokenResponse> SaveCardAsync(string number, string expiry, string securityCode, string? name, string key, CancellationToken ct);
    Task DeleteCardAsync(string tokenId, CancellationToken ct);
    Task<IReadOnlyList<TransactionDetails>> SearchAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

public sealed class PayPalPaymentService(PayPalServerSdkClient client, PayPalSettings settings) : IPayPalPaymentService
{
    public async Task<AuthorizationWithAdditionalData> AuthorizeAsync(decimal amount, string currency, string? number, string? expiry, string? cvc, string? name, string? vaultId, string key, CancellationToken ct)
    {
        var source = new PaymentSource { Card = new CardRequest { Number = number, Expiry = expiry, SecurityCode = cvc, Name = name, VaultId = vaultId } };
        try
        {
            var order = await client.Orders.CreateOrder(null, key, null, null, null, new OrderRequest { Intent = CheckoutPaymentIntent.Authorize, PurchaseUnits = new[] { new PurchaseUnitRequest { Amount = new AmountWithBreakdown { CurrencyCode = currency, Value = amount.ToString("0.00", CultureInfo.InvariantCulture) } } }, PaymentSource = source }, ct: ct);
            var response = await client.Orders.AuthorizeOrder(order.Id!, null, key, null, null, null, ct: ct);
            var auth = response.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
            return auth?.Id is null ? throw new PaymentProviderException("PayPal did not return an authorization.") : auth;
        }
        catch (PaymentProviderException) { throw; }
        catch (Exception ex) { throw new PaymentProviderException("The payment provider could not complete the authorization.", null, ex); }
    }
    public async Task<CapturedPayment> CaptureAsync(string id, string key, CancellationToken ct) { try { return await client.Payments.CaptureAuthorizedPayment(id, null, key, null, new CaptureRequest { FinalCapture = true }, ct: ct); } catch (Exception ex) { throw new PaymentProviderException("The payment provider could not capture this authorization.", null, ex); } }
    public async Task<PaymentAuthorization> GetAuthorizationAsync(string id, CancellationToken ct) { try { return await client.Payments.GetAuthorizedPayment(id, null, null, ct: ct); } catch (Exception ex) { throw new PaymentProviderException("The payment provider could not read this authorization.", null, ex); } }
    public async Task<PaymentAuthorization> VoidAsync(string id, string key, CancellationToken ct) { try { return await client.Payments.VoidPayment(id, null, null, key, ct: ct); } catch (Exception ex) { throw new PaymentProviderException("The payment provider could not release this authorization.", null, ex); } }
    public async Task<Refund> RefundAsync(string id, decimal? amount, string key, CancellationToken ct) { try { return await client.Payments.RefundCapturedPayment(id, null, key, null, amount.HasValue ? new PayPalServerSdk.Models.RefundRequest { Amount = new Money { CurrencyCode = settings.Currency, Value = amount.Value.ToString("0.00", CultureInfo.InvariantCulture) } } : null, ct: ct); } catch (Exception ex) { throw new PaymentProviderException("The payment provider could not refund this capture.", null, ex); } }
    public async Task<PaymentTokenResponse> SaveCardAsync(string number, string expiry, string cvc, string? name, string key, CancellationToken ct) { try { return await client.Vault.CreatePaymentToken(key, new PaymentTokenRequest { PaymentSource = new PaymentTokenRequestPaymentSource { Card = new PaymentTokenRequestCard { Number = number, Expiry = expiry, SecurityCode = cvc, Name = name } } }, ct: ct); } catch (Exception ex) { throw new PaymentProviderException("The payment provider could not save this card.", null, ex); } }
    public async Task DeleteCardAsync(string id, CancellationToken ct) { try { await client.Vault.DeletePaymentToken(id, ct: ct); } catch (Exception ex) { throw new PaymentProviderException("The payment provider could not remove this card.", null, ex); } }
    public async Task<IReadOnlyList<TransactionDetails>> SearchAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        try
        {
            var all = new List<TransactionDetails>();
            var page = 1;
            while (true)
            {
                var response = await client.TransactionSearch.SearchTransactions(from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"), to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"), null, null, null, null, null, null, null, null, "transaction_info", "Y", 100, page, ct: ct);
                if (response.TransactionDetails is not null) all.AddRange(response.TransactionDetails);
                if (!response.TotalPages.HasValue || page >= response.TotalPages.Value) return all;
                page++;
            }
        }
        catch (Exception ex) { throw new PaymentProviderException("The payment provider could not produce reconciliation data.", null, ex); }
    }
}
