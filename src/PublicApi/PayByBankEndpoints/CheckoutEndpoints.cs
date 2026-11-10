using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.PayByBank;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PayByBankEndpoints;

public record StartBankPaymentBody(int? ConnectionId, List<BankPaymentItemLine>? Items, decimal? ShippingCost);

public record StartBankPaymentRequest(string UserId, StartBankPaymentBody? Body);

/// <summary>Starts a pay-by-bank checkout: prices the order and answers which payment speeds are available.</summary>
public class StartBankPaymentEndpoint : IEndpoint<IResult, StartBankPaymentRequest, BankPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/checkout/bank-payment",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, BankPaymentService payments, StartBankPaymentBody? body) =>
            {
                return await HandleAsync(new StartBankPaymentRequest(Caller.Id(user), body), payments);
            })
            .Produces<CheckoutDto>(StatusCodes.Status201Created)
            .WithTags("PayByBankEndpoints");
    }

    public async Task<IResult> HandleAsync(StartBankPaymentRequest request, BankPaymentService payments)
    {
        var body = request.Body ?? throw Fail.BadRequest("A request body is required.");
        var checkout = await payments.StartAsync(request.UserId, body.ConnectionId, body.Items, body.ShippingCost);
        return Results.Created($"api/checkout/bank-payment/{checkout.CheckoutId}", checkout);
    }
}

public record ChangeShippingBody(decimal? ShippingCost);

public record ChangeShippingRequest(string UserId, Guid CheckoutId, ChangeShippingBody? Body);

/// <summary>Re-prices a started checkout with a new shipping cost; confirming then charges the new total.</summary>
public class ChangeBankPaymentShippingEndpoint : IEndpoint<IResult, ChangeShippingRequest, BankPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/checkout/bank-payment/{checkoutId}/shipping",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (Guid checkoutId, ClaimsPrincipal user, BankPaymentService payments, ChangeShippingBody? body) =>
            {
                return await HandleAsync(new ChangeShippingRequest(Caller.Id(user), checkoutId, body), payments);
            })
            .Produces<CheckoutDto>()
            .WithTags("PayByBankEndpoints");
    }

    public async Task<IResult> HandleAsync(ChangeShippingRequest request, BankPaymentService payments) =>
        Results.Ok(await payments.ChangeShippingAsync(request.UserId, request.CheckoutId, request.Body?.ShippingCost));
}

public record ConfirmBankPaymentBody(string? Speed);

public record ConfirmBankPaymentRequest(string UserId, Guid CheckoutId, ConfirmBankPaymentBody? Body);

/// <summary>Confirms a started checkout with one of the offered speeds and charges the bank.</summary>
public class ConfirmBankPaymentEndpoint : IEndpoint<IResult, ConfirmBankPaymentRequest, BankPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/checkout/bank-payment/{checkoutId}/confirm",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (Guid checkoutId, ClaimsPrincipal user, BankPaymentService payments, ConfirmBankPaymentBody? body) =>
            {
                return await HandleAsync(new ConfirmBankPaymentRequest(Caller.Id(user), checkoutId, body), payments);
            })
            .Produces<ConfirmationDto>()
            .WithTags("PayByBankEndpoints");
    }

    public async Task<IResult> HandleAsync(ConfirmBankPaymentRequest request, BankPaymentService payments) =>
        Results.Ok(await payments.ConfirmAsync(request.UserId, request.CheckoutId, request.Body?.Speed));
}

public record CancelOrderRequest(string UserId, int OrderId);

/// <summary>Cancels an order paid by bank before it ships and returns all the money.</summary>
public class CancelBankOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, BankPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, BankPaymentService payments) =>
            {
                return await HandleAsync(new CancelOrderRequest(Caller.Id(user), orderId), payments);
            })
            .Produces<CancellationDto>()
            .WithTags("PayByBankEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, BankPaymentService payments) =>
        Results.Ok(await payments.CancelOrderAsync(request.UserId, request.OrderId));
}

public record BankActivityRequest(string Email);

public record BankActivityResponse(IReadOnlyList<ActivityTransactionDto> Transactions);

/// <summary>Support: the shop's synced copy of the activity on every bank a shopper connected.</summary>
public class GetBankActivityEndpoint : IEndpoint<IResult, BankActivityRequest, BankActivityService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/support/shoppers/{email}/bank-activity",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string email, BankActivityService activity) =>
            {
                return await HandleAsync(new BankActivityRequest(email), activity);
            })
            .Produces<BankActivityResponse>()
            .WithTags("PayByBankEndpoints");
    }

    public async Task<IResult> HandleAsync(BankActivityRequest request, BankActivityService activity) =>
        Results.Ok(new BankActivityResponse(await activity.GetActivityAsync(request.Email)));
}

public record BankTransactionsRequest(string Email, string? From, string? To);

public record BankTransactionsResponse(IReadOnlyList<LookupTransactionDto> Transactions);

/// <summary>Support: transactions on any connected bank between two dates, straight from the bank.</summary>
public class GetBankTransactionsEndpoint : IEndpoint<IResult, BankTransactionsRequest, BankActivityService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/support/shoppers/{email}/transactions",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string email, string? from, string? to, BankActivityService activity) =>
            {
                return await HandleAsync(new BankTransactionsRequest(email, from, to), activity);
            })
            .Produces<BankTransactionsResponse>()
            .WithTags("PayByBankEndpoints");
    }

    public async Task<IResult> HandleAsync(BankTransactionsRequest request, BankActivityService activity) =>
        Results.Ok(new BankTransactionsResponse(await activity.GetTransactionsAsync(request.Email, request.From, request.To)));
}
