using System;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>Shared helpers for the payment endpoints: identity extraction, card validation and error mapping.</summary>
public static class PaymentEndpointHelpers
{
    public static string? GetBuyerId(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name) ?? user.FindFirstValue("name") ?? user.Identity?.Name;

    public static CardInput? ToCardInput(CardDetailsDto? dto, out PaymentError? error)
    {
        error = null;
        if (dto == null)
        {
            error = new PaymentError(PaymentErrorType.Validation, "Card details are required.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            error = new PaymentError(PaymentErrorType.Validation, "Cardholder name is required.");
            return null;
        }

        var number = dto.Number?.Replace(" ", string.Empty).Replace("-", string.Empty) ?? string.Empty;
        if (number.Length < 12 || number.Length > 19 || !System.Linq.Enumerable.All(number, char.IsDigit))
        {
            error = new PaymentError(PaymentErrorType.Validation, "A valid card number is required.");
            return null;
        }

        if (dto.ExpiryMonth < 1 || dto.ExpiryMonth > 12)
        {
            error = new PaymentError(PaymentErrorType.Validation, "ExpiryMonth must be between 1 and 12.");
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        if (dto.ExpiryYear < now.Year || (dto.ExpiryYear == now.Year && dto.ExpiryMonth < now.Month))
        {
            error = new PaymentError(PaymentErrorType.Validation, "The card expiry must be in the future.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(dto.SecurityCode) || dto.SecurityCode.Length < 3 || dto.SecurityCode.Length > 4 ||
            !System.Linq.Enumerable.All(dto.SecurityCode, char.IsDigit))
        {
            error = new PaymentError(PaymentErrorType.Validation, "A valid security code is required.");
            return null;
        }

        if (dto.BillingAddress == null || string.IsNullOrWhiteSpace(dto.BillingAddress.CountryCode) ||
            dto.BillingAddress.CountryCode.Length != 2)
        {
            error = new PaymentError(PaymentErrorType.Validation, "A billing address with a two-letter country code is required.");
            return null;
        }

        return new CardInput
        {
            Name = dto.Name,
            Number = number,
            ExpiryMonth = dto.ExpiryMonth,
            ExpiryYear = dto.ExpiryYear,
            SecurityCode = dto.SecurityCode,
            BillingAddress = new BillingAddressInput
            {
                CountryCode = dto.BillingAddress.CountryCode,
                AddressLine1 = dto.BillingAddress.AddressLine1,
                AddressLine2 = dto.BillingAddress.AddressLine2,
                AdminArea1 = dto.BillingAddress.AdminArea1,
                AdminArea2 = dto.BillingAddress.AdminArea2,
                PostalCode = dto.BillingAddress.PostalCode
            }
        };
    }

    /// <summary>Maps a classified payment error onto an HTTP result; distinct failure kinds stay distinct.</summary>
    public static IResult FromError(PaymentError error)
    {
        var (status, code) = error.Type switch
        {
            PaymentErrorType.Validation => (HttpStatusCode.BadRequest, "validation_failed"),
            PaymentErrorType.NotFound => (HttpStatusCode.NotFound, "not_found"),
            PaymentErrorType.Declined => (HttpStatusCode.UnprocessableEntity, "payment_declined"),
            PaymentErrorType.StaleAuthorization => (HttpStatusCode.Conflict, "authorization_not_renewable"),
            PaymentErrorType.Conflict => (HttpStatusCode.Conflict, "conflict"),
            PaymentErrorType.ProviderError => (HttpStatusCode.BadGateway, "payment_provider_error"),
            PaymentErrorType.TransportFailure => (HttpStatusCode.ServiceUnavailable, "payment_provider_unavailable"),
            _ => (HttpStatusCode.InternalServerError, "payment_error")
        };

        return Results.Json(new { error = code, message = error.Message }, statusCode: (int)status);
    }
}

