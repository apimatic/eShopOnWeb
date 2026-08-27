using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated
/// by the messaging provider and stored in the provider's canonical form.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest>
{
    private readonly IContactNumberService _contactNumberService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateContactNumberEndpoint(IContactNumberService contactNumberService, IHttpContextAccessor httpContextAccessor)
    {
        _contactNumberService = contactNumberService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request) =>
            {
                return await HandleAsync(request);
            })
            .Produces<CreateContactNumberResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new { message = "PhoneNumber is required." });
        }

        try
        {
            var contactNumber = await _contactNumberService.RegisterAsync(buyerId, request.PhoneNumber, request.CountryCode);
            var response = new CreateContactNumberResponse(request.CorrelationId())
            {
                ContactNumberId = contactNumber.Id,
                PhoneNumber = contactNumber.PhoneNumber,
                NationalFormat = contactNumber.NationalFormat
            };
            return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
        }
        catch (InvalidPhoneNumberException ex)
        {
            return Results.BadRequest(new { message = ex.Message, validationErrors = ex.ValidationErrors });
        }
    }
}
