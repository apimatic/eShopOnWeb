using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
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

public class SavePaymentMethodRequest : BaseRequest
{
    public CardRequestDto Card { get; set; } = new();

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class SavePaymentMethodResponse : BaseResponse
{
    public string PaymentMethodId { get; set; } = string.Empty;
    public string? Last4 { get; set; }
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
    public string? Name { get; set; }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public List<SavedPaymentMethodDto> PaymentMethods { get; set; } = new();
}

public class SavedPaymentMethodDto
{
    public string PaymentMethodId { get; set; } = string.Empty;
    public string? Last4 { get; set; }
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
    public string? Name { get; set; }
}

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, HttpContext http, IPaymentMethodService paymentMethods) =>
            {
                request.BuyerId = CurrentUser.Require(http);
                return await HandleAsync(request, paymentMethods);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, IPaymentMethodService paymentMethods)
    {
        var saved = await paymentMethods.SaveCardAsync(request.BuyerId, OrderDtoMapper.ToCardSource(request.Card), default);
        var response = new SavePaymentMethodResponse
        {
            PaymentMethodId = saved.PaymentTokenId,
            Last4 = saved.LastDigits,
            Brand = saved.Brand,
            Expiry = saved.Expiry,
            Name = saved.Name
        };
        return Results.Created($"api/payment-methods/{saved.PaymentTokenId}", response);
    }
}

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, string, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, IPaymentMethodService paymentMethods) =>
            {
                return await HandleAsync(CurrentUser.Require(http), paymentMethods);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(string buyerId, IPaymentMethodService paymentMethods)
    {
        var list = await paymentMethods.ListAsync(buyerId, default);
        return Results.Ok(new ListPaymentMethodsResponse
        {
            PaymentMethods = list.Select(p => new SavedPaymentMethodDto
            {
                PaymentMethodId = p.CardId ?? string.Empty,
                Last4 = p.Last4,
                Brand = p.Brand,
                Expiry = p.Expiry,
                Name = p.Alias
            }).ToList()
        });
    }
}

public class DeletePaymentMethodRequest : BaseRequest
{
    public string PaymentMethodId { get; set; } = string.Empty;
    public string BuyerId { get; set; } = string.Empty;
}

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string paymentMethodId, HttpContext http, IPaymentMethodService paymentMethods) =>
            {
                return await HandleAsync(new DeletePaymentMethodRequest
                {
                    PaymentMethodId = paymentMethodId,
                    BuyerId = CurrentUser.Require(http)
                }, paymentMethods);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IPaymentMethodService paymentMethods)
    {
        await paymentMethods.DeleteAsync(request.BuyerId, request.PaymentMethodId, default);
        return Results.NoContent();
    }
}
