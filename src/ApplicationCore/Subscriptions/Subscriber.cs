namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb identity a subscription belongs to. The billing provider's customer record is keyed off
/// this, so it must be derived from something stable for the user — never from a per-request value.
/// </summary>
public class Subscriber
{
    public Subscriber(string email)
    {
        Email = email;
    }

    /// <summary>The signed-in user's email, taken from the caller's JWT.</summary>
    public string Email { get; }
}
