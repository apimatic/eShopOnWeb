using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Banking;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BankEndpoints;

public class StartBankPaymentBody
{
    public int? ConnectionId { get; init; }
    public List<CheckoutItemInput>? Items { get; init; }
    public decimal? ShippingCost { get; init; }
    /// <summary>Optional: the name on the bank account; defaults to the shopper's user name.</summary>
    public string? LegalName { get; init; }
    public ShipToInput? ShipTo { get; init; }
}

public class StartBankPaymentRequest : BaseRequest
{
    public ClaimsPrincipal User { get; init; } = null!;
    public StartBankPaymentBody? Body { get; init; }
}

/// <summary>
/// Starts a bank payment for an order: answers the order total and the payment speeds on offer.
/// </summary>
public class StartBankPaymentEndpoint : IEndpoint<IResult, StartBankPaymentRequest, BankPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/checkout/bank-payment",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, StartBankPaymentBody? body, BankPaymentService payments) =>
            {
                return await HandleAsync(new StartBankPaymentRequest { User = user, Body = body }, payments);
            })
            .Produces<BankPaymentDto>(StatusCodes.Status201Created)
            .WithTags("CheckoutEndpoints");
    }

    public async Task<IResult> HandleAsync(StartBankPaymentRequest request, BankPaymentService payments)
    {
        var b = request.Body ?? throw new BankingException(400, "A body is required.");
        var result = await payments.StartAsync(request.User, b.ConnectionId, b.Items, b.ShippingCost, b.LegalName, b.ShipTo);
        return Results.Created($"api/checkout/bank-payment/{result.CheckoutId}", result);
    }
}

public class ChangeShippingBody
{
    public decimal? ShippingCost { get; init; }
}

public class ChangeShippingRequest : BaseRequest
{
    public ClaimsPrincipal User { get; init; } = null!;
    public Guid CheckoutId { get; init; }
    public decimal? ShippingCost { get; init; }
}

/// <summary>
/// Changes the shipping cost of a started payment; confirming then charges the new total.
/// </summary>
public class ChangeShippingEndpoint : IEndpoint<IResult, ChangeShippingRequest, BankPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/checkout/bank-payment/{checkoutId}/shipping",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, Guid checkoutId, ChangeShippingBody? body, BankPaymentService payments) =>
            {
                return await HandleAsync(new ChangeShippingRequest { User = user, CheckoutId = checkoutId, ShippingCost = body?.ShippingCost }, payments);
            })
            .Produces<BankPaymentDto>()
            .WithTags("CheckoutEndpoints");
    }

    public async Task<IResult> HandleAsync(ChangeShippingRequest request, BankPaymentService payments)
    {
        return Results.Ok(await payments.ChangeShippingAsync(request.User, request.CheckoutId, request.ShippingCost));
    }
}

public class ConfirmBankPaymentBody
{
    /// <summary>"standard" or "express".</summary>
    public string? Speed { get; init; }
}

public class ConfirmBankPaymentRequest : BaseRequest
{
    public ClaimsPrincipal User { get; init; } = null!;
    public Guid CheckoutId { get; init; }
    public string? Speed { get; init; }
}

/// <summary>
/// Confirms a bank payment with one of the offered speeds and charges the bank.
/// </summary>
public class ConfirmBankPaymentEndpoint : IEndpoint<IResult, ConfirmBankPaymentRequest, BankPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/checkout/bank-payment/{checkoutId}/confirm",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, Guid checkoutId, ConfirmBankPaymentBody? body, BankPaymentService payments) =>
            {
                return await HandleAsync(new ConfirmBankPaymentRequest { User = user, CheckoutId = checkoutId, Speed = body?.Speed }, payments);
            })
            .Produces<OrderConfirmationDto>()
            .WithTags("CheckoutEndpoints");
    }

    public async Task<IResult> HandleAsync(ConfirmBankPaymentRequest request, BankPaymentService payments)
    {
        return Results.Ok(await payments.ConfirmAsync(request.User, request.CheckoutId, request.Speed));
    }
}

public class CancelOrderRequest : BaseRequest
{
    public ClaimsPrincipal User { get; init; } = null!;
    public int OrderId { get; init; }
}

/// <summary>
/// Cancels an order paid by bank, before it ships, and gives all the money back.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, BankPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, int orderId, BankPaymentService payments) =>
            {
                return await HandleAsync(new CancelOrderRequest { User = user, OrderId = orderId }, payments);
            })
            .Produces<OrderCancellationDto>()
            .WithTags("CheckoutEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, BankPaymentService payments)
    {
        return Results.Ok(await payments.CancelAsync(request.User, request.OrderId));
    }
}
