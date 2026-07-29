using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Translates the subscription domain exceptions into faithful HTTP responses,
/// so clients get meaningful status codes rather than a blanket 500.
/// </summary>
internal static class SubscriptionResults
{
    public static async Task<IResult> RunAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (SubscriberIdentityException ex)
        {
            return Results.Problem(title: "Unauthenticated", detail: ex.Message, statusCode: StatusCodes.Status401Unauthorized);
        }
        catch (PlanNotFoundException ex)
        {
            return Results.Problem(title: "Plan not found", detail: ex.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (MaxioBillingException ex)
        {
            // Surface upstream billing failures as 502 Bad Gateway — the request was
            // well-formed but the downstream billing system rejected/could not serve it.
            return Results.Problem(title: "Billing system error", detail: ex.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
