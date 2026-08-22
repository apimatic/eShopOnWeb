using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreatePaymentMethodRequest request, IPaymentService payments, HttpContext httpContext) =>
            {
                request.BuyerId = httpContext.User.GetBuyerId();
                return await HandleAsync(request, payments);
            })
            .Produces<PaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IPaymentService payments)
    {
        if (request.Card is null)
        {
            return Results.BadRequest(new { message = "Card details are required." });
        }

        var saved = await payments.SaveCardAsync(request.BuyerId!, request.Card.ToCardPaymentRequest());
        var response = PaymentMethodResponse.From(saved);
        return Results.Created($"api/payment-methods/{response.PaymentMethodId}", response);
    }
}

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IPaymentService payments, HttpContext httpContext) =>
            {
                var cards = await payments.ListSavedCardsAsync(httpContext.User.GetBuyerId());
                return Results.Ok(new PaymentMethodListResponse
                {
                    PaymentMethods = cards.Select(PaymentMethodResponse.From).ToList()
                });
            })
            .Produces<PaymentMethodListResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(IPaymentService payments) => Task.FromResult(Results.Ok());
}

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId, IPaymentService payments, HttpContext httpContext) =>
            {
                return await HandleAsync(new DeletePaymentMethodRequest
                {
                    PaymentMethodId = paymentMethodId,
                    BuyerId = httpContext.User.GetBuyerId()
                }, payments);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IPaymentService payments)
    {
        await payments.DeleteSavedCardAsync(request.BuyerId, request.PaymentMethodId);
        return Results.NoContent();
    }
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public string? BuyerId { get; set; }
    public OrderEndpoints.CardDetailsRequest? Card { get; set; }
}

public class DeletePaymentMethodRequest : BaseRequest
{
    public int PaymentMethodId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
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

public class PaymentMethodListResponse
{
    public System.Collections.Generic.List<PaymentMethodResponse> PaymentMethods { get; set; } = new();
}
