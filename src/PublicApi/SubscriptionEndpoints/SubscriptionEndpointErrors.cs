using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointErrors
{
    public static ActionResult<T> ToActionResult<T>(ControllerBase controller, BillingValidationException exception) =>
        controller.Problem(
            statusCode: StatusCodes.Status422UnprocessableEntity,
            title: "The subscription request is invalid.",
            detail: exception.Message);

    public static ActionResult<T> ToActionResult<T>(ControllerBase controller, BillingProviderException exception)
    {
        var status = exception.ProviderStatusCode is null
            ? StatusCodes.Status503ServiceUnavailable
            : (int)exception.ProviderStatusCode.Value;
        return controller.Problem(
            statusCode: status,
            title: status >= 500 ? "Subscription billing is temporarily unavailable." : "Maxio rejected the request.",
            detail: exception.Message);
    }
}
