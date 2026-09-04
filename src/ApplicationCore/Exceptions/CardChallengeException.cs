namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the payment provider asks the shopper to approve a card payment in a
/// browser (e.g. 3-D Secure challenge). The integration stops rather than building an
/// approval round-trip.
/// </summary>
public class CardChallengeException : ApiException
{
    public CardChallengeException(string message)
        : base(message, 422)
    {
    }
}