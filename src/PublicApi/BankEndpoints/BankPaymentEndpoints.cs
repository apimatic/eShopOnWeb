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

public class StartBankPaymentRequest : BaseRequest
{
    public string ShopperName { get; set; } = "";

    /// <summary>One of the shopper's own bank connections.</summary>
    public int? ConnectionId { get; set; }

    public List<CheckoutLineRequest>? Items { get; set; }
    public decimal? ShippingCost { get; set; }
    public ShipToRequest? ShipTo { get; set; }
}

public class BankPaymentResponse : BaseResponse
{
    public BankPaymentResponse(Guid correlationId) : base(correlationId)
    {
    }

    public BankPaymentResponse()
    {
    }

    public Guid CheckoutId { get; set; }
    public decimal Subtotal { get; set; }
    public decimal ShippingCost { get; set; }

    /// <summary>The order total: items and shipping.</summary>
    public decimal Total { get; set; }

    /// <summary>What express costs on top of the total.</summary>
    public decimal ExpressFee { get; set; }

    /// <summary>The payment speeds available for this order: standard, express.</summary>
    public List<string> Options { get; set; } = new();

    public static BankPaymentResponse From(Guid correlationId, CheckoutView view) => new(correlationId)
    {
        CheckoutId = view.CheckoutId,
        Subtotal = view.Subtotal,
        ShippingCost = view.ShippingCost,
        Total = view.Total,
        ExpressFee = view.ExpressFee,
        Options = view.Options,
    };
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
            (StartBankPaymentRequest request, ClaimsPrincipal user, BankPaymentService payments) =>
            {
                request.ShopperName = BankEndpointSupport.ShopperOf(user);
                return await HandleAsync(request, payments);
            })
            .Produces<BankPaymentResponse>(StatusCodes.Status201Created)
            .WithTags("BankEndpoints");
    }

    public async Task<IResult> HandleAsync(StartBankPaymentRequest request, BankPaymentService payments)
    {
        var view = await payments.StartAsync(request.ShopperName, request.ConnectionId, request.Items, request.ShippingCost, request.ShipTo);
        return Results.Created($"api/checkout/bank-payment/{view.CheckoutId}", BankPaymentResponse.From(request.CorrelationId(), view));
    }
}

public class ChangeShippingRequest : BaseRequest
{
    public string ShopperName { get; set; } = "";
    public Guid CheckoutId { get; set; }
    public decimal? ShippingCost { get; set; }
}

/// <summary>
/// Changes the shipping cost of a started bank payment; confirming then charges the new total
/// </summary>
public class ChangeShippingEndpoint : IEndpoint<IResult, ChangeShippingRequest, BankPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/checkout/bank-payment/{checkoutId}/shipping",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (Guid checkoutId, ChangeShippingRequest request, ClaimsPrincipal user, BankPaymentService payments) =>
            {
                request.ShopperName = BankEndpointSupport.ShopperOf(user);
                request.CheckoutId = checkoutId;
                return await HandleAsync(request, payments);
            })
            .Produces<BankPaymentResponse>()
            .WithTags("BankEndpoints");
    }

    public async Task<IResult> HandleAsync(ChangeShippingRequest request, BankPaymentService payments)
    {
        var view = await payments.ChangeShippingAsync(request.ShopperName, request.CheckoutId, request.ShippingCost);
        return Results.Ok(BankPaymentResponse.From(request.CorrelationId(), view));
    }
}

public class ConfirmBankPaymentRequest : BaseRequest
{
    public string ShopperName { get; set; } = "";
    public Guid CheckoutId { get; set; }

    /// <summary>One of the offered speeds: standard or express.</summary>
    public string? Speed { get; set; }
}

public class ConfirmBankPaymentResponse : BaseResponse
{
    public ConfirmBankPaymentResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ConfirmBankPaymentResponse()
    {
    }

    public int OrderId { get; set; }
    public Guid CheckoutId { get; set; }
    public string Speed { get; set; } = "";
    public decimal Total { get; set; }

    /// <summary>What express cost on top of the total; zero for standard.</summary>
    public decimal Fee { get; set; }

    /// <summary>What the bank was charged: total plus fee.</summary>
    public decimal Charged { get; set; }

    /// <summary>The date the money is expected to arrive.</summary>
    public DateOnly ArrivesOn { get; set; }
    public string TransferId { get; set; } = "";
}

/// <summary>
/// Confirms a bank payment with one of the offered speeds and charges the bank
/// </summary>
public class ConfirmBankPaymentEndpoint : IEndpoint<IResult, ConfirmBankPaymentRequest, BankPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/checkout/bank-payment/{checkoutId}/confirm",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (Guid checkoutId, ConfirmBankPaymentRequest request, ClaimsPrincipal user, BankPaymentService payments) =>
            {
                request.ShopperName = BankEndpointSupport.ShopperOf(user);
                request.CheckoutId = checkoutId;
                return await HandleAsync(request, payments);
            })
            .Produces<ConfirmBankPaymentResponse>()
            .WithTags("BankEndpoints");
    }

    public async Task<IResult> HandleAsync(ConfirmBankPaymentRequest request, BankPaymentService payments)
    {
        var c = await payments.ConfirmAsync(request.ShopperName, request.CheckoutId, request.Speed);
        return Results.Ok(new ConfirmBankPaymentResponse(request.CorrelationId())
        {
            OrderId = c.OrderId,
            CheckoutId = c.CheckoutId,
            Speed = c.Speed,
            Total = c.Total,
            Fee = c.Fee,
            Charged = c.Charged,
            ArrivesOn = DateOnly.FromDateTime(c.ArrivesOn),
            TransferId = c.TransferId,
        });
    }
}

public class CancelOrderRequest : BaseRequest
{
    public string ShopperName { get; init; } = "";
    public int OrderId { get; init; }
}

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CancelOrderResponse()
    {
    }

    public int OrderId { get; set; }
    public string Status { get; set; } = "";

    /// <summary>What went back to the shopper: everything charged, fee included.</summary>
    public decimal Refunded { get; set; }
}

/// <summary>
/// Cancels an order paid by bank, before it ships, and gives all the money back
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, BankPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, BankPaymentService payments) =>
            {
                return await HandleAsync(new CancelOrderRequest { ShopperName = BankEndpointSupport.ShopperOf(user), OrderId = orderId }, payments);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("BankEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, BankPaymentService payments)
    {
        var result = await payments.CancelOrderAsync(request.ShopperName, request.OrderId);
        return Results.Ok(new CancelOrderResponse(request.CorrelationId())
        {
            OrderId = result.OrderId,
            Status = result.Status,
            Refunded = result.Refunded,
        });
    }
}
