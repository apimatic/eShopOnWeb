using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.Infrastructure.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps <see cref="MaxioBillingException"/> kinds onto HTTP responses. Distinct failures stay
/// distinct: deterministic rejections are 4xx, provider outages are 502, unknown conditions 500.
/// </summary>
public static class MaxioBillingHttpResults
{
    public static IResult From(MaxioBillingException ex)
    {
        return ex.Kind switch
        {
            MaxioBillingErrorKind.NotFound => Results.Problem(statusCode: 404, title: ex.Message),
            MaxioBillingErrorKind.InvalidRequest => Results.Problem(statusCode: 400, title: ex.Message),
            MaxioBillingErrorKind.ProviderUnavailable => Results.Problem(statusCode: 502, title: ex.Message),
            _ => Results.Problem(statusCode: 500, title: "An unexpected billing error occurred."),
        };
    }
}
