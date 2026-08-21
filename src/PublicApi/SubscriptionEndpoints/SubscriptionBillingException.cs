using System;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(
        int statusCode,
        string code,
        string safeMessage,
        Exception? innerException = null)
        : base(safeMessage, innerException)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string Code { get; }

    public static SubscriptionBillingException Unauthorized() => new(
        StatusCodes.Status401Unauthorized,
        "billing_identity_unavailable",
        "The authenticated user could not be resolved.");

    public static SubscriptionBillingException UnknownPlan() => new(
        StatusCodes.Status404NotFound,
        "subscription_plan_not_found",
        "The requested subscription plan was not found.");

    public static SubscriptionBillingException PlanUnavailable() => new(
        StatusCodes.Status422UnprocessableEntity,
        "subscription_plan_unavailable",
        "The requested plan is not available for payment-free subscription.");

    public static SubscriptionBillingException InProgress() => new(
        StatusCodes.Status409Conflict,
        "subscription_in_progress",
        "This subscription request is already in progress or awaiting reconciliation.");

    public static SubscriptionBillingException ProviderValidation(Exception? inner = null) => new(
        StatusCodes.Status422UnprocessableEntity,
        "maxio_rejected_request",
        "Maxio rejected the subscription request.",
        inner);

    public static SubscriptionBillingException ProviderUnavailable(Exception? inner = null) => new(
        StatusCodes.Status502BadGateway,
        "maxio_unavailable",
        "Maxio is temporarily unavailable or returned an unreadable response.",
        inner);

    public static SubscriptionBillingException ProviderConfiguration(Exception? inner = null) => new(
        StatusCodes.Status502BadGateway,
        "maxio_configuration_error",
        "Maxio rejected the server credentials or site configuration.",
        inner);

    public static SubscriptionBillingException ContractDrift(Exception? inner = null) => new(
        StatusCodes.Status502BadGateway,
        "maxio_contract_error",
        "Maxio returned an incomplete response.",
        inner);
}
