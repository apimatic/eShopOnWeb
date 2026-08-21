using System;
using System.Collections.Generic;
using System.Linq;
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

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardDetailsRequest Card { get; set; } = new();
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public CreatePaymentMethodResponse() { }

    public int PaymentMethodId { get; set; }
    public SavedPaymentMethodDto PaymentMethod { get; set; } = new();
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public List<SavedPaymentMethodDto> PaymentMethods { get; set; } = new();
}

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ISavedPaymentMethodService methods, HttpContext httpContext) =>
            {
                var card = OrderDtoMapper.ToCardInput(request.Card)
                           ?? throw new ApplicationCore.Exceptions.PaymentException(400, "Card details are required.");
                var saved = await methods.SaveAsync(httpContext.RequireBuyerId(), card, httpContext.RequestAborted);
                var response = new CreatePaymentMethodResponse(request.CorrelationId())
                {
                    PaymentMethodId = saved.Id,
                    PaymentMethod = SavedPaymentMethodDto.From(saved)
                };
                return Results.Created($"api/payment-methods/{saved.Id}", response);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedPaymentMethodService methods)
        => Task.FromResult(Results.BadRequest());
}

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISavedPaymentMethodService methods, HttpContext httpContext) =>
            {
                var saved = await methods.ListAsync(httpContext.RequireBuyerId(), httpContext.RequestAborted);
                return Results.Ok(new ListPaymentMethodsResponse
                {
                    PaymentMethods = saved.Select(SavedPaymentMethodDto.From).ToList()
                });
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(ISavedPaymentMethodService methods) => Task.FromResult(Results.BadRequest());
}

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, int, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ISavedPaymentMethodService methods, HttpContext httpContext) =>
            {
                await methods.DeleteAsync(httpContext.RequireBuyerId(), paymentMethodId, httpContext.RequestAborted);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(int paymentMethodId, ISavedPaymentMethodService methods)
        => Task.FromResult(Results.BadRequest());
}
