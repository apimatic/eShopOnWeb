using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodRequest : BaseRequest
{
    public PayCardRequest Card { get; set; } = new();
}

public class PaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastDigits { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

public class SavePaymentMethodResponse : BaseResponse
{
    public SavePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public SavePaymentMethodResponse() { }
    public int PaymentMethodId { get; set; }
    public PaymentMethodResponse PaymentMethod { get; set; } = new();
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public List<PaymentMethodResponse> PaymentMethods { get; set; } = new();
}

public static class PaymentMethodResponseMapper
{
    public static PaymentMethodResponse Map(SavedPaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        Brand = method.Brand ?? "CARD",
        LastDigits = method.LastDigits ?? string.Empty,
        Expiry = method.Expiry,
        CardholderName = method.CardholderName
    };
}

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SavePaymentMethodRequest request, HttpContext httpContext, ISavedPaymentMethodService service) =>
            {
                return await HandleAsync(request, httpContext, service);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(SavePaymentMethodRequest request, ISavedPaymentMethodService service) =>
        throw new NotSupportedException();

    private async Task<IResult> HandleAsync(SavePaymentMethodRequest request, HttpContext httpContext, ISavedPaymentMethodService service)
    {
        var buyerId = CreateOrderEndpoint.BuyerId(httpContext);
        var saved = await service.SaveCardAsync(buyerId, PayOrderEndpoint.MapCard(request.Card));
        var response = new SavePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = saved.Id,
            PaymentMethod = PaymentMethodResponseMapper.Map(saved)
        };
        return Results.Created($"api/payment-methods/{saved.Id}", response);
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
                return await HandleAsync(CreateOrderEndpoint.BuyerId(httpContext), service);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(string buyerId, ISavedPaymentMethodService service)
    {
        var methods = await service.ListForBuyerAsync(buyerId);
        return Results.Ok(new ListPaymentMethodsResponse
        {
            PaymentMethods = methods.Select(PaymentMethodResponseMapper.Map).ToList()
        });
    }
}

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, int, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId, HttpContext httpContext, ISavedPaymentMethodService service) =>
            {
                await service.DeleteAsync(CreateOrderEndpoint.BuyerId(httpContext), paymentMethodId);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(int paymentMethodId, ISavedPaymentMethodService service) =>
        throw new NotSupportedException();
}
