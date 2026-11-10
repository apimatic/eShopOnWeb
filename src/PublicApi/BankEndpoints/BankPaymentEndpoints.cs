using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Plaid;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BankEndpoints;

public class StartBankPaymentRequest
{
    public int? ConnectionId { get; set; }
    public List<CheckoutItemRequest>? Items { get; set; }
    public decimal? ShippingCost { get; set; }
    public ShipToRequest? ShipTo { get; set; }
}

public class StartBankPaymentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/checkout/bank-payment",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (StartBankPaymentRequest? request, ClaimsPrincipal user, BankPaymentService service, CancellationToken ct) =>
            {
                var quote = await service.StartAsync(user.Buyer(), request?.ConnectionId, request?.Items, request?.ShippingCost, request?.ShipTo, ct);
                return Results.Ok(quote);
            })
            .Produces<BankPaymentQuote>()
            .WithTags("BankEndpoints");
    }
}

public class ChangeShippingRequest
{
    public decimal? ShippingCost { get; set; }
}

public class ChangeBankPaymentShippingEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/checkout/bank-payment/{checkoutId}/shipping",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string checkoutId, ChangeShippingRequest? request, ClaimsPrincipal user, BankPaymentService service, CancellationToken ct) =>
                Results.Ok(await service.ChangeShippingAsync(user.Buyer(), checkoutId, request?.ShippingCost, ct)))
            .Produces<BankPaymentQuote>()
            .WithTags("BankEndpoints");
    }
}

public class ConfirmBankPaymentRequest
{
    /// <summary>"standard" or "express".</summary>
    public string? Speed { get; set; }
}

public class ConfirmBankPaymentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/checkout/bank-payment/{checkoutId}/confirm",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string checkoutId, ConfirmBankPaymentRequest? request, ClaimsPrincipal user, BankPaymentService service, CancellationToken ct) =>
                Results.Ok(await service.ConfirmAsync(user.Buyer(), checkoutId, request?.Speed, ct)))
            .Produces<OrderConfirmation>()
            .WithTags("BankEndpoints");
    }
}

public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, BankPaymentService service, CancellationToken ct) =>
                Results.Ok(await service.CancelOrderAsync(user.Buyer(), orderId, ct)))
            .Produces<OrderCancellation>()
            .WithTags("BankEndpoints");
    }
}

public class ShipOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/ship",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, BankPaymentService service, CancellationToken ct) =>
            {
                await service.ShipOrderAsync(orderId, ct);
                return Results.NoContent();
            })
            .Produces(204)
            .WithTags("BankEndpoints");
    }
}

public class BankActivityEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/support/shoppers/{email}/bank-activity",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string email, BankActivityService service, CancellationToken ct) =>
                Results.Ok(new { email, transactions = await service.GetActivityAsync(email, ct) }))
            .Produces(200)
            .WithTags("BankEndpoints");
    }
}

public class BankTransactionsBetweenEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/support/shoppers/{email}/transactions",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string email, string? from, string? to, BankActivityService service, CancellationToken ct) =>
                Results.Ok(new { email, from, to, transactions = await service.GetBetweenAsync(email, from, to, ct) }))
            .Produces(200)
            .WithTags("BankEndpoints");
    }
}

public class PortfolioEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/portfolio",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, PortfolioService service, CancellationToken ct) =>
                Results.Ok(new { positions = await service.GetPositionsAsync(user.Buyer(), ct) }))
            .Produces(200)
            .WithTags("BankEndpoints");
    }
}
