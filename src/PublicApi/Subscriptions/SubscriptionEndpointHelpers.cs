using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

internal static class SubscriptionEndpointHelpers
{
    internal static ActionResult FromException(ControllerBase endpoint, SubscriptionBillingException exception) =>
        exception switch
        {
            ShopperNotFoundException => endpoint.Unauthorized(),
            SubscriptionPlanNotFoundException => endpoint.NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Subscription plan not found",
                Detail = exception.Message
            }),
            PaymentMethodRequiredException => endpoint.UnprocessableEntity(new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "Payment method required",
                Detail = exception.Message
            }),
            _ => throw exception
        };
}
