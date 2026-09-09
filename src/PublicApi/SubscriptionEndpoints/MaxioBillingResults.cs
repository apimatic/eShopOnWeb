using System;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps billing failures onto HTTP outcomes. Each failure kind gets a distinct
/// status: provider rejections (the caller can act) stay 4xx; provider
/// outages and unknown outcomes are 5xx so callers do not mistake them for a
/// deterministic rejection.
/// </summary>
internal static class MaxioBillingResults
{
    public static IResult Problem(MaxioBillingException ex, Guid correlationId)
    {
        var (statusCode, title) = ex.Error switch
        {
            MaxioBillingError.PlanNotFound => (404, "Plan not found"),
            MaxioBillingError.ProviderRejected => (422, "The billing provider rejected the request"),
            MaxioBillingError.ProviderUnreachable => (503, "The billing provider is unreachable"),
            MaxioBillingError.ProviderError => (502, "The billing provider failed"),
            MaxioBillingError.UnreadableResponse => (502, "The billing provider response could not be processed"),
            MaxioBillingError.FamilyNotFound => (500, "Billing configuration error"),
            _ => (500, "Billing error")
        };
        return Results.Problem(statusCode: statusCode, title: title, detail: ex.Message);
    }
}
