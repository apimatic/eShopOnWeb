using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest
{
    public CardDetailsRequest Card { get; set; } = new();
}

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreatePaymentMethodRequest request, IOrderCheckoutService checkout, ClaimsPrincipal user) =>
            {
                var saved = await checkout.SavePaymentMethodAsync(user.GetBuyerId(), request.Card.ToCardSource());
                var body = OrderResponseMapper.From(saved);
                return Results.Created($"api/payment-methods/{body.PaymentMethodId}", body);
            })
            .Produces<PaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IOrderCheckoutService checkout) =>
        Task.FromResult(Results.BadRequest());
}

public class ListPaymentMethodsResponse
{
    public List<PaymentMethodResponse> PaymentMethods { get; set; } = new();
}

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IOrderCheckoutService checkout, ClaimsPrincipal user) =>
            {
                var methods = await checkout.ListPaymentMethodsAsync(user.GetBuyerId());
                return Results.Ok(new ListPaymentMethodsResponse
                {
                    PaymentMethods = methods.Select(OrderResponseMapper.From).ToList()
                });
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderCheckoutService checkout) => Task.FromResult(Results.BadRequest());
}

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId, IOrderCheckoutService checkout, ClaimsPrincipal user) =>
            {
                await checkout.DeletePaymentMethodAsync(user.GetBuyerId(), paymentMethodId);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderCheckoutService checkout) => Task.FromResult(Results.BadRequest());
}
