using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Banking;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.CheckoutEndpoints;

public class BankPaymentItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class StartBankPaymentRequest : BaseRequest
{
    public int ConnectionId { get; set; }
    public List<BankPaymentItem>? Items { get; set; }
    public decimal? ShippingCost { get; set; }
}

public class ChangeShippingRequest : BaseRequest
{
    public decimal? ShippingCost { get; set; }
}

public class ConfirmBankPaymentRequest : BaseRequest
{
    public string? Speed { get; set; }
}

public class BankPaymentResponse : BaseResponse
{
    public string CheckoutId { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal ShippingCost { get; set; }
    public decimal Total { get; set; }

    /// <summary>The payment speeds available for this order: "standard" and/or "express".</summary>
    public IReadOnlyList<string> Options { get; set; } = Array.Empty<string>();

    /// <summary>What express costs on top of the total.</summary>
    public decimal ExpressFee { get; set; }

    public static BankPaymentResponse From(CheckoutQuote quote) => new()
    {
        CheckoutId = quote.CheckoutId,
        Subtotal = quote.Subtotal,
        ShippingCost = quote.Shipping,
        Total = quote.Total,
        Options = quote.Options,
        ExpressFee = quote.ExpressFee,
    };
}

public class OrderConfirmationResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string CheckoutId { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public decimal Fee { get; set; }
    public decimal Charged { get; set; }
    public string Speed { get; set; } = string.Empty;

    /// <summary>The date the money is expected to arrive (yyyy-MM-dd).</summary>
    public DateOnly? ArrivesOn { get; set; }
}

/// <summary>
/// Starts a bank payment for an order
/// </summary>
public class StartBankPaymentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/checkout/bank-payment",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (StartBankPaymentRequest request, ClaimsPrincipal user, CheckoutService checkout, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, user, checkout, cancellationToken);
            })
            .Produces<BankPaymentResponse>(StatusCodes.Status201Created)
            .WithTags("CheckoutEndpoints");
    }

    public async Task<IResult> HandleAsync(StartBankPaymentRequest request, ClaimsPrincipal user, CheckoutService checkout,
        CancellationToken cancellationToken)
    {
        var lines = request.Items?.ConvertAll(i => new RequestedLine(i.CatalogItemId, i.Quantity));
        var quote = await checkout.StartAsync(user.ShopperId(), request.ConnectionId, lines, request.ShippingCost, cancellationToken);
        return Results.Created($"api/checkout/bank-payment/{quote.CheckoutId}", BankPaymentResponse.From(quote));
    }
}

/// <summary>
/// Changes the shipping cost of a started bank payment
/// </summary>
public class ChangeShippingEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/checkout/bank-payment/{checkoutId}/shipping",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string checkoutId, ChangeShippingRequest request, ClaimsPrincipal user, CheckoutService checkout, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(checkoutId, request, user, checkout, cancellationToken);
            })
            .Produces<BankPaymentResponse>()
            .WithTags("CheckoutEndpoints");
    }

    public async Task<IResult> HandleAsync(string checkoutId, ChangeShippingRequest request, ClaimsPrincipal user,
        CheckoutService checkout, CancellationToken cancellationToken)
    {
        var quote = await checkout.ChangeShippingAsync(user.ShopperId(), checkoutId, request.ShippingCost, cancellationToken);
        return Results.Ok(BankPaymentResponse.From(quote));
    }
}

/// <summary>
/// Confirms a bank payment with one of the offered speeds, and charges the bank
/// </summary>
public class ConfirmBankPaymentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/checkout/bank-payment/{checkoutId}/confirm",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string checkoutId, ConfirmBankPaymentRequest request, HttpContext http, CheckoutService checkout, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(checkoutId, request, http, checkout, cancellationToken);
            })
            .Produces<OrderConfirmationResponse>()
            .WithTags("CheckoutEndpoints");
    }

    public async Task<IResult> HandleAsync(string checkoutId, ConfirmBankPaymentRequest request, HttpContext http,
        CheckoutService checkout, CancellationToken cancellationToken)
    {
        var device = new DeviceInfo(http.Connection.RemoteIpAddress?.ToString(), http.Request.Headers.UserAgent.ToString());
        var confirmation = await checkout.ConfirmAsync(http.User.ShopperId(), checkoutId, request.Speed, device, cancellationToken);
        return Results.Ok(new OrderConfirmationResponse
        {
            OrderId = confirmation.OrderId,
            CheckoutId = confirmation.CheckoutId,
            Total = confirmation.Total,
            Fee = confirmation.Fee,
            Charged = confirmation.Charged,
            Speed = confirmation.Speed,
            ArrivesOn = confirmation.ArrivesOn,
        });
    }
}
