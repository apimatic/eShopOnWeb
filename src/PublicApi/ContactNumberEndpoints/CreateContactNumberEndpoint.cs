using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, IContactNumberService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateContactNumberEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateContactNumberRequest request, IContactNumberService contactNumbers) =>
            {
                return await HandleAsync(request, contactNumbers);
            })
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, IContactNumberService contactNumbers)
    {
        var buyerId = _httpContextAccessor.HttpContext!.User.GetBuyerId();
        var created = await contactNumbers.RegisterAsync(buyerId, request.PhoneNumber, request.CountryCode);
        var response = new CreateContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = created.Id,
            PhoneNumber = created.PhoneNumber,
            NationalFormat = created.NationalFormat,
            CountryCode = created.CountryCode
        };

        return Results.Created($"api/contact-numbers/{created.Id}", response);
    }
}
