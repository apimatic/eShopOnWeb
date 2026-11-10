using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.Banking;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BankEndpoints;

public class BankPaymentLineBody
{
    public int CatalogItemId { get; init; }

    /// <summary>How many of the item (also accepted as <c>units</c>).</summary>
    public int? Quantity { get; init; }
    public int? Units { get; init; }
}

public class StartBankPaymentBody
{
    public int ConnectionId { get; init; }
    public List<BankPaymentLineBody>? Items { get; init; }
    public decimal ShippingCost { get; init; }
    public ShipTo? ShipToAddress { get; init; }
}

public class StartBankPaymentRequest : BaseRequest
{
    public string BuyerId { get; init; } = string.Empty;
    public StartBankPaymentBody Body { get; init; } = new();
}

/// <summary>
/// The payment as quoted: <c>options</c> are the speeds available for this order.
/// </summary>
public record BankPaymentResponse(
    Guid CheckoutId,
    int ConnectionId,
    decimal Subtotal,
    decimal ShippingCost,
    decimal Total,
    IReadOnlyList<string> Options,
    decimal ExpressFee)
{
    public static BankPaymentResponse From(CheckoutQuote q) =>
        new(q.CheckoutId, q.ConnectionId, q.Subtotal, q.ShippingCost, q.Total, q.Options, q.ExpressFee);
}

/// <summary>
/// Starts a bank payment for an order
/// </summary>
public class StartBankPaymentEndpoint : IEndpoint<IResult, StartBankPaymentRequest, BankPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/checkout/bank-payment",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, StartBankPaymentBody body, BankPaymentService service) =>
            {
                return await HandleAsync(new StartBankPaymentRequest { BuyerId = BankEndpointSupport.Caller(user), Body = body }, service);
            })
            .Produces<BankPaymentResponse>()
            .WithTags("BankEndpoints");
    }

    public async Task<IResult> HandleAsync(StartBankPaymentRequest request, BankPaymentService service)
    {
        var lines = request.Body.Items?
            .Select(i => new CheckoutLine(i.CatalogItemId, i.Quantity ?? i.Units ?? 1))
            .ToList();
        var quote = await service.StartAsync(request.BuyerId, request.Body.ConnectionId, lines,
            request.Body.ShippingCost, request.Body.ShipToAddress, default);
        return Results.Ok(BankPaymentResponse.From(quote));
    }
}

public class ChangeShippingBody
{
    public decimal? ShippingCost { get; init; }
}

public class ChangeShippingRequest : BaseRequest
{
    public string BuyerId { get; init; } = string.Empty;
    public Guid CheckoutId { get; init; }
    public decimal? ShippingCost { get; init; }
}

/// <summary>
/// Changes the shipping cost of a started bank payment; confirming charges the new total
/// </summary>
public class ChangeShippingEndpoint : IEndpoint<IResult, ChangeShippingRequest, BankPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/checkout/bank-payment/{checkoutId}/shipping",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, Guid checkoutId, ChangeShippingBody body, BankPaymentService service) =>
            {
                return await HandleAsync(new ChangeShippingRequest
                {
                    BuyerId = BankEndpointSupport.Caller(user),
                    CheckoutId = checkoutId,
                    ShippingCost = body.ShippingCost,
                }, service);
            })
            .Produces<BankPaymentResponse>()
            .WithTags("BankEndpoints");
    }

    public async Task<IResult> HandleAsync(ChangeShippingRequest request, BankPaymentService service)
    {
        if (request.ShippingCost is null) throw new BankingException(400, "shippingCost is required.");
        var quote = await service.ChangeShippingAsync(request.BuyerId, request.CheckoutId, request.ShippingCost.Value, default);
        return Results.Ok(BankPaymentResponse.From(quote));
    }
}

public class ConfirmBody
{
    /// <summary>"standard" or "express".</summary>
    public string? Speed { get; init; }
}

public class ConfirmRequest : BaseRequest
{
    public string BuyerId { get; init; } = string.Empty;
    public Guid CheckoutId { get; init; }
    public string? Speed { get; init; }
}

/// <summary>
/// The order confirmation: <c>fee</c> is what the chosen speed cost, <c>arrivesOn</c> when the money is expected to arrive.
/// </summary>
public record ConfirmResponse(
    int OrderId,
    Guid CheckoutId,
    string Speed,
    decimal Total,
    decimal Fee,
    decimal TotalCharged,
    DateOnly ArrivesOn,
    string TransferId)
{
    public static ConfirmResponse From(OrderConfirmation c) =>
        new(c.OrderId, c.CheckoutId, c.Speed, c.Total, c.Fee, c.TotalCharged, DateOnly.FromDateTime(c.ArrivesOn), c.TransferId);
}

/// <summary>
/// Confirms a bank payment with one of the offered speeds and charges the bank account
/// </summary>
public class ConfirmBankPaymentEndpoint : IEndpoint<IResult, ConfirmRequest, BankPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/checkout/bank-payment/{checkoutId}/confirm",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, Guid checkoutId, ConfirmBody body, BankPaymentService service) =>
            {
                return await HandleAsync(new ConfirmRequest
                {
                    BuyerId = BankEndpointSupport.Caller(user),
                    CheckoutId = checkoutId,
                    Speed = body.Speed,
                }, service);
            })
            .Produces<ConfirmResponse>()
            .WithTags("BankEndpoints");
    }

    public async Task<IResult> HandleAsync(ConfirmRequest request, BankPaymentService service)
    {
        var confirmation = await service.ConfirmAsync(request.BuyerId, request.CheckoutId, request.Speed, default);
        return Results.Ok(ConfirmResponse.From(confirmation));
    }
}

public class CancelOrderRequest : BaseRequest
{
    public string BuyerId { get; init; } = string.Empty;
    public int OrderId { get; init; }
}

public record CancelOrderResponse(int OrderId, string Status, decimal Refunded, string Method);

/// <summary>
/// Cancels an order paid by bank, before it ships, and gives the shopper all their money back
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, BankPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, int orderId, BankPaymentService service) =>
            {
                return await HandleAsync(new CancelOrderRequest { BuyerId = BankEndpointSupport.Caller(user), OrderId = orderId }, service);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("BankEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, BankPaymentService service)
    {
        var result = await service.CancelOrderAsync(request.BuyerId, request.OrderId, default);
        return Results.Ok(new CancelOrderResponse(result.OrderId, result.Status, result.Refunded, result.Method));
    }
}
