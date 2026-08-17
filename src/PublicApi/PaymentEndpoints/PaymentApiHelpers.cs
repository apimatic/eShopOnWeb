using System;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using AppCard = Microsoft.eShopWeb.ApplicationCore.Payments.CardDetails;
using AppBilling = Microsoft.eShopWeb.ApplicationCore.Payments.BillingAddress;
using ResultStatus = Ardalis.Result.ResultStatus;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Shared helpers for the payment endpoints: identity, request mapping and Result→HTTP translation.</summary>
public static class PaymentApiHelpers
{
    public static string? GetUserName(this ClaimsPrincipal? user) => user?.Identity?.Name;

    public static AppCard? ToCardDetails(this CardModel? card)
    {
        if (card is null)
        {
            return null;
        }

        AppBilling? billing = card.BillingAddress is null
            ? null
            : new AppBilling(card.BillingAddress.AddressLine1, card.BillingAddress.AddressLine2,
                card.BillingAddress.AdminArea2, card.BillingAddress.AdminArea1,
                card.BillingAddress.PostalCode, card.BillingAddress.CountryCode);

        return new AppCard(card.Number ?? string.Empty, card.Expiry ?? string.Empty,
            card.SecurityCode ?? string.Empty, card.CardholderName, billing);
    }

    public static IResult ToHttp<T>(Ardalis.Result.Result<T> result, Func<T, IResult> onSuccess) =>
        result.IsSuccess ? onSuccess(result.Value) : Failure(result);

    public static IResult ToHttp(Ardalis.Result.Result result, IResult onSuccess) =>
        result.IsSuccess ? onSuccess : Failure(result);

    private static IResult Failure(Ardalis.Result.IResult result)
    {
        var messages = result.Status == ResultStatus.Invalid
            ? result.ValidationErrors.Select(v => v.ErrorMessage).ToArray()
            : result.Errors.ToArray();

        var payload = new { errors = messages.Length > 0 ? messages : new[] { result.Status.ToString() } };

        return result.Status switch
        {
            ResultStatus.NotFound => Results.NotFound(payload),
            ResultStatus.Forbidden => Results.Json(payload, statusCode: StatusCodes.Status403Forbidden),
            ResultStatus.Unauthorized => Results.Json(payload, statusCode: StatusCodes.Status401Unauthorized),
            ResultStatus.Invalid => Results.Json(payload, statusCode: StatusCodes.Status422UnprocessableEntity),
            ResultStatus.Error => Results.Json(payload, statusCode: StatusCodes.Status502BadGateway),
            _ => Results.Json(payload, statusCode: StatusCodes.Status400BadRequest)
        };
    }
}
