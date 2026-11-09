using System;
using System.Linq;
using System.Text.Json;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// The shop passes its card processing fees on to the shopper. The fees are the merchant's own
/// processing rates as Stax holds them in the shop's registration: a discount rate (percent of the
/// amount) plus a fixed fee per transaction, by card type. Bank payments carry no fee.
/// </summary>
public static class FeeCalculator
{
    private static readonly string[] Tiers = { "qual", "mid", "non" };

    public static decimal Compute(JsonElement registration, string method, string? cardType, string? binType, decimal subtotal)
    {
        if (!string.Equals(method, "card", StringComparison.OrdinalIgnoreCase)) return 0m;

        var isAmex = string.Equals(cardType, "amex", StringComparison.OrdinalIgnoreCase);
        decimal rate;
        decimal fixedFee;
        var debit = string.Equals(binType, "DEBIT", StringComparison.OrdinalIgnoreCase);

        if (isAmex)
        {
            (rate, fixedFee) = Highest(registration, "amex_{0}_disc_rate", "amex_{0}_trans_fee");
        }
        else if (debit && StaxGateway.Dec(registration, "cnp_debit_discount_rate") is { } debitRate)
        {
            rate = debitRate;
            fixedFee = StaxGateway.Dec(registration, "cnp_debit_trans_fee") ?? 0m;
        }
        else
        {
            (rate, fixedFee) = Highest(registration, "credit_{0}_disc_rate", "credit_{0}_trans_fee");
        }

        return Math.Round(subtotal * rate / 100m + fixedFee, 2, MidpointRounding.AwayFromZero);
    }

    // A card-not-present payment may land in any interchange tier, so the shop recovers the dearest one.
    private static (decimal rate, decimal fee) Highest(JsonElement registration, string rateKey, string feeKey)
    {
        var rate = Tiers.Select(t => StaxGateway.Dec(registration, string.Format(rateKey, t)) ?? 0m).DefaultIfEmpty(0m).Max();
        var fee = Tiers.Select(t => StaxGateway.Dec(registration, string.Format(feeKey, t)) ?? 0m).DefaultIfEmpty(0m).Max();
        return (rate, fee);
    }
}
