namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing system has already seen this uniqueness token and refused to process the request
/// again. The first submission may or may not have succeeded, so the caller has to re-read state
/// rather than assume either outcome.
/// </summary>
public class DuplicateBillingSubmissionException : BillingGatewayException
{
    public DuplicateBillingSubmissionException(string uniquenessToken)
        : base($"The billing system has already processed a request with uniqueness token '{uniquenessToken}'.")
    {
        UniquenessToken = uniquenessToken;
    }

    public string UniquenessToken { get; }
}
