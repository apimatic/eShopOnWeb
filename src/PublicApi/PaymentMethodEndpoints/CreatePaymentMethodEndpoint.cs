using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Finix;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest
{
    public string? Number { get; set; }
    public string? CardNumber { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public int? ExpiryMonth { get; set; }
    public int? ExpiryYear { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public string? Cvv { get; set; }
    public string? Name { get; set; }
    public string? CardHolderName { get; set; }
    public CardAddressRequest? Address { get; set; }
    public CardAddressRequest? BillingAddress { get; set; }
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) { }

    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string Last4 { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? Name { get; set; }
}

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest>
{
    private readonly CatalogContext _db;
    private readonly FinixClient _finix;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreatePaymentMethodEndpoint(CatalogContext db, FinixClient finix, IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _finix = finix;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request) => await HandleAsync(request))
            .Produces<CreatePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request)
    {
        var ownerId = PaymentHelpers.OwnerId(_httpContextAccessor.HttpContext?.User);
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            return Results.Unauthorized();
        }

        var number = request.Number ?? request.CardNumber;
        var month = request.ExpirationMonth ?? request.ExpiryMonth;
        var year = request.ExpirationYear ?? request.ExpiryYear;
        var expiry = request.Expiry;

        if ((month is null || year is null) && !string.IsNullOrWhiteSpace(expiry))
        {
            var parts = expiry.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], out var parsedMonth) &&
                int.TryParse(parts[1], out var parsedYear))
            {
                month ??= parsedMonth;
                year ??= parsedYear;
            }
        }

        if (year is < 100)
        {
            year += 2000;
        }

        if (string.IsNullOrWhiteSpace(number) || month is null || year is null)
        {
            return Results.BadRequest(new
            {
                message = "Card number, expirationMonth and expirationYear are required."
            });
        }

        var cancellationToken = CancellationToken.None;

        FinixPaymentInstrument paymentInstrument;
        try
        {
            var identityId = await PaymentHelpers.GetOrCreateIdentityAsync(_db, _finix, ownerId, cancellationToken);
            paymentInstrument = await _finix.CreatePaymentInstrumentAsync(
                identityId,
                string.IsNullOrWhiteSpace(request.Name ?? request.CardHolderName) ? ownerId : (request.Name ?? request.CardHolderName)!,
                number!,
                month.Value,
                year.Value,
                request.SecurityCode ?? request.Cvv,
                ToFinixAddress(request.Address ?? request.BillingAddress),
                cancellationToken);
        }
        catch (FinixApiException exception)
        {
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "The card could not be saved with Finix.",
                detail: exception.Message);
        }

        var savedCard = new SavedCard(ownerId, paymentInstrument.Id, paymentInstrument.Brand,
            paymentInstrument.LastFour ?? string.Empty,
            paymentInstrument.ExpirationMonth ?? month.Value,
            paymentInstrument.ExpirationYear ?? year.Value,
            request.Name ?? request.CardHolderName);

        _db.SavedCards.Add(savedCard);
        await _db.SaveChangesAsync(cancellationToken);

        var response = new CreatePaymentMethodResponse(Guid.NewGuid())
        {
            PaymentMethodId = savedCard.Id,
            Brand = savedCard.Brand,
            Last4 = savedCard.Last4,
            ExpirationMonth = savedCard.ExpirationMonth,
            ExpirationYear = savedCard.ExpirationYear,
            Name = savedCard.Name
        };

        return Results.Created($"api/payment-methods/{savedCard.Id}", response);
    }

    private static FinixAddressRequest? ToFinixAddress(CardAddressRequest? address)
    {
        if (address is null)
        {
            return null;
        }

        var line1 = address.Line1;
        var city = address.City;
        var region = address.Region ?? address.State;
        var postalCode = address.PostalCode ?? address.ZipCode;
        var country = address.Country;

        if (string.IsNullOrWhiteSpace(line1) || string.IsNullOrWhiteSpace(city) ||
            string.IsNullOrWhiteSpace(region) || string.IsNullOrWhiteSpace(postalCode) ||
            string.IsNullOrWhiteSpace(country))
        {
            return null;
        }

        return new FinixAddressRequest
        {
            Line1 = line1,
            Line2 = address.Line2,
            City = city,
            Region = region,
            PostalCode = postalCode,
            Country = country
        };
    }
}
