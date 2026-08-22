using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ISavedPaymentMethodService vault, HttpContext http) =>
            {
                request.BuyerId = http.User.RequireBuyerId();
                return await HandleAsync(request, vault);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedPaymentMethodService vault)
    {
        var method = await vault.SaveCardAsync(request.BuyerId!, CardInputMapper.Map(request.Card), request.Alias, default);
        var body = CreatePaymentMethodResponse.From(method);
        return Results.Created($"api/payment-methods/{method.Id}", body);
    }
}

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISavedPaymentMethodService vault, HttpContext http) =>
            {
                var methods = await vault.ListAsync(http.User.RequireBuyerId(), default);
                return Results.Ok(new ListPaymentMethodsResponse
                {
                    PaymentMethods = methods.Select(PaymentMethodResponse.From).ToList()
                });
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(ISavedPaymentMethodService vault) =>
        Task.FromResult(Results.Ok());
}

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ISavedPaymentMethodService vault, HttpContext http) =>
            {
                return await HandleAsync(new DeletePaymentMethodRequest
                {
                    PaymentMethodId = paymentMethodId,
                    BuyerId = http.User.RequireBuyerId()
                }, vault);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, ISavedPaymentMethodService vault)
    {
        await vault.DeleteAsync(request.BuyerId!, request.PaymentMethodId, default);
        return Results.NoContent();
    }
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public string? BuyerId { get; set; }
    public string? Alias { get; set; }
    public CardDetailsRequest Card { get; set; } = new();
}

public class DeletePaymentMethodRequest : BaseRequest
{
    public string? BuyerId { get; set; }
    public int PaymentMethodId { get; set; }
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
    public string? Alias { get; set; }

    public static CreatePaymentMethodResponse From(PaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        Brand = method.Brand,
        Last4 = method.Last4,
        Expiry = method.Expiry,
        Alias = method.Alias
    };
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public List<PaymentMethodResponse> PaymentMethods { get; set; } = new();
}

public class PaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
    public string? Alias { get; set; }

    public static PaymentMethodResponse From(PaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        Brand = method.Brand,
        Last4 = method.Last4,
        Expiry = method.Expiry,
        Alias = method.Alias
    };
}
