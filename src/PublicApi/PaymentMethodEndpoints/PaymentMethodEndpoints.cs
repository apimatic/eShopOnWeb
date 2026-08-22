using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreatePaymentMethodRequest request, HttpContext httpContext, ISavedPaymentMethodService service) =>
            {
                request.BuyerId = PaymentHttp.BuyerId(httpContext);
                return await HandleAsync(request, service);
            })
            .Produces<PaymentMethodResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedPaymentMethodService service)
    {
        try
        {
            var saved = await service.SaveCardAsync(request.BuyerId, request.Card.ToCardPaymentSource());
            return Results.Created($"api/payment-methods/{saved.Id}", PaymentMethodResponse.From(saved));
        }
        catch (System.Exception ex)
        {
            return PaymentHttp.FromException(ex);
        }
    }
}

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, string, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext, ISavedPaymentMethodService service) =>
            {
                return await HandleAsync(PaymentHttp.BuyerId(httpContext), service);
            })
            .Produces<PaymentMethodListResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(string buyerId, ISavedPaymentMethodService service)
    {
        try
        {
            var methods = await service.ListAsync(buyerId);
            return Results.Ok(new PaymentMethodListResponse
            {
                PaymentMethods = methods.Select(PaymentMethodResponse.From).ToList()
            });
        }
        catch (System.Exception ex)
        {
            return PaymentHttp.FromException(ex);
        }
    }
}

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId, HttpContext httpContext, ISavedPaymentMethodService service) =>
            {
                return await HandleAsync(new DeletePaymentMethodRequest
                {
                    PaymentMethodId = paymentMethodId,
                    BuyerId = PaymentHttp.BuyerId(httpContext)
                }, service);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, ISavedPaymentMethodService service)
    {
        try
        {
            await service.DeleteAsync(request.BuyerId, request.PaymentMethodId);
            return Results.NoContent();
        }
        catch (System.Exception ex)
        {
            return PaymentHttp.FromException(ex);
        }
    }
}

public class CreatePaymentMethodRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public CardRequest Card { get; set; } = new();
}

public class DeletePaymentMethodRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public int PaymentMethodId { get; set; }
}

public class PaymentMethodListResponse
{
    public List<PaymentMethodResponse> PaymentMethods { get; set; } = new();
}

public class PaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string LastDigits { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }

    public static PaymentMethodResponse From(SavedPaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        LastDigits = method.LastDigits,
        Brand = method.Brand,
        Expiry = method.Expiry,
        CardholderName = method.CardholderName
    };
}
