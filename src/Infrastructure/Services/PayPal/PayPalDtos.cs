using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

internal class PayPalErrorDto
{
    public string? name { get; set; }
    public string? message { get; set; }
    public string? debug_id { get; set; }
    public List<PayPalErrorDetailDto>? details { get; set; }
}

internal class PayPalErrorDetailDto
{
    public string? issue { get; set; }
    public string? description { get; set; }
}

internal class AccessTokenDto
{
    public string? access_token { get; set; }
    public string? token_type { get; set; }
    public long expires_in { get; set; }
    public string? scope { get; set; }
}

internal class CreateOrderResponse
{
    public string? id { get; set; }
    public string? status { get; set; }
    public List<CreateOrderPurchaseUnitDto>? purchase_units { get; set; }
}

internal class CreateOrderPurchaseUnitDto
{
    public CreateOrderPaymentsDto? payments { get; set; }
}

internal class CreateOrderPaymentsDto
{
    public List<AuthorizationDto>? authorizations { get; set; }
}

internal class AuthorizationDto
{
    public string? id { get; set; }
    public string? status { get; set; }
    public string? expiration_time { get; set; }
}

internal class CaptureResponse
{
    public string? id { get; set; }
    public string? status { get; set; }
    public MoneyDto? amount { get; set; }
    public SellerReceivableBreakdownDto? seller_receivable_breakdown { get; set; }
}

internal class SellerReceivableBreakdownDto
{
    public MoneyDto? gross_amount { get; set; }
    public MoneyDto? paypal_fee { get; set; }
    public MoneyDto? net_amount { get; set; }
}

internal class VoidResponse
{
    public string? id { get; set; }
    public string? status { get; set; }
}

internal class ReauthorizeResponse
{
    public string? id { get; set; }
    public string? status { get; set; }
    public string? expiration_time { get; set; }
}

internal class RefundResponse
{
    public string? id { get; set; }
    public string? status { get; set; }
    public MoneyDto? amount { get; set; }
    public SellerPayableBreakdownDto? seller_payable_breakdown { get; set; }
}

internal class SellerPayableBreakdownDto
{
    public MoneyDto? total_refunded_amount { get; set; }
}

internal class SetupTokenResponse
{
    public string? id { get; set; }
    public string? status { get; set; }
    public CustomerDto? customer { get; set; }
}

internal class PaymentTokenResponse
{
    public string? id { get; set; }
    public CustomerDto? customer { get; set; }
    public PaymentTokenSourceDto? payment_source { get; set; }
}

internal class PaymentTokenSourceDto
{
    public PaymentTokenCardDto? card { get; set; }
}

internal class PaymentTokenCardDto
{
    public string? last_digits { get; set; }
    public string? brand { get; set; }
    public string? expiry { get; set; }
    public string? name { get; set; }
}

internal class CustomerDto
{
    public string? id { get; set; }
    public string? merchant_customer_id { get; set; }
}

internal class MoneyDto
{
    public string? currency_code { get; set; }
    public string? value { get; set; }
}

internal class SearchTransactionsResponse
{
    public List<TransactionDetailsDto>? transaction_details { get; set; }
    public int page { get; set; }
    public int total_pages { get; set; }
    public int total_items { get; set; }
}

internal class TransactionDetailsDto
{
    public TransactionInfoDto? transaction_info { get; set; }
}

internal class TransactionInfoDto
{
    public string? transaction_id { get; set; }
    public string? transaction_event_code { get; set; }
    public string? transaction_initiation_date { get; set; }
    public string? transaction_status { get; set; }
    public MoneyDto? transaction_amount { get; set; }
    public MoneyDto? fee_amount { get; set; }
    public string? invoice_id { get; set; }
    public string? custom_field { get; set; }
    public string? paypal_reference_id { get; set; }
    public string? paypal_reference_id_type { get; set; }
}