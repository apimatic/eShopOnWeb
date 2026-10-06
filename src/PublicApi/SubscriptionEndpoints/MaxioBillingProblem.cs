using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Shared mapping from <see cref="MaxioBillingException"/> to a coherent HTTP error:
/// provider 4xx statuses surface as the same client status, everything ambiguous
/// surfaces as 5xx. Provider validation messages ride along when present.
/// </summary>
public static class MaxioBillingProblem
{
    public static IResult From(MaxioBillingException ex)
    {
        var extensions = ex.Errors.Count > 0
            ? new Dictionary<string, object?> { ["errors"] = ex.Errors }
            : null;
        return Results.Problem(statusCode: ex.StatusCode, title: ex.Message, extensions: extensions);
    }
}