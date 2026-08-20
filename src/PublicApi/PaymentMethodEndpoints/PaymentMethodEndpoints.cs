using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodRequest : BaseRequest
{
    public CardPaymentRequest Card { get; set; } = new();
    internal string? BuyerId { get; set; }
}

public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SavePaymentMethodRequest request, HttpContext http, IPaymentMethodService service) =>
            {
                request.BuyerId = CreateOrderEndpoint.RequireBuyerId(http);
                return await HandleAsync(request, service);
            })
            .Produces<PaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, IPaymentMethodService service)
    {
        if (request.Card is null)
        {
            throw new PaymentException("Card details are required.");
        }

        var saved = await service.SaveCardAsync(request.BuyerId!, CardPaymentMapper.ToCardDetails(request.Card));
        var response = PaymentMethodResponse.From(saved);
        return Results.Created($"api/payment-methods/{response.PaymentMethodId}", response);
    }
}

public class ListPaymentMethodsRequest : BaseRequest
{
    internal string BuyerId { get; set; } = string.Empty;
}

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext http, IPaymentMethodService service) =>
            {
                return await HandleAsync(new ListPaymentMethodsRequest { BuyerId = CreateOrderEndpoint.RequireBuyerId(http) }, service);
            })
            .Produces<PaymentMethodListResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, IPaymentMethodService service)
    {
        var methods = await service.ListAsync(request.BuyerId);
        return Results.Ok(new PaymentMethodListResponse
        {
            PaymentMethods = methods.Select(PaymentMethodResponse.From).ToList()
        });
    }
}

public class DeletePaymentMethodRequest : BaseRequest
{
    internal int PaymentMethodId { get; set; }
    internal string BuyerId { get; set; } = string.Empty;
}

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId, HttpContext http, IPaymentMethodService service) =>
            {
                return await HandleAsync(new DeletePaymentMethodRequest
                {
                    PaymentMethodId = paymentMethodId,
                    BuyerId = CreateOrderEndpoint.RequireBuyerId(http)
                }, service);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IPaymentMethodService service)
    {
        await service.DeleteAsync(request.BuyerId, request.PaymentMethodId);
        return Results.NoContent();
    }
}
