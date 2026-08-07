namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// A request to charge a payment for an order. Exactly one of <see cref="Card"/> (a one-off card) or
/// <see cref="VaultTokenId"/> (a previously saved card) is supplied. <see cref="IdempotencyKey"/> is
/// sent to PayPal as <c>PayPal-Request-Id</c> so a retried/double-clicked request never double charges.
/// </summary>
public sealed class ChargeCardRequest
{
    public ChargeCardRequest(
        decimal amount,
        string currencyCode,
        string idempotencyKey,
        CardDetails? card,
        string? vaultTokenId)
    {
        Amount = amount;
        CurrencyCode = currencyCode;
        IdempotencyKey = idempotencyKey;
        Card = card;
        VaultTokenId = vaultTokenId;
    }

    public decimal Amount { get; }
    public string CurrencyCode { get; }
    public string IdempotencyKey { get; }
    public CardDetails? Card { get; }
    public string? VaultTokenId { get; }

    public override string ToString() =>
        $"ChargeCardRequest {{ Amount = {Amount} {CurrencyCode}, VaultTokenId = {VaultTokenId ?? "(one-off card)"} }}";
}
