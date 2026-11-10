using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

/// <summary>Gives a transfer's money back: cancels it if it has not left yet, refunds it otherwise.</summary>
public class TransferReverser
{
    private readonly PlaidClient _plaid;

    public TransferReverser(PlaidClient plaid) => _plaid = plaid;

    /// <returns>The refund id when a refund was needed, otherwise null (cancelled, or the money never moved).</returns>
    public async Task<string?> ReverseAsync(string transferId, decimal amount, string idempotencyKey, CancellationToken ct)
    {
        var transfer = (await _plaid.TransferGetAsync(transferId, ct)).Obj("transfer");
        var status = transfer?.Str("status");
        if (status is "cancelled" or "failed" or "returned")
            return null; // the money never moved, or already came back

        if (transfer?.Bool("cancellable") == true)
        {
            try
            {
                await _plaid.TransferCancelAsync(transferId, ct);
                return null;
            }
            catch (PlaidException ex) when (!ex.IsUnavailable && !ex.IsNotConfigured)
            {
                // Too late to cancel: it has gone to the bank, so refund it instead.
            }
        }

        var refund = await _plaid.TransferRefundCreateAsync(transferId, amount.ToString("F2", CultureInfo.InvariantCulture), idempotencyKey, ct);
        return refund.Obj("refund")?.Str("id");
    }
}
