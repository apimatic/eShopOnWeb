using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

using Microsoft.eShopWeb.PublicApi;
using Microsoft.eShopWeb.PublicApi.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, PaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (HttpContext http, CreatePaymentMethodRequest request, PaymentService payments) =>
                {
                    request.BuyerId = http.User.Identity?.Name ?? string.Empty;
                    request.RequestAborted = http.RequestAborted;
                    return await HandleAsync(request, payments);
                })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, PaymentService payments)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var card = new CardInput
        {
            Number = request.Number,
            ExpirationMonth = request.ExpirationMonth,
            ExpirationYear = request.ExpirationYear,
            SecurityCode = request.SecurityCode,
            Name = request.Name
        };

        var saved = await payments.CreateSavedCardAsync(request.BuyerId, card, request.RequestAborted);

        return Results.Created($"api/payment-methods/{saved.PaymentMethodId}", new CreatePaymentMethodResponse
        {
            PaymentMethodId = saved.PaymentMethodId,
            PaymentMethod = saved
        });
    }
}

public class CreatePaymentMethodRequest : PaymentRequestBase
{
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public string PaymentMethodId { get; set; } = string.Empty;
    public SavedCardView PaymentMethod { get; set; } = null!;
}
