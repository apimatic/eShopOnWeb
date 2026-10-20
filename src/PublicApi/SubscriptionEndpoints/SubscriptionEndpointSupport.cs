using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int? SubscriptionId { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public decimal? Price { get; set; }
    public string? State { get; set; }
    public System.DateTimeOffset? NextBillingDate { get; set; }

    public static SubscriptionDto From(SubscriptionSummary s) => new()
    {
        SubscriptionId = s.SubscriptionId,
        PlanHandle = s.PlanHandle,
        PlanName = s.PlanName,
        Price = s.PriceInCents / 100m,
        State = s.State,
        NextBillingDate = s.NextBillingDate,
    };
}

internal static class SubscriptionEndpointSupport
{
    /// <summary>The caller's identity: the user name in the JWT (the only identity claim the token carries).</summary>
    public static string? UserKey(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.Name);

    /// <summary>One mapping from billing failure kind to HTTP outcome, shared by every subscription endpoint.</summary>
    public static IResult Problem(SubscriptionBillingException ex) => ex.Failure switch
    {
        SubscriptionFailure.Invalid => Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest),
        SubscriptionFailure.Conflict => Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict),
        SubscriptionFailure.Rejected => Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity),
        _ => Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable),
    };
}
