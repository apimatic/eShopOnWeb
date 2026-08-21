using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, IRepository<BuyerContactNumber>>
{
    private readonly ITwilioMessagingClient _twilio;

    public CreateContactNumberEndpoint(ITwilioMessagingClient twilio)
    {
        _twilio = twilio;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, IRepository<BuyerContactNumber> repository, HttpContext httpContext) =>
            {
                return await HandleAsync(request with { BuyerId = httpContext.User.GetBuyerId() }, repository);
            })
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, IRepository<BuyerContactNumber> repository)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new { message = "PhoneNumber is required." });
        }

        var lookup = await _twilio.LookupAsync(request.PhoneNumber, request.CountryCode);
        if (!lookup.Valid || string.IsNullOrWhiteSpace(lookup.PhoneNumber))
        {
            throw new InvalidContactNumberException(lookup.ValidationErrors);
        }

        var duplicate = await repository.FirstOrDefaultAsync(
            new ContactNumberByBuyerAndPhoneSpecification(request.BuyerId, lookup.PhoneNumber));
        if (duplicate != null)
        {
            throw new DuplicateException("This mobile number is already registered.");
        }

        var entity = new BuyerContactNumber(request.BuyerId, lookup.PhoneNumber, lookup.NationalFormat);
        entity = await repository.AddAsync(entity);

        var response = new CreateContactNumberResponse
        {
            ContactNumberId = entity.Id,
            PhoneNumber = entity.PhoneNumber,
            NationalFormat = entity.NationalFormat
        };

        return Results.Created($"api/contact-numbers/{entity.Id}", response);
    }
}
