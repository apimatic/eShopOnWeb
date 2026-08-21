using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// --- Shared request DTOs (bound from JSON bodies; never carry caller identity) ---

public record OrderLineDto(int CatalogItemId, int Quantity);

public record ShippingAddressDto(string? Street, string? City, string? State, string? Country, string? ZipCode);

public record BillingAddressDto(string? AddressLine1, string? AddressLine2, string? AdminArea1, string? AdminArea2, string? PostalCode, string? CountryCode);

public record CardDto(string? Name, string? Number, string? Expiry, string? SecurityCode, BillingAddressDto? BillingAddress);

// --- Per-endpoint requests. Identity/route fields are set server-side by the endpoint, never from the body. ---

public record PlaceOrderRequest(List<OrderLineDto> Items, ShippingAddressDto? ShipTo)
{
    public string BuyerId { get; set; } = string.Empty;
}

public record PayOrderRequest(int? PaymentMethodId, CardDto? Card)
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
}

public record RefundOrderRequest(decimal? Amount, string? IdempotencyKey)
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
}

public record SavePaymentMethodRequest(string? Name, string? Number, string? Expiry, string? SecurityCode, BillingAddressDto? BillingAddress)
{
    public string BuyerId { get; set; } = string.Empty;
}

public record FulfilOrderRequest(int OrderId);

public record CancelOrderRequest(int OrderId);

public record MyOrdersRequest(string BuyerId);

public record ReconciliationRequest(DateTimeOffset From, DateTimeOffset To);

public record ListPaymentMethodsRequest(string BuyerId);

public record DeletePaymentMethodRequest(int PaymentMethodId, string BuyerId);

/// <summary>
/// Maps an orchestration <see cref="PaymentResult{T}"/> onto an HTTP result, so a validation rejection or a
/// provider failure surfaces as the right status code with a caller-safe body — never an opaque 500.
/// </summary>
public static class PaymentHttpResults
{
    public static IResult ToHttpResult<T>(this PaymentResult<T> result, Func<T, IResult> onSuccess) =>
        result.Status switch
        {
            PaymentResultStatus.Ok => onSuccess(result.Value!),
            PaymentResultStatus.NotFound => Results.NotFound(new { error = result.Error }),
            PaymentResultStatus.Invalid => Results.BadRequest(new { error = result.Error }),
            PaymentResultStatus.Conflict => Results.Conflict(new { error = result.Error }),
            PaymentResultStatus.RequiresApproval => Results.Json(
                new { status = "RequiresApproval", error = result.Error }, statusCode: StatusCodes.Status402PaymentRequired),
            PaymentResultStatus.ProviderUnavailable => Results.Json(
                new { error = result.Error }, statusCode: StatusCodes.Status502BadGateway),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };
}
