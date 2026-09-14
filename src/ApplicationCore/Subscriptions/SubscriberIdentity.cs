using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb user on whose behalf a Maxio billing operation is performed.
/// The <see cref="UserId"/> is the stable application identity (the JWT name claim)
/// and is used to derive the Maxio customer <c>reference</c>, which is how the
/// user &lt;-&gt; Maxio customer mapping is persisted (Maxio is the system of record,
/// so the mapping survives even though the local database is in-memory).
/// </summary>
public sealed class SubscriberIdentity
{
    public SubscriberIdentity(string userId, string? email = null)
    {
        UserId = Guard.Against.NullOrWhiteSpace(userId, nameof(userId));
        // In eShopOnWeb the user name is the e-mail address; fall back to it when
        // no explicit e-mail is supplied.
        Email = string.IsNullOrWhiteSpace(email) ? userId : email!;
    }

    /// <summary>Stable application identity (JWT <c>name</c> claim).</summary>
    public string UserId { get; }

    /// <summary>E-mail used when a Maxio customer must be created for this user.</summary>
    public string Email { get; }
}
