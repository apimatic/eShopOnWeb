using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.PublicApi.Payments;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Maps integration-layer failures to HTTP results. Provider 4xx surface as the same 4xx so
/// callers can act on them; transport/unknown provider failures surface as 502; domain state
/// violations as 409; missing or foreign-owned resources as 404.
/// </summary>
public static class ApiErrorResults
{
    public static IResult FromException(Exception ex) => ex switch
    {
        KeyNotFoundException => Results.NotFound(new { message = ex.Message }),
        InvalidOperationException => Results.Conflict(new { message = ex.Message }),
        PaymentGatewayException pgx => Results.Json(
            new { message = pgx.Message },
            statusCode: pgx.ProviderStatus is >= 400 and < 500 ? pgx.ProviderStatus.Value : StatusCodes.Status502BadGateway),
        _ => Results.Problem("An unexpected error occurred.", statusCode: StatusCodes.Status500InternalServerError)
    };
}
