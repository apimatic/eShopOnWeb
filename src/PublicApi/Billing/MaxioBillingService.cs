using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.ProductPricePoints;
using MaxioAdvancedBilling.Requests.Products;
using MaxioAdvancedBilling.Requests.SubscriptionComponents;
using MaxioAdvancedBilling.Requests.SubscriptionProducts;
using MaxioAdvancedBilling.Requests.SubscriptionStatus;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// All billing operations. Maxio is the system of record: nothing is stored locally, a shopper is
/// linked to their Maxio customer through the customer "reference" (the user name from the JWT).
/// </summary>
public class MaxioBillingService
{
    private const int MaxPages = 50;

    private readonly Lazy<MaxioAdvancedBillingClient> _client;

    public MaxioBillingService(IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _client = new Lazy<MaxioAdvancedBillingClient>(
            () => MaxioClientFactory.Create(configuration, httpClientFactory.CreateClient()),
            true);
    }

    private MaxioAdvancedBillingClient Client => _client.Value;

    // ---------------------------------------------------------------- plans

    public Task<List<PlanDto>> ListPlansAsync(IReadOnlyCollection<string> catalogBrands) =>
        Run("list plans", async () =>
        {
            var currency = (await Client.Sites.ReadSite()).Site.Currency;
            var plans = new List<PlanDto>();
            for (var page = 1; page <= MaxPages; page++)
            {
                var batch = await Client.Products.ListProducts(new ListProductsRequest { Page = page, PerPage = 200 });
                foreach (var p in batch.Select(b => b.Product))
                {
                    if (p.ArchivedAt != null || string.IsNullOrWhiteSpace(p.Handle) || p.Id == null) continue;
                    plans.Add(ToPlan(p, catalogBrands, currency));
                }
                if (batch.Count < 200) break;
            }
            return plans;
        });

    public Task<PlanDto> CreatePlanAsync(string? name, decimal? monthlyPrice, string brand)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw BillingException.BadRequest("A plan needs a name.");
        if (monthlyPrice is null || monthlyPrice < 0)
            throw BillingException.BadRequest("A plan needs a monthly price of zero or more.");
        var cents = ToCents(monthlyPrice.Value, "monthly price");
        var handle = Slug(name);
        if (handle.Length == 0)
            throw BillingException.BadRequest("The plan name must contain letters or digits.");

        return Run("create the plan", async () =>
        {
            var existing = await FindProductByHandle(handle);
            if (existing != null)
                throw BillingException.Conflict($"A plan with the handle '{handle}' already exists.");

            var familyId = await EnsureBrandFamilyAsync(brand);
            var created = await Client.Products.CreateProduct(new CreateProductRequest
            {
                ProductFamilyId = familyId.ToString(CultureInfo.InvariantCulture),
                Body = new CreateOrUpdateProductRequest
                {
                    Product = new CreateOrUpdateProduct
                    {
                        Name = name.Trim(),
                        Handle = handle,
                        Description = $"{name.Trim()} ({brand})",
                        PriceInCents = cents,
                        Interval = 1,
                        IntervalUnit = IntervalUnit.Month,
                        // shoppers subscribe without card details
                        RequireCreditCard = false,
                    },
                },
            });
            var currency = (await Client.Sites.ReadSite()).Site.Currency;
            return ToPlan(created.Product, new[] { brand }, currency);
        });
    }

    public Task<CurrencyPriceDto> AddCurrencyPriceAsync(int planId, string? currency, decimal? price) =>
        Run("price the plan in another currency", async () =>
        {
            if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3 || !currency.Trim().All(char.IsLetter))
                throw BillingException.BadRequest("currency must be a three letter ISO 4217 code such as EUR.");
            if (price is null || price <= 0)
                throw BillingException.BadRequest("price must be greater than zero.");
            ToCents(price.Value, "price"); // at most two decimals
            if (price > int.MaxValue) throw BillingException.BadRequest("price is too large.");
            var code = currency.Trim().ToUpperInvariant();

            var product = await ReadProductOrNull(planId)
                          ?? throw BillingException.NotFound($"Plan {planId} was not found.");
            if (product.ArchivedAt != null)
                throw BillingException.Conflict($"Plan {planId} is archived.");
            var pricePointId = product.DefaultProductPricePointId ?? product.ProductPricePointId
                               ?? throw BillingException.Conflict($"Plan {planId} has no price point to add a price to.");

            var site = (await Client.Sites.ReadSite()).Site;
            if (string.Equals(site.Currency, code, StringComparison.OrdinalIgnoreCase))
                throw BillingException.Conflict($"{code} is the shop's base currency; the plan is already priced in it.");
            var enabled = site.NonPrimaryCurrencies ?? Array.Empty<string>();
            if (!enabled.Contains(code, StringComparer.OrdinalIgnoreCase))
                throw BillingException.Unprocessable(
                    $"Maxio cannot price in {code}: the currency is not enabled on the billing site " +
                    $"(enabled besides {site.Currency}: {(enabled.Count == 0 ? "none" : string.Join(", ", enabled))}).");

            var result = await Client.ProductPricePoints.CreateProductCurrencyPrices(new CreateProductCurrencyPricesOperationRequest
            {
                ProductPricePointId = pricePointId,
                Body = new CreateProductCurrencyPricesRequest
                {
                    CurrencyPrices = new[]
                    {
                        new CreateProductCurrencyPrice { Currency = code, Price = (int)Math.Round(price.Value), Role = CurrencyPriceRole.Baseline },
                    },
                },
            });
            var created = result.CurrencyPrices.FirstOrDefault(c => string.Equals(c.Currency, code, StringComparison.OrdinalIgnoreCase));

            // the create call only takes whole units; apply the exact amount when there are minor units
            if (price.Value != Math.Round(price.Value) && created?.Id != null)
            {
                result = await Client.ProductPricePoints.UpdateProductCurrencyPrices(new UpdateProductCurrencyPricesRequest
                {
                    ProductPricePointId = pricePointId,
                    Body = new UpdateCurrencyPricesRequest
                    {
                        CurrencyPrices = new[] { new UpdateCurrencyPrice { Id = created.Id.Value, Price = (double)price.Value } },
                    },
                });
                created = result.CurrencyPrices.FirstOrDefault(c => string.Equals(c.Currency, code, StringComparison.OrdinalIgnoreCase)) ?? created;
            }

            return new CurrencyPriceDto
            {
                PlanId = planId,
                Currency = code,
                Price = created?.Price != null ? (decimal)created.Price.Value : price.Value,
            };
        });

    // -------------------------------------------------------- subscriptions

    public Task<SubscriptionDto> SubscribeAsync(string user, string? planHandle) =>
        Run("subscribe", async () =>
        {
            if (string.IsNullOrWhiteSpace(planHandle))
                throw BillingException.BadRequest("A plan handle is required.");
            var plan = await RequireActivePlan(planHandle);
            var customerId = await EnsureCustomerAsync(user);

            var created = await Client.Subscriptions.CreateSubscription(new CreateSubscriptionOperationRequest
            {
                Body = new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = plan.Handle,
                        CustomerId = customerId,
                        // no card details: the shopper is invoiced and pays the invoice
                        PaymentCollectionMethod = CollectionMethod.Remittance,
                    },
                },
            });
            return ToDto(created.Subscription ?? throw BillingException.Unprocessable("Maxio did not create the subscription."));
        });

    public Task<SubscriptionDto> GetAsync(string user, int subscriptionId) =>
        Run("read the subscription", async () => ToDto(await GetOwned(user, subscriptionId)));

    public Task<SubscriptionDto> PauseAsync(string user, int subscriptionId) =>
        Run("pause the subscription", async () =>
        {
            var sub = await GetOwned(user, subscriptionId);
            var state = StateOf(sub);
            if (IsPaused(state)) throw BillingException.Conflict("The subscription is already paused.");
            if (IsEnded(state)) throw BillingException.Conflict($"A {state} subscription cannot be paused.");
            var r = await Client.SubscriptionStatus.PauseSubscription(new PauseSubscriptionRequest { SubscriptionId = subscriptionId });
            return ToDto(r.Subscription ?? sub);
        });

    public Task<SubscriptionDto> ResumeAsync(string user, int subscriptionId) =>
        Run("resume the subscription", async () =>
        {
            var sub = await GetOwned(user, subscriptionId);
            if (!IsPaused(StateOf(sub))) throw BillingException.Conflict($"Only a paused subscription can be resumed; this one is {StateOf(sub)}.");
            var r = await Client.SubscriptionStatus.ResumeSubscription(new ResumeSubscriptionRequest { SubscriptionId = subscriptionId });
            return ToDto(r.Subscription ?? sub);
        });

    public Task<SubscriptionDto> CancelAsync(string user, int subscriptionId) =>
        Run("cancel the subscription", async () =>
        {
            var sub = await GetOwned(user, subscriptionId);
            if (IsEnded(StateOf(sub))) throw BillingException.Conflict($"The subscription is already {StateOf(sub)}.");
            var r = await Client.SubscriptionStatus.CancelSubscription(new CancelSubscriptionRequest
            {
                SubscriptionId = subscriptionId,
                Body = new CancellationRequest { Subscription = new CancellationOptions { CancellationMessage = "Cancelled by the shopper" } },
            });
            return ToDto(r.Subscription ?? sub);
        });

    public Task<SubscriptionDto> RestartAsync(string user, int subscriptionId) =>
        Run("restart the subscription", async () =>
        {
            var sub = await GetOwned(user, subscriptionId);
            if (!IsEnded(StateOf(sub))) throw BillingException.Conflict($"Only a cancelled subscription can be restarted; this one is {StateOf(sub)}.");
            var r = await Client.SubscriptionStatus.ReactivateSubscription(new ReactivateSubscriptionOperationRequest { SubscriptionId = subscriptionId });
            return ToDto(r.Subscription ?? sub);
        });

    public Task<SubscriptionDto> SwitchPlanAsync(string user, int subscriptionId, string? planHandle) =>
        Run("switch the plan", async () =>
        {
            if (string.IsNullOrWhiteSpace(planHandle))
                throw BillingException.BadRequest("A plan handle is required.");
            var sub = await GetOwned(user, subscriptionId);
            var target = await RequireActivePlan(planHandle);
            if (sub.Product?.Id == target.Id)
                throw BillingException.Conflict($"The subscription is already on plan '{target.Handle}'.");

            // Maxio cannot move a cancelled subscription, so it is brought back first; if the move
            // then fails the subscription is put back the way it was.
            var reactivated = false;
            if (IsEnded(StateOf(sub)))
            {
                await Client.SubscriptionStatus.ReactivateSubscription(new ReactivateSubscriptionOperationRequest { SubscriptionId = subscriptionId });
                reactivated = true;
            }

            try
            {
                var migrated = await Client.SubscriptionProducts.MigrateSubscriptionProduct(new MigrateSubscriptionProductRequest
                {
                    SubscriptionId = subscriptionId,
                    Body = new SubscriptionProductMigrationRequest
                    {
                        Migration = new SubscriptionProductMigration { ProductHandle = target.Handle },
                    },
                });
                if (migrated.Subscription?.Product?.Id != target.Id)
                    throw BillingException.Conflict("Maxio accepted the request but the subscription is not on the new plan.");
            }
            catch
            {
                if (reactivated)
                {
                    try
                    {
                        await Client.SubscriptionStatus.CancelSubscription(new CancelSubscriptionRequest
                        {
                            SubscriptionId = subscriptionId,
                            Body = new CancellationRequest { Subscription = new CancellationOptions { CancellationMessage = "Plan switch failed; restoring cancelled state" } },
                        });
                    }
                    catch
                    {
                        // nothing more can be done here
                    }
                }
                throw;
            }

            return ToDto(await ReadSubscription(subscriptionId));
        });

    public Task<UsageDto> GetUsageAsync(string user, int subscriptionId, DateOnly day) =>
        Run("read the usage", async () =>
        {
            await GetOwned(user, subscriptionId);
            var components = (await Client.SubscriptionComponents.ListSubscriptionComponents(new ListSubscriptionComponentsRequest { SubscriptionId = subscriptionId }))
                .Select(c => c.Component)
                .Where(c => c?.ComponentId != null && c.Kind == ComponentKind.MeteredComponent)
                .ToList();

            // Maxio's until_date is midnight at the *start* of that date, and timestamps are in the
            // site's time zone, so ask for a window around the day and keep exactly that day.
            var since = new DateTimeOffset(day.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var until = new DateTimeOffset(day.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

            var result = new UsageDto { SubscriptionId = subscriptionId, Date = day };
            foreach (var component in components)
            {
                var item = new ComponentUsageDto
                {
                    ComponentId = component!.ComponentId!.Value,
                    ComponentName = component.Name,
                    UnitName = component.UnitName,
                };
                for (var page = 1; page <= MaxPages; page++)
                {
                    var batch = await Client.SubscriptionComponents.ListUsages(new ListUsagesRequest
                    {
                        SubscriptionIdOrReference = subscriptionId,
                        ComponentId = component.ComponentId.Value,
                        SinceDate = since,
                        UntilDate = until,
                        Page = page,
                        PerPage = 200,
                    });
                    foreach (var u in batch.Select(b => b.Usage))
                    {
                        if (u?.CreatedAt == null || DateOnly.FromDateTime(u.CreatedAt.Value.DateTime) != day) continue;
                        item.Usages.Add(new UsageEntryDto
                        {
                            UsageId = u.Id,
                            Quantity = QuantityOf(u.Quantity),
                            Memo = u.Memo,
                            RecordedAt = u.CreatedAt,
                        });
                    }
                    if (batch.Count < 200) break;
                }
                item.Quantity = item.Usages.Sum(x => x.Quantity);
                result.Components.Add(item);
            }
            result.TotalQuantity = result.Components.Sum(c => c.Quantity);
            return result;
        });

    public Task<SubscriptionDto> SetNextBillingDateAsync(int subscriptionId, DateOnly date) =>
        Run("set the next billing date", async () =>
        {
            var sub = await ReadSubscription(subscriptionId);
            if (IsEnded(StateOf(sub)))
                throw BillingException.Conflict($"A {StateOf(sub)} subscription is not billed, so it has no next billing date.");

            // Maxio works in the site's time zone; its offset is visible on the timestamps it returns.
            var offset = sub.NextAssessmentAt?.Offset ?? sub.CreatedAt?.Offset ?? TimeSpan.Zero;
            var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(offset).DateTime);
            if (date <= today)
                throw BillingException.Unprocessable($"The next billing date must be after today ({today:yyyy-MM-dd}).");

            // noon keeps the date stable even if the site's UTC offset changes (daylight saving) before then
            var at = new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), offset);
            var updated = await Client.Subscriptions.UpdateSubscription(new UpdateSubscriptionOperationRequest
            {
                SubscriptionId = subscriptionId,
                Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { NextBillingAt = at } },
            });
            var result = updated.Subscription ?? await ReadSubscription(subscriptionId);
            if (result.NextAssessmentAt == null || DateOnly.FromDateTime(result.NextAssessmentAt.Value.DateTime) != date)
                throw BillingException.Conflict("Maxio did not apply the requested billing date.");
            return ToDto(result);
        });

    // -------------------------------------------------------------- helpers

    private async Task<T> Run<T>(string action, Func<Task<T>> work)
    {
        try
        {
            return await work();
        }
        catch (Exception ex) when (ex is not BillingException)
        {
            throw MaxioErrors.Translate(ex, action);
        }
    }

    private async Task<Subscription> ReadSubscription(int id)
    {
        try
        {
            return (await Client.Subscriptions.ReadSubscription(new ReadSubscriptionRequest { SubscriptionId = id })).Subscription
                   ?? throw BillingException.NotFound($"Subscription {id} was not found.");
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw BillingException.NotFound($"Subscription {id} was not found.");
        }
    }

    /// <summary>Reads the subscription and makes sure it belongs to the caller (others look like 404).</summary>
    private async Task<Subscription> GetOwned(string user, int id)
    {
        var sub = await ReadSubscription(id);
        if (!string.Equals(sub.Customer?.Reference, user, StringComparison.OrdinalIgnoreCase))
            throw BillingException.NotFound($"Subscription {id} was not found.");
        return sub;
    }

    private async Task<int> EnsureCustomerAsync(string user)
    {
        try
        {
            return (await Client.Customers.ReadCustomerByReference(new ReadCustomerByReferenceRequest { Reference = user })).Customer.Id
                   ?? throw BillingException.BadRequest("Maxio returned a customer without an id.");
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
        }

        var local = user.Contains('@') ? user[..user.IndexOf('@')] : user;
        var parts = local.Split(new[] { '.', '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var first = parts.Length > 0 ? Capitalise(parts[0]) : "Shopper";
        var last = parts.Length > 1 ? Capitalise(parts[^1]) : "Shopper";
        try
        {
            var created = await Client.Customers.CreateCustomer(new CreateCustomerOperationRequest
            {
                Body = new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = first,
                        LastName = last,
                        Email = user.Contains('@') ? user : $"{user}@example.invalid",
                        Reference = user,
                    },
                },
            });
            return created.Customer.Id ?? throw BillingException.BadRequest("Maxio returned a customer without an id.");
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // someone else may have created the customer a moment ago
            return (await Client.Customers.ReadCustomerByReference(new ReadCustomerByReferenceRequest { Reference = user })).Customer.Id
                   ?? throw MaxioErrors.Translate(ex, "create the customer");
        }
    }

    private async Task<Product> RequireActivePlan(string handle)
    {
        var product = await FindProductByHandle(handle.Trim());
        if (product == null || product.ArchivedAt != null)
            throw BillingException.NotFound($"Plan '{handle}' was not found.");
        return product;
    }

    private async Task<Product?> FindProductByHandle(string handle)
    {
        try
        {
            return (await Client.Products.ReadProductByHandle(new ReadProductByHandleRequest { ApiHandle = handle })).Product;
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<Product?> ReadProductOrNull(int id)
    {
        try
        {
            return (await Client.Products.ReadProduct(new ReadProductRequest { ProductId = id })).Product;
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>Plans are grouped in one Maxio product family per catalog brand, named "{brand} plans".</summary>
    private async Task<int> EnsureBrandFamilyAsync(string brand)
    {
        var familyName = FamilyName(brand);
        var families = await Client.ProductFamilies.ListProductFamilies(new ListProductFamiliesRequest());
        var existing = families.Select(f => f.ProductFamily)
            .FirstOrDefault(f => f?.Id != null && f.ArchivedAt == null && string.Equals(f.Name, familyName, StringComparison.OrdinalIgnoreCase));
        if (existing?.Id != null) return existing.Id.Value;

        var created = await Client.ProductFamilies.CreateProductFamily(new CreateProductFamilyOperationRequest
        {
            Body = new CreateProductFamilyRequest
            {
                ProductFamily = new CreateProductFamily { Name = familyName, Handle = Slug(familyName), Description = $"Subscription plans for the {brand} brand" },
            },
        });
        return created.ProductFamily?.Id ?? throw BillingException.Unprocessable("Maxio did not create the product family.");
    }

    private static string FamilyName(string brand) => $"{brand} plans";

    private static PlanDto ToPlan(Product p, IReadOnlyCollection<string> brands, string? currency)
    {
        var family = p.ProductFamily?.Name;
        var brand = brands.FirstOrDefault(b => string.Equals(family, FamilyName(b), StringComparison.OrdinalIgnoreCase)
                                               || string.Equals(family, b, StringComparison.OrdinalIgnoreCase))
                    ?? family;
        return new PlanDto
        {
            PlanId = p.Id ?? 0,
            Handle = p.Handle,
            Name = p.Name,
            Description = p.Description,
            Brand = brand,
            MonthlyPrice = (p.PriceInCents ?? 0) / 100m,
            Currency = currency,
            Interval = p.Interval ?? 1,
            IntervalUnit = p.IntervalUnit?.Value,
        };
    }

    private static SubscriptionDto ToDto(Subscription s)
    {
        var balance = s.BalanceInCents ?? 0;
        return new SubscriptionDto
        {
            SubscriptionId = s.Id ?? 0,
            State = s.State?.Value,
            // a negative balance is a credit: nothing is owed
            Paid = balance <= 0,
            BalanceDue = Math.Max(balance, 0) / 100m,
            Currency = s.Currency,
            PlanId = s.Product?.Id,
            PlanHandle = s.Product?.Handle,
            PlanName = s.Product?.Name,
            PlanPrice = s.ProductPriceInCents / 100m,
            NextBillingAt = s.NextAssessmentAt,
            NextBillingDate = s.NextAssessmentAt == null ? null : DateOnly.FromDateTime(s.NextAssessmentAt.Value.DateTime),
            CreatedAt = s.CreatedAt,
            CanceledAt = s.CanceledAt,
        };
    }

    private static string StateOf(Subscription s) => s.State?.Value ?? "unknown";
    private static bool IsPaused(string state) => state is "on_hold" or "paused";
    private static bool IsEnded(string state) => state is "canceled" or "expired";

    private static decimal QuantityOf(MaxioAdvancedBilling.Models.AnyOf.Quantity1? q)
    {
        if (q == null) return 0;
        if (q.TryGetInt(out var i)) return i;
        return q.TryGetString(out var s) && decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    private static long ToCents(decimal amount, string what)
    {
        var cents = amount * 100m;
        if (cents != decimal.Truncate(cents))
            throw BillingException.BadRequest($"The {what} can have at most two decimal places.");
        if (cents > 100_000_000_00L)
            throw BillingException.BadRequest($"The {what} is too large.");
        return (long)cents;
    }

    private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    public static string Slug(string value)
    {
        var sb = new StringBuilder();
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }
}
