using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Shared <see cref="IResult"/> helpers for the subscription endpoints.</summary>
internal static class SubscriptionResults
{
    /// <summary>An upstream/config failure in the billing system is surfaced as 502 Bad Gateway.</summary>
    public static IResult BillingError(SubscriptionBillingException exception) =>
        Results.Problem(detail: exception.Message, statusCode: StatusCodes.Status502BadGateway, title: "Billing provider error");
}
