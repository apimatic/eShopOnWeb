using System.Linq;
using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Turns a transport-level Maxio failure into a billing exception the API layer can map to a status
/// code. Upstream detail is summarised, never echoed verbatim, and credentials never appear.
/// </summary>
internal static class MaxioErrorTranslator
{
    public static BillingException Translate(MaxioApiException exception)
    {
        var detail = exception.Errors.Count > 0
            ? string.Join("; ", exception.Errors.Take(5))
            : "no detail returned";

        return exception.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new BillingConfigurationException("The billing system rejected our credentials. Check the configured Maxio API key and site subdomain."),

            HttpStatusCode.NotFound =>
                new BillingConfigurationException($"The billing system could not find the requested resource ({exception.RequestDescription}). Check the configured Maxio site and product family handle."),

            HttpStatusCode.Conflict =>
                new BillingConflictException($"The billing system reported a conflicting request: {detail}"),

            HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest =>
                new BillingValidationException($"The billing system rejected the request: {detail}"),

            HttpStatusCode.TooManyRequests =>
                new BillingUnavailableException("The billing system is throttling requests. Please try again shortly.", exception),

            >= HttpStatusCode.InternalServerError =>
                new BillingUnavailableException("The billing system is temporarily unavailable. Please try again shortly.", exception),

            _ => new BillingException($"The billing system returned an unexpected response: {detail}", exception)
        };
    }
}
