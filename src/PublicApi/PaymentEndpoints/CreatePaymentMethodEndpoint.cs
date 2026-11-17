using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodEndpoint.CreatePaymentMethodRequest>
{
    private readonly IOrderPaymentService _paymentService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreatePaymentMethodEndpoint(IOrderPaymentService paymentService, IHttpContextAccessor httpContextAccessor)
    {
        _paymentService = paymentService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (CreatePaymentMethodRequest request) =>
                await HandleAsync(request))
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User?.Identity?.Name
            ?? throw new PaymentValidationException("A signed-in shopper is required.");

        var card = request.Card;
        if (card is null && !string.IsNullOrWhiteSpace(request.Number))
        {
            card = new CardRequest
            {
                Number = request.Number,
                ExpirationMonth = request.ExpirationMonth ?? 0,
                ExpirationYear = request.ExpirationYear ?? 0,
                SecurityCode = request.SecurityCode,
                Name = request.Name,
                Address = request.Address
            };
        }

        if (card is null)
        {
            throw new PaymentValidationException("Card details are required.");
        }

        var finixCard = new FinixCardDetails(
            card.Number ?? string.Empty,
            card.ExpirationMonth,
            card.ExpirationYear,
            card.SecurityCode,
            card.Name,
            card.Address is null
                ? null
                : new FinixAddress(
                    card.Address.Line1,
                    card.Address.Line2,
                    card.Address.City,
                    card.Address.Region,
                    card.Address.PostalCode,
                    card.Address.Country));

        var saved = await _paymentService.SavePaymentMethodAsync(
            buyerId,
            finixCard,
            _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);

        return Results.Created($"api/payment-methods/{saved.PaymentMethodId}", new CreatePaymentMethodResponse
        {
            PaymentMethodId = saved.PaymentMethodId,
            PaymentMethod = saved
        });
    }

    public class CreatePaymentMethodRequest
    {
        public CardRequest? Card { get; set; }

        // Flat card fields are accepted as a convenience for callers that do not nest them.
        public string? Number { get; set; }
        public int? ExpirationMonth { get; set; }
        public int? ExpirationYear { get; set; }
        public string? SecurityCode { get; set; }
        public string? Name { get; set; }
        public CardAddressRequest? Address { get; set; }
    }

    public class CreatePaymentMethodResponse
    {
        public int PaymentMethodId { get; set; }
        public SavedCardView PaymentMethod { get; set; } = null!;
    }
}

