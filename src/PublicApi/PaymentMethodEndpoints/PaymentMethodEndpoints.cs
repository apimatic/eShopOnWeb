using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ICheckoutPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreatePaymentMethodRequest request, ICheckoutPaymentService payments, HttpContext http) =>
            {
                var card = CardRequestMapper.ToCardDetails(request.Card);
                if (card == null)
                {
                    return Results.BadRequest(new { message = "Card details are required." });
                }

                var saved = await payments.SavePaymentMethodAsync(http.User.GetBuyerId(), card);
                var dto = OrderResponseMapper.ToDto(saved);
                return Results.Created($"api/payment-methods/{dto.PaymentMethodId}", new CreatePaymentMethodResponse
                {
                    PaymentMethodId = dto.PaymentMethodId,
                    PaymentMethod = dto
                });
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ICheckoutPaymentService payments) =>
        Task.FromResult(Results.StatusCode(StatusCodes.Status501NotImplemented));
}

public class CreatePaymentMethodRequest
{
    public CardRequestDto Card { get; set; } = new();
}

public class CreatePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public PaymentMethodDto PaymentMethod { get; set; } = new();
}

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ICheckoutPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ICheckoutPaymentService payments, HttpContext http) =>
            {
                var methods = await payments.ListPaymentMethodsAsync(http.User.GetBuyerId());
                return Results.Ok(new ListPaymentMethodsResponse
                {
                    PaymentMethods = methods.Select(OrderResponseMapper.ToDto).ToList()
                });
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(ICheckoutPaymentService payments) =>
        Task.FromResult(Results.StatusCode(StatusCodes.Status501NotImplemented));
}

public class ListPaymentMethodsResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, int, ICheckoutPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId, ICheckoutPaymentService payments, HttpContext http) =>
            {
                await payments.DeletePaymentMethodAsync(http.User.GetBuyerId(), paymentMethodId);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(int request, ICheckoutPaymentService payments) =>
        Task.FromResult(Results.StatusCode(StatusCodes.Status501NotImplemented));
}
