using System;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointHelpers
{
    public static MaxioUser ToMaxioUser(ApplicationUser user)
    {
        var email = user.Email ?? user.UserName ?? throw new InvalidOperationException("The authenticated user has no email address.");
        var localPart = email.Split('@', 2)[0];
        var nameParts = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var firstName = nameParts.Length > 0 ? nameParts[0] : localPart;
        var lastName = nameParts.Length > 1 ? string.Join(" ", nameParts[1..]) : "Customer";

        return new MaxioUser(user.Id, email, firstName, lastName);
    }

    public static IResult BillingFailure(MaxioApiException exception)
        => Results.Problem(
            title: "The subscription billing service could not complete the request.",
            detail: exception.Message,
            statusCode: StatusCodes.Status502BadGateway);
}
