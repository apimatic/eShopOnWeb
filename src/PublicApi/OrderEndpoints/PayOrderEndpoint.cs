using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderApiRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderApiRequest request, IOrderPaymentService service, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, service, user);
            })
            .Produces<OrderDto>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderApiRequest request, IOrderPaymentService service) =>
        HandleAsync(request, service, new ClaimsPrincipal());

    private async Task<IResult> HandleAsync(PayOrderApiRequest request, IOrderPaymentService service, ClaimsPrincipal user)
    {
        var buyerId = EndpointUser.RequireBuyerId(user);
        var order = await service.PayAsync(buyerId, request.OrderId, new PayOrderRequest
        {
            PaymentMethodId = request.PaymentMethodId,
            Card = request.Card is null ? null : new CardPaymentRequest
            {
                Name = request.Card.Name,
                Number = request.Card.Number,
                Expiry = request.Card.Expiry,
                SecurityCode = request.Card.SecurityCode,
                BillingAddress = new BillingAddressRequest
                {
                    AddressLine1 = request.Card.BillingAddress.AddressLine1,
                    AddressLine2 = request.Card.BillingAddress.AddressLine2,
                    AdminArea2 = request.Card.BillingAddress.AdminArea2,
                    AdminArea1 = request.Card.BillingAddress.AdminArea1,
                    PostalCode = request.Card.BillingAddress.PostalCode,
                    CountryCode = request.Card.BillingAddress.CountryCode
                }
            }
        });

        return Results.Ok(OrderDto.From(order));
    }
}

public class PayOrderApiRequest : BaseRequest
{
    public int OrderId { get; set; }
    public int? PaymentMethodId { get; set; }
    public CardDetailsRequest? Card { get; set; }
}

public class CardDetailsRequest
{
    public string Name { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public BillingAddressApiRequest BillingAddress { get; set; } = new();
}

public class BillingAddressApiRequest
{
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string AdminArea2 { get; set; } = string.Empty;
    public string AdminArea1 { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
}
