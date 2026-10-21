// ListAll.cs
//
// Reads a Maxio list to its end. It asks for 200 rows a page, or fewer if you say so, starts at
// page 1 and stops only when a page comes back empty. It never reads more pages than the ceiling you pass, and reaching the
// ceiling is an exception, never a shorter list. With a key function it drops a row it has already
// returned, so a list that shifts while you walk it yields no row twice.
//
// Implements: knowledge/rules.md R8.
//
// Which operations it is for:
// - A list operation whose request has Page and PerPage and that returns an array, such as
//   ListSubscriptions and ListCustomers.
// - A list operation that returns a wrapper object. Unwrap it in the function you pass:
//     async (page, perPage) =>
//         (await client.Invoices.ListInvoices(request with { Page = page, PerPage = perPage })).Invoices
// - Not a list operation that takes no page. ListProductFamilies and ListSubscriptionComponents are
//   two of them: in the SDK map, `page` is not among their query params. Call such an operation
//   once. Walked with this file it returns the same rows for every page, and the walk ends in
//   PageCeilingException.
// - An operation with `page` and no `per_page` (ListAllocations) works too: pass the page number
//   and ignore the page size.
//
// What it does not do:
// - It does not make a shifting list complete. A row added or removed while you walk moves the
//   rows after it by one place. With a key no row comes back twice, but a row can still be
//   missed. Where the operation has a sort, sort ascending by a value that never changes, so
//   that a new row lands on the last page. ListAll.Subscriptions asks for ascending order unless
//   your request names a direction.
// - It does not pick the ceiling. You pass it. Hitting it throws PageCeilingException.
// - It does not check your filters. Some fail open: an unrecognized date_field is ignored and
//   every row comes back (knowledge/rules.md R9).
// - It does not limit requests in flight. Wrap the walk, or each page, in RequestLimit.Run.
// - It is not the tool for a bulk extract. Use an export job (knowledge/decisions.md D19).
//
// Copy into your project and adapt. Not part of the SDK's API.
//
// Adapted for eShopOnWeb: PagesOrPartial returns the rows read together with IsComplete = false when
// the ceiling is reached, so an API response can say it was cut short instead of failing the request.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Recipes;

/// <summary>Reads a paged Maxio list to its end (knowledge/rules.md R8).</summary>
public static class ListAll
{
    /// <summary>Maxio's cap on per_page. A larger value is cut to 200 with no error.</summary>
    public const int MaxPerPage = 200;

    /// <summary>
    /// The pattern on one real operation: every subscription <paramref name="request"/> matches,
    /// each one once. Page and PerPage of the request are replaced. When the request sets no
    /// Direction this asks for ascending order, so that under the default sort, the signup date, a
    /// new signup lands on the last page. Throws <see cref="PageCeilingException"/> when
    /// <paramref name="maxPages"/> pages were read and none was empty.
    /// </summary>
    public static Task<IReadOnlyList<SubscriptionResponse>> Subscriptions(
        MaxioAdvancedBillingClient client,
        ListSubscriptionsRequest request,
        int maxPages,
        CancellationToken cancellationToken = default)
    {
        _ = client ?? throw new ArgumentNullException(nameof(client));
        _ = request ?? throw new ArgumentNullException(nameof(request));

        var ordered = request with { Direction = request.Direction ?? SortingDirection.Asc };
        return Pages(
            (page, perPage) => client.Subscriptions.ListSubscriptions(
                ordered with { Page = page, PerPage = perPage }, cancellationToken: cancellationToken),
            maxPages,
            key: row => row.Subscription?.Id,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Walks any paged list. <paramref name="fetchPage"/> receives the page number, from 1, and
    /// the page size, and must pass both on to the operation. Returns every row of every page up
    /// to the first empty page. Throws <see cref="PageCeilingException"/> when
    /// <paramref name="maxPages"/> pages were read and none was empty.
    /// </summary>
    /// <param name="fetchPage">Calls the list operation for one page and returns its rows.</param>
    /// <param name="maxPages">The most pages to read. The walk needs one more page than the list has.</param>
    /// <param name="perPage">Rows to ask for per page: 200, or less for an operation whose own maximum is lower.</param>
    /// <param name="key">
    /// What identifies a row, such as its id. When given, a row whose key was already returned is
    /// dropped. A row whose key is null is always kept.
    /// </param>
    /// <param name="cancellationToken">Checked before each page. Pass the same token to the operation.</param>
    public static async Task<IReadOnlyList<T>> Pages<T>(
        Func<int, int, Task<IReadOnlyList<T>>> fetchPage,
        int maxPages,
        int perPage = MaxPerPage,
        Func<T, object?>? key = null,
        CancellationToken cancellationToken = default)
    {
        var result = await PagesOrPartial(fetchPage, maxPages, perPage, key, cancellationToken).ConfigureAwait(false);
        if (!result.IsComplete)
            throw new PageCeilingException(maxPages, result.Items.Count);
        return result.Items;
    }

    /// <summary>
    /// The same walk as <see cref="Pages{T}"/>, but reaching <paramref name="maxPages"/> without an
    /// empty page returns the rows read with <see cref="PagedRows{T}.IsComplete"/> false instead of
    /// throwing. The caller must pass that flag on: a partial list must never look complete.
    /// </summary>
    public static async Task<PagedRows<T>> PagesOrPartial<T>(
        Func<int, int, Task<IReadOnlyList<T>>> fetchPage,
        int maxPages,
        int perPage = MaxPerPage,
        Func<T, object?>? key = null,
        CancellationToken cancellationToken = default)
    {
        _ = fetchPage ?? throw new ArgumentNullException(nameof(fetchPage));
        if (maxPages < 1)
            throw new ArgumentOutOfRangeException(nameof(maxPages), maxPages, "Read at least one page.");
        if (perPage < 1 || perPage > MaxPerPage)
            throw new ArgumentOutOfRangeException(nameof(perPage), perPage, "Maxio cuts per_page to 200 with no error. Pass 1 to 200.");

        var all = new List<T>();
        var returned = key is null ? null : new HashSet<object>();

        for (var page = 1; page <= maxPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rows = await fetchPage(page, perPage).ConfigureAwait(false);
            if (rows is null)
                throw new InvalidOperationException("fetchPage returned null. Return an empty list for an empty page.");

            // An empty page is the only end signal. A short page is not one: "fewer rows than I
            // asked for" is also what every full page looks like once Maxio has cut per_page,
            // and a loop that stops there returns page one as if it were the whole list.
            if (rows.Count == 0)
                return new PagedRows<T>(all, IsComplete: true);

            foreach (var row in rows)
            {
                if (key != null && returned != null)
                {
                    var id = key(row);
                    if (id != null && !returned.Add(id))
                        continue;
                }

                all.Add(row);
            }
        }

        // The ceiling ended the walk, not Maxio. A partial list that looks complete is a wrong
        // answer the caller cannot detect, so it is returned flagged as incomplete.
        return new PagedRows<T>(all, IsComplete: false);
    }
}

/// <summary>The rows a walk read, and whether it reached the end of the list (an empty page).</summary>
public sealed record PagedRows<T>(IReadOnlyList<T> Items, bool IsComplete);

/// <summary>
/// <see cref="ListAll"/> read as many pages as its ceiling allows and Maxio had not returned an
/// empty page. Either the list is longer than the ceiling, or the operation ignores <c>page</c>
/// and returned the same rows every time.
/// </summary>
public sealed class PageCeilingException : Exception
{
    /// <summary>Creates the exception for a walk that stopped at <paramref name="maxPages"/> pages.</summary>
    public PageCeilingException(int maxPages, int rowsRead)
        : base("Read " + maxPages + " pages (" + rowsRead + " rows kept) and no page was empty. " +
               "The list is longer than the ceiling, or the operation ignores `page` and returned the same rows each time.")
    {
        MaxPages = maxPages;
        RowsRead = rowsRead;
    }

    /// <summary>The ceiling that was reached.</summary>
    public int MaxPages { get; }

    /// <summary>How many rows the walk had kept when it stopped. They are not the whole list.</summary>
    public int RowsRead { get; }
}
