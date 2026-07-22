using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Translates the subscription domain's exceptions into HTTP results, so a caller can tell a bad request
/// apart from a provisioning problem and from an upstream provider failure.
/// </summary>
public static class SubscriptionErrorResults
{
    /// <summary>
    /// Runs an endpoint body, translating the subscription domain's exceptions into HTTP results.
    /// Anything else is left to the pipeline's exception middleware.
    /// </summary>
    public static async Task<IResult> ExecuteAsync(Func<Task<IResult>> handler)
    {
        try
        {
            return await handler();
        }
        catch (Exception exception)
        {
            var translated = TryTranslate(exception);

            if (translated is null)
            {
                throw;
            }

            return translated;
        }
    }

    /// <summary>
    /// Returns the matching result, or null when the exception is not one this API is expected to handle.
    /// </summary>
    public static IResult? TryTranslate(Exception exception)
    {
        return exception switch
        {
            // The request itself is not valid for the subscription's current state.
            InvalidSubscriptionOperationException invalid => Results.BadRequest(new { error = invalid.Message }),

            // The billing catalog is not provisioned as this deployment expects.
            BillingConfigurationException configuration => Results.Problem(
                detail: configuration.Message,
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Billing configuration error"),

            // The provider rejected the call or could not be reached.
            BillingProviderException provider => Results.Problem(
                detail: provider.ProviderMessage,
                statusCode: StatusCodes.Status502BadGateway,
                title: "Billing provider error"),

            _ => null
        };
    }
}
