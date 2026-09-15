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
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, IContactNumberService contactNumbers, HttpContext httpContext) =>
            {
                return await HandleAsync(request, contactNumbers, httpContext);
            })
            .Produces<CreateContactNumberResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(CreateContactNumberRequest request, IContactNumberService contactNumbers)
        => HandleAsync(request, contactNumbers, null!);

    private async Task<IResult> HandleAsync(
        CreateContactNumberRequest request,
        IContactNumberService contactNumbers,
        HttpContext httpContext)
    {
        var buyerId = httpContext.User.GetBuyerId();
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new { message = "phoneNumber is required." });
        }

        var created = await contactNumbers.RegisterAsync(buyerId, request.PhoneNumber, httpContext.RequestAborted);
        var response = new CreateContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = created.Id,
            CanonicalNumber = created.CanonicalNumber
        };

        return Results.Created($"api/contact-numbers/{created.Id}", response);
    }
}
