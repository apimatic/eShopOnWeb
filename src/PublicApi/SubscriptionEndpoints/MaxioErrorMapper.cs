using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Translates Maxio client failures into API responses that do not leak provider details
/// while remaining actionable for callers.
/// </summary>
public static class MaxioErrorMapper
{
    public static ActionResult ToActionResult(this MaxioApiException exception)
    {
        if (exception.StatusCode == 422)
        {
            return new BadRequestObjectResult(new
            {
                message = "The billing provider rejected the subscription request.",
                providerErrors = exception.ResponseBody
            });
        }

        if (exception.StatusCode == 404)
        {
            return new NotFoundObjectResult(new { message = "The requested billing record was not found." });
        }

        if (exception.StatusCode == 401 || exception.StatusCode == 403)
        {
            return new ObjectResult(new { message = "The billing provider rejected the configured credentials." })
            {
                StatusCode = (int)HttpStatusCode.BadGateway
            };
        }

        return new ObjectResult(new { message = "The billing provider returned an unexpected error." })
        {
            StatusCode = (int)HttpStatusCode.BadGateway
        };
    }

    public static ActionResult ToActionResult(this MaxioConfigurationException exception)
    {
        return new ObjectResult(new { message = "Subscription billing is not configured." })
        {
            StatusCode = (int)HttpStatusCode.ServiceUnavailable
        };
    }
}
