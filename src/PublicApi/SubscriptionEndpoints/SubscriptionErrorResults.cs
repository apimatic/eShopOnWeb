using System;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class SubscriptionErrorResults
{
    public static IResult From(BillingProviderException exception)
    {
        return exception.Kind switch
        {
            BillingFailureKind.Rejected => Results.Problem(
                title: "The subscription request was rejected by the billing provider.",
                detail: exception.Message,
                statusCode: StatusCodes.Status422UnprocessableEntity),
            BillingFailureKind.NotFound => Results.Problem(
                title: "Billing resource not found.",
                detail: exception.Message,
                statusCode: StatusCodes.Status404NotFound),
            BillingFailureKind.Misconfigured => Results.Problem(
                title: "Billing is not available.",
                detail: "The billing integration is misconfigured. Contact an administrator.",
                statusCode: StatusCodes.Status502BadGateway),
            _ => Results.Problem(
                title: "Billing is temporarily unavailable.",
                detail: "The billing provider could not be reached. Please retry shortly.",
                statusCode: StatusCodes.Status503ServiceUnavailable)
        };
    }

    public static IResult PlanNotFound(SubscriptionPlanNotFoundException exception)
        => Results.Problem(
            title: "Subscription plan not found.",
            detail: exception.Message,
            statusCode: StatusCodes.Status404NotFound);
}
