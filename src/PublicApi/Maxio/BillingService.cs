using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductPricePoints;
using MaxioAdvancedBilling.Requests.Products;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.SubscriptionComponents;
using MaxioAdvancedBilling.Requests.SubscriptionProducts;
using MaxioAdvancedBilling.Requests.SubscriptionStatus;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MaxioAdvancedBilling.Models.AnyOf;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The billing use cases. Maxio is the system of record: nothing about plans or subscriptions is kept locally.
/// A shopper is represented in Maxio by a customer whose reference is the shopper's user name.
/// </summary>
public class BillingService
{
    private const string BRAND_PREFIX = "Brand: ";
    private const string PLAN_FAMILY_HANDLE = "eshop-subscribe";
    private const string PLAN_FAMILY_NAME = "eShopSubscribe";
    private const int PAGE_SIZE = 200;

    private readonly MaxioClientProvider _maxio;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IReadRepository<CatalogBrand> _brands;

    public BillingService(MaxioClientProvider maxio, UserManager<ApplicationUser> userManager,
        IReadRepository<CatalogBrand> brands)
    {
        _maxio = maxio;
        _userManager = userManager;
        _brands = brands;
    }

    // ---------------------------------------------------------------- plans

    public Task<List<PlanDto>> ListPlansAsync(CancellationToken ct) => MaxioErrors.Run(async () =>
    {
        var plans = new List<PlanDto>();
        for (var page = 1; ; page++)
        {
            var products = await _maxio.Client.Products.ListProducts(
                new ListProductsRequest { Page = page, PerPage = PAGE_SIZE, IncludeArchived = false }, null, ct);
            plans.AddRange(products.Select(p => p.Product).Where(p => p.ArchivedAt == null).Select(ToPlan));
            if (products.Count < PAGE_SIZE)
            {
                break;
            }
        }
        return plans;
    });

    public Task<PlanDto> CreatePlanAsync(string? name, decimal? monthlyPrice, string? brand, CancellationToken ct)
    {
        name = name?.Trim();
        brand = brand?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            throw BillingException.BadRequest("A plan needs a name.");
        }
        if (name.Length > 255)
        {
            throw BillingException.BadRequest("The plan name is too long.");
        }
        if (monthlyPrice == null || monthlyPrice < 0 || decimal.Round(monthlyPrice.Value, 2) != monthlyPrice)
        {
            throw BillingException.BadRequest("The monthly price must be a non-negative amount with at most two decimals.");
        }
        if (string.IsNullOrEmpty(brand))
        {
            throw BillingException.BadRequest("A plan needs a brand.");
        }

        return MaxioErrors.Run(async () =>
        {
            var catalogBrand = (await _brands.ListAsync(ct))
                .FirstOrDefault(b => string.Equals(b.Brand, brand, StringComparison.OrdinalIgnoreCase));
            if (catalogBrand == null)
            {
                throw BillingException.Unprocessable($"'{brand}' is not a brand of the shop's catalog.");
            }

            var familyId = await FindOrCreatePlanFamilyAsync(ct);
            var handle = await UniqueHandleAsync(Slug(name), ct);
            var created = await _maxio.Client.Products.CreateProduct(new CreateProductRequest
            {
                ProductFamilyId = familyId.ToString(CultureInfo.InvariantCulture),
                Body = new CreateOrUpdateProductRequest
                {
                    Product = new CreateOrUpdateProduct
                    {
                        Name = name,
                        Handle = handle,
                        Description = BRAND_PREFIX + catalogBrand.Brand,
                        PriceInCents = (long)(monthlyPrice.Value * 100m),
                        Interval = 1,
                        IntervalUnit = IntervalUnit.Month,
                        RequireCreditCard = false,
                        AutoCreateSignupPage = false,
                    },
                },
            }, null, ct);
            return ToPlan(created.Product);
        });
    }

    public async Task<CurrencyPriceDto> AddCurrencyPriceAsync(int planId, string? currency, decimal? price, CancellationToken ct)
    {
        currency = currency?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(currency) || currency.Length != 3 || !currency.All(c => c is >= 'A' and <= 'Z'))
        {
            throw BillingException.BadRequest("The currency must be a three-letter ISO code.");
        }
        if (price == null || price <= 0)
        {
            throw BillingException.BadRequest("The price must be greater than zero.");
        }
        if (price != decimal.Truncate(price.Value) || price > int.MaxValue)
        {
            throw BillingException.BadRequest("The price must be a whole number of currency units.");
        }

        return await MaxioErrors.Run(async () =>
        {
            var plan = await ReadPlanAsync(planId, ct);
            var site = (await _maxio.Client.Sites.ReadSite(null, ct)).Site;
            if (string.Equals(site.Currency, currency, StringComparison.OrdinalIgnoreCase))
            {
                throw BillingException.Unprocessable($"{currency} is the plan's primary currency.");
            }
            if (site.NonPrimaryCurrencies == null ||
                !site.NonPrimaryCurrencies.Contains(currency, StringComparer.OrdinalIgnoreCase))
            {
                throw BillingException.Unprocessable($"{currency} is not a currency enabled on the billing site.");
            }
            if (plan.DefaultProductPricePointId == null)
            {
                throw BillingException.Unprocessable("The plan has no price point to price in another currency.");
            }

            await _maxio.Client.ProductPricePoints.CreateProductCurrencyPrices(new CreateProductCurrencyPricesOperationRequest
            {
                ProductPricePointId = plan.DefaultProductPricePointId.Value,
                Body = new CreateProductCurrencyPricesRequest
                {
                    CurrencyPrices = new[]
                    {
                        new CreateProductCurrencyPrice
                        {
                            Currency = currency,
                            Price = (int)price.Value,
                            Role = CurrencyPriceRole.Baseline,
                        },
                    },
                },
            }, null, ct);
            return new CurrencyPriceDto { PlanId = planId, Currency = currency, Price = price.Value };
        });
    }

    // ---------------------------------------------------------- subscriptions

    public Task<SubscriptionDto> SubscribeAsync(string userName, string? planHandle, CancellationToken ct)
    {
        planHandle = planHandle?.Trim();
        if (string.IsNullOrEmpty(planHandle))
        {
            throw BillingException.BadRequest("A plan handle is required.");
        }

        return MaxioErrors.Run(async () =>
        {
            var user = await ResolveUserAsync(userName);
            var plan = await ReadPlanByHandleAsync(planHandle, ct);

            var subscription = new CreateSubscription
            {
                ProductId = plan.Id,
                // No card details: the shopper is invoiced and pays by remittance.
                PaymentCollectionMethod = CollectionMethod.Remittance,
            };

            var existing = await FindCustomerAsync(user.UserName!, ct);
            subscription = existing != null
                ? subscription with { CustomerId = existing.Id }
                : subscription with
                {
                    CustomerAttributes = new CustomerAttributes
                    {
                        FirstName = LocalPart(user.UserName!),
                        LastName = "Shopper",
                        Email = user.Email ?? user.UserName,
                        Reference = user.UserName,
                    },
                };

            var response = await _maxio.Client.Subscriptions.CreateSubscription(
                new CreateSubscriptionOperationRequest { Body = new CreateSubscriptionRequest { Subscription = subscription } }, null, ct);
            return ToSubscription(response.Subscription!);
        });
    }

    public Task<SubscriptionDto> GetAsync(string userName, int subscriptionId, CancellationToken ct) =>
        MaxioErrors.Run(async () => ToSubscription(await LoadOwnedAsync(userName, subscriptionId, ct)));

    public Task<SubscriptionDto> PauseAsync(string userName, int subscriptionId, CancellationToken ct) =>
        MaxioErrors.Run(async () =>
        {
            var sub = await LoadOwnedAsync(userName, subscriptionId, ct);
            if (sub.State == SubscriptionState.OnHold || sub.State == SubscriptionState.Paused)
            {
                throw BillingException.Conflict("The subscription is already paused.");
            }
            if (sub.State == SubscriptionState.Canceled)
            {
                throw BillingException.Conflict("A cancelled subscription cannot be paused; restart it first.");
            }
            var response = await _maxio.Client.SubscriptionStatus.PauseSubscription(
                new PauseSubscriptionRequest { SubscriptionId = subscriptionId, Body = new PauseRequest() }, null, ct);
            return ToSubscription(response.Subscription!);
        });

    public Task<SubscriptionDto> ResumeAsync(string userName, int subscriptionId, CancellationToken ct) =>
        MaxioErrors.Run(async () =>
        {
            var sub = await LoadOwnedAsync(userName, subscriptionId, ct);
            if (sub.State != SubscriptionState.OnHold && sub.State != SubscriptionState.Paused)
            {
                throw BillingException.Conflict("Only a paused subscription can be resumed.");
            }
            var response = await _maxio.Client.SubscriptionStatus.ResumeSubscription(
                new ResumeSubscriptionRequest { SubscriptionId = subscriptionId }, null, ct);
            return ToSubscription(response.Subscription!);
        });

    public Task<SubscriptionDto> CancelAsync(string userName, int subscriptionId, CancellationToken ct) =>
        MaxioErrors.Run(async () =>
        {
            var sub = await LoadOwnedAsync(userName, subscriptionId, ct);
            if (sub.State == SubscriptionState.Canceled)
            {
                throw BillingException.Conflict("The subscription is already cancelled.");
            }
            var response = await _maxio.Client.SubscriptionStatus.CancelSubscription(new CancelSubscriptionRequest
            {
                SubscriptionId = subscriptionId,
                Body = new CancellationRequest
                {
                    Subscription = new CancellationOptions { CancellationMessage = "Cancelled by the shopper." },
                },
            }, null, ct);
            return ToSubscription(response.Subscription!);
        });

    public Task<SubscriptionDto> RestartAsync(string userName, int subscriptionId, CancellationToken ct) =>
        MaxioErrors.Run(async () =>
        {
            var sub = await LoadOwnedAsync(userName, subscriptionId, ct);
            if (sub.State != SubscriptionState.Canceled)
            {
                throw BillingException.Conflict("Only a cancelled subscription can be restarted.");
            }
            var response = await _maxio.Client.SubscriptionStatus.ReactivateSubscription(
                new ReactivateSubscriptionOperationRequest
                {
                    SubscriptionId = subscriptionId,
                    Body = new ReactivateSubscriptionRequest(),
                }, null, ct);
            return ToSubscription(response.Subscription!);
        });

    public Task<SubscriptionDto> SwitchAsync(string userName, int subscriptionId, string? planHandle, CancellationToken ct)
    {
        planHandle = planHandle?.Trim();
        if (string.IsNullOrEmpty(planHandle))
        {
            throw BillingException.BadRequest("A plan handle is required.");
        }

        return MaxioErrors.Run(async () =>
        {
            var sub = await LoadOwnedAsync(userName, subscriptionId, ct);
            var plan = await ReadPlanByHandleAsync(planHandle, ct);
            if (sub.Product?.Id == plan.Id)
            {
                throw BillingException.Conflict("The subscription is already on that plan.");
            }

            SubscriptionResponse response;
            if (sub.State == SubscriptionState.Active || sub.State == SubscriptionState.Trialing)
            {
                response = await _maxio.Client.SubscriptionProducts.MigrateSubscriptionProduct(
                    new MigrateSubscriptionProductRequest
                    {
                        SubscriptionId = subscriptionId,
                        Body = new SubscriptionProductMigrationRequest
                        {
                            Migration = new SubscriptionProductMigration { ProductId = plan.Id },
                        },
                    }, null, ct);
            }
            else
            {
                // Maxio only migrates active or trialing subscriptions. Any other one (a cancelled one, for
                // example) is moved by changing its product, which leaves its status untouched.
                response = await _maxio.Client.Subscriptions.UpdateSubscription(new UpdateSubscriptionOperationRequest
                {
                    SubscriptionId = subscriptionId,
                    Body = new UpdateSubscriptionRequest
                    {
                        Subscription = new UpdateSubscription { ProductId = plan.Id, DeferSignup = null },
                    },
                }, null, ct);
            }
            return ToSubscription(response.Subscription!);
        });
    }

    public Task<SubscriptionUsageDto> UsageAsync(string userName, int subscriptionId, DateOnly date, CancellationToken ct) =>
        MaxioErrors.Run(async () =>
        {
            await LoadOwnedAsync(userName, subscriptionId, ct);

            // The request has no paging parameter: Maxio returns the subscription's components in one call.
            var components = (await _maxio.Client.SubscriptionComponents.ListSubscriptionComponents(
                    new ListSubscriptionComponentsRequest { SubscriptionId = subscriptionId }, null, ct))
                .Select(c => c.Component!).Where(c => c != null).ToList();

            var result = new SubscriptionUsageDto
            {
                SubscriptionId = subscriptionId,
                Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            };

            // Maxio's date filter is a half-open window in the site's time zone, so ask for a wider window
            // and keep exactly the usages recorded on the requested (site-local) day.
            var from = date.AddDays(-1).ToDateTime(TimeOnly.MinValue);
            var until = date.AddDays(2).ToDateTime(TimeOnly.MinValue);

            foreach (var component in components.Where(c => c.Kind == ComponentKind.MeteredComponent && c.ComponentId != null))
            {
                var entries = new List<UsageEntryDto>();
                for (var page = 1; ; page++)
                {
                    var usages = await _maxio.Client.SubscriptionComponents.ListUsages(new ListUsagesRequest
                    {
                        SubscriptionIdOrReference = subscriptionId,
                        ComponentId = ComponentIdModel.Int(component.ComponentId!.Value),
                        SinceDate = new DateTimeOffset(from, TimeSpan.Zero),
                        UntilDate = new DateTimeOffset(until, TimeSpan.Zero),
                        Page = page,
                        PerPage = PAGE_SIZE,
                    }, null, ct);
                    foreach (var u in usages.Select(x => x.Usage))
                    {
                        if (u.CreatedAt != null && DateOnly.FromDateTime(u.CreatedAt.Value.DateTime) == date)
                        {
                            entries.Add(new UsageEntryDto
                            {
                                Id = u.Id,
                                Quantity = ReadQuantity(u.Quantity),
                                Memo = u.Memo,
                                RecordedAt = u.CreatedAt,
                            });
                        }
                    }
                    if (usages.Count < PAGE_SIZE)
                    {
                        break;
                    }
                }

                result.Components.Add(new ComponentUsageDto
                {
                    ComponentId = component.ComponentId!.Value,
                    ComponentName = component.Name,
                    UnitName = component.UnitName,
                    Quantity = entries.Sum(e => e.Quantity),
                    Entries = entries.OrderBy(e => e.RecordedAt).ThenBy(e => e.Id).ToList(),
                });
            }

            result.TotalQuantity = result.Components.Sum(c => c.Quantity);
            return result;
        });

    public Task<SubscriptionDto> SetNextBillingDateAsync(int subscriptionId, DateOnly? date, CancellationToken ct)
    {
        if (date == null)
        {
            throw BillingException.BadRequest("A date is required (yyyy-MM-dd).");
        }

        return MaxioErrors.Run(async () =>
        {
            var sub = (await _maxio.Client.Subscriptions.ReadSubscription(
                new ReadSubscriptionRequest { SubscriptionId = subscriptionId }, null, ct)).Subscription!;

            // Express the date in the site's own offset (the one Maxio reports for the subscription), at midday,
            // so that the subscriber is billed on exactly that calendar day.
            var offset = (sub.NextAssessmentAt ?? sub.CurrentPeriodEndsAt ?? sub.CreatedAt)?.Offset ?? TimeSpan.Zero;
            var at = new DateTimeOffset(date.Value.ToDateTime(new TimeOnly(12, 0)), offset);
            if (at <= DateTimeOffset.UtcNow)
            {
                throw BillingException.Unprocessable("The next billing date must be in the future.");
            }

            var response = await _maxio.Client.Subscriptions.UpdateSubscription(new UpdateSubscriptionOperationRequest
            {
                SubscriptionId = subscriptionId,
                Body = new UpdateSubscriptionRequest
                {
                    Subscription = new UpdateSubscription { NextBillingAt = at, DeferSignup = null },
                },
            }, null, ct);
            return ToSubscription(response.Subscription!);
        });
    }

    // ---------------------------------------------------------------- helpers

    private async Task<ApplicationUser> ResolveUserAsync(string userName)
    {
        var user = await _userManager.FindByNameAsync(userName);
        if (user == null)
        {
            throw new BillingException(401, "The signed-in user is unknown.");
        }
        return user;
    }

    /// <summary>Reads a subscription and makes sure it belongs to the signed-in shopper.</summary>
    private async Task<Subscription> LoadOwnedAsync(string userName, int subscriptionId, CancellationToken ct)
    {
        var user = await ResolveUserAsync(userName);
        Subscription? sub;
        try
        {
            sub = (await _maxio.Client.Subscriptions.ReadSubscription(
                new ReadSubscriptionRequest { SubscriptionId = subscriptionId }, null, ct)).Subscription;
        }
        catch (ApiException ex) when ((int)ex.StatusCode == 404)
        {
            throw BillingException.NotFound("Subscription not found.");
        }

        // A subscription of someone else is reported as missing, so that ids cannot be probed.
        if (sub == null || !string.Equals(sub.Customer?.Reference, user.UserName, StringComparison.OrdinalIgnoreCase))
        {
            throw BillingException.NotFound("Subscription not found.");
        }
        return sub;
    }

    private async Task<Customer?> FindCustomerAsync(string reference, CancellationToken ct)
    {
        try
        {
            return (await _maxio.Client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference }, null, ct)).Customer;
        }
        catch (ApiException ex) when ((int)ex.StatusCode == 404)
        {
            return null;
        }
    }

    private async Task<Product> ReadPlanAsync(int planId, CancellationToken ct)
    {
        try
        {
            var product = (await _maxio.Client.Products.ReadProduct(
                new ReadProductRequest { ProductId = planId }, null, ct)).Product;
            if (product.ArchivedAt != null)
            {
                throw BillingException.NotFound("Plan not found.");
            }
            return product;
        }
        catch (ApiException ex) when ((int)ex.StatusCode == 404)
        {
            throw BillingException.NotFound("Plan not found.");
        }
    }

    private async Task<Product> ReadPlanByHandleAsync(string handle, CancellationToken ct)
    {
        try
        {
            var product = (await _maxio.Client.Products.ReadProductByHandle(
                new ReadProductByHandleRequest { ApiHandle = handle }, null, ct)).Product;
            if (product.ArchivedAt != null)
            {
                throw BillingException.NotFound($"Plan '{handle}' not found.");
            }
            return product;
        }
        catch (ApiException ex) when ((int)ex.StatusCode == 404)
        {
            throw BillingException.NotFound($"Plan '{handle}' not found.");
        }
    }

    private async Task<int> FindOrCreatePlanFamilyAsync(CancellationToken ct)
    {
        var families = await _maxio.Client.ProductFamilies.ListProductFamilies(new ListProductFamiliesRequest(), null, ct);
        var family = families.Select(f => f.ProductFamily).FirstOrDefault(f =>
            f != null && f.ArchivedAt == null && string.Equals(f.Handle, PLAN_FAMILY_HANDLE, StringComparison.OrdinalIgnoreCase));
        if (family?.Id != null)
        {
            return family.Id.Value;
        }
        var created = await _maxio.Client.ProductFamilies.CreateProductFamily(new CreateProductFamilyOperationRequest
        {
            Body = new CreateProductFamilyRequest
            {
                ProductFamily = new CreateProductFamily { Name = PLAN_FAMILY_NAME, Handle = PLAN_FAMILY_HANDLE },
            },
        }, null, ct);
        return created.ProductFamily!.Id!.Value;
    }

    private async Task<string> UniqueHandleAsync(string slug, CancellationToken ct)
    {
        var handle = slug;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                await _maxio.Client.Products.ReadProductByHandle(new ReadProductByHandleRequest { ApiHandle = handle }, null, ct);
            }
            catch (ApiException ex) when ((int)ex.StatusCode == 404)
            {
                return handle;
            }
            handle = $"{slug}-{Guid.NewGuid().ToString("N")[..6]}";
        }
        return handle;
    }

    private static string Slug(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name.ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                sb.Append(c);
            }
            else if (sb.Length > 0 && sb[^1] != '-')
            {
                sb.Append('-');
            }
        }
        var slug = sb.ToString().Trim('-');
        if (slug.Length > 60)
        {
            slug = slug[..60].Trim('-');
        }
        return slug.Length == 0 ? "plan" : slug;
    }

    private static string LocalPart(string userName)
    {
        var at = userName.IndexOf('@');
        return at > 0 ? userName[..at] : userName;
    }

    private static decimal ReadQuantity(Quantity1? quantity)
    {
        if (quantity == null)
        {
            return 0;
        }
        if (quantity.TryGetInt(out var i))
        {
            return i;
        }
        return quantity.TryGetString(out var s) && decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    private static PlanDto ToPlan(Product p)
    {
        var description = p.Description;
        var brand = description != null && description.StartsWith(BRAND_PREFIX, StringComparison.Ordinal)
            ? description[BRAND_PREFIX.Length..].Trim()
            : null;
        return new PlanDto
        {
            Id = p.Id ?? 0,
            Handle = p.Handle,
            Name = p.Name,
            Brand = brand,
            MonthlyPrice = (p.PriceInCents ?? 0) / 100m,
            Currency = "USD",
            PricePointId = p.DefaultProductPricePointId,
        };
    }

    private static SubscriptionDto ToSubscription(Subscription s)
    {
        var balance = s.BalanceInCents ?? 0;
        return new SubscriptionDto
        {
            Id = s.Id ?? 0,
            State = s.State?.Value,
            Paid = balance <= 0,
            BalanceDue = Math.Max(balance, 0) / 100m,
            Currency = s.Currency,
            PlanId = s.Product?.Id,
            PlanHandle = s.Product?.Handle,
            PlanName = s.Product?.Name,
            PlanPrice = s.ProductPriceInCents == null ? null : s.ProductPriceInCents / 100m,
            NextBillingAt = s.NextAssessmentAt,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
            CanceledAt = s.CanceledAt,
            CreatedAt = s.CreatedAt,
        };
    }
}
