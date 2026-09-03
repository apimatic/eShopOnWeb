using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalGateway
{
    private readonly PayPalServerSdkClient _client;
    private readonly PayPalSettings _settings;
    public PayPalGateway(PayPalServerSdkClient client, PayPalSettings settings) { _client=client; _settings=settings; }
    public string Currency => _settings.Currency;
    private string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private AmountWithBreakdown Amount(decimal value) => new() { CurrencyCode=Currency, Value=Money(value) };
    public async Task<PayPalServerSdk.Models.Order> CreateOrder(decimal total, CancellationToken ct)
        => await _client.Orders.CreateOrder(null, null, null, null, null,
            new OrderRequest { Intent=CheckoutPaymentIntent.Authorize, PurchaseUnits=new[] { new PurchaseUnitRequest { ReferenceId="default", Amount=Amount(total) } } }, ct:ct);
    public Task<OrderAuthorizeResponse> Authorize(string id, string? number, string? expiry, string? cvc, string? name, string? vaultId, CancellationToken ct)
    {
        CardRequest? card = null;
        if (!string.IsNullOrWhiteSpace(vaultId)) card = new CardRequest { VaultId=vaultId };
        else card = new CardRequest { Number=number, Expiry=expiry, SecurityCode=cvc, Name=name };
        return _client.Orders.AuthorizeOrder(id, null, null, null, null,
            new OrderAuthorizeRequest { PaymentSource=new OrderAuthorizeRequestPaymentSource { Card=card } }, ct:ct);
    }
    public Task<PaymentAuthorization> GetAuthorization(string id, CancellationToken ct) => _client.Payments.GetAuthorizedPayment(id,null,null,ct:ct);
    public Task<PaymentAuthorization> Reauthorize(string id, decimal total, CancellationToken ct)
        => _client.Payments.ReauthorizePayment(id,null,null,new ReauthorizeRequest { Amount=new Money { CurrencyCode=Currency, Value=Money(total) } },ct:ct);
    public Task<CapturedPayment> Capture(string id, decimal total, CancellationToken ct)
        => _client.Payments.CaptureAuthorizedPayment(id,null,null,null,new CaptureRequest { Amount=new PayPalServerSdk.Models.Money { CurrencyCode=Currency, Value=Money(total) }, FinalCapture=true },ct:ct);
    public Task<PaymentAuthorization> Void(string id, CancellationToken ct) => _client.Payments.VoidPayment(id,null,null,null,ct:ct);
    public Task<Refund> Refund(string id, decimal? amount, string key, CancellationToken ct)
        => _client.Payments.RefundCapturedPayment(id,null,key,null,amount is null ? null : new PayPalServerSdk.Models.RefundRequest { Amount=new PayPalServerSdk.Models.Money { CurrencyCode=Currency, Value=Money(amount.Value) } },ct:ct);
    public Task<PaymentTokenResponse> SaveCard(string customerId, string? number, string? expiry, string? cvc, string? name, CancellationToken ct)
        => _client.Vault.CreatePaymentToken(null,new PaymentTokenRequest { Customer=new Customer { Id=customerId, MerchantCustomerId=customerId }, PaymentSource=new PaymentTokenRequestPaymentSource { Card=new PaymentTokenRequestCard { Number=number, Expiry=expiry, SecurityCode=cvc, Name=name } } },ct:ct);
    public Task DeleteCard(string id, CancellationToken ct) => _client.Vault.DeletePaymentToken(id,ct:ct);
    public Task<CustomerVaultPaymentTokensResponse> Cards(string id, int page, CancellationToken ct)
        => _client.Vault.ListCustomerPaymentTokens(id,100,page,true,ct:ct);
    public Task<SearchResponse> Search(string from,string to,int page,CancellationToken ct)
        => _client.TransactionSearch.SearchTransactions(from,to,null,null,null,null,null,null,null,null,"transaction_info","Y",100,page,ct:ct);
}
