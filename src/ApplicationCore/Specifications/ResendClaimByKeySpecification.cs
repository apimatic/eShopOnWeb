using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A resend claim by its idempotency key (the entity's primary key).</summary>
public class ResendClaimByKeySpecification : Specification<ResendClaim>
{
    public ResendClaimByKeySpecification(string idempotencyKey)
    {
        // No-tracking: this is read after a duplicate-key insert failed, which leaves a stale
        // (Added, no-result) claim in the change tracker. We must read the PERSISTED claim — the one
        // the winning request set the resulting notification id on — not that tracked stand-in.
        Query.Where(c => c.IdempotencyKey == idempotencyKey).AsNoTracking();
    }
}
