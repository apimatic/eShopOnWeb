---
name: feedback-eshoponweb-razor-pages-tempdata
description: In this repo's Razor Pages, [TempData] properties are read-once — a multi-step POST/redirect/GET/POST flow needs explicit TempData.Keep() or it silently loses state on the second hop
metadata:
  type: feedback
---

ASP.NET Core's default `CookieTempDataProvider` (used by eShopOnWeb's Web host) deletes a `[TempData]`-attributed property's value **after it's read once**, even just by rendering it in a Razor view — it is not auto-renewed just because the same PageModel type keeps re-touching it.

**Why this matters:** a 3-step flow (POST sets TempData → redirect → GET renders it → user's next POST needs to still read it) breaks silently on step 3 unless the GET handler calls `TempData.Keep(nameof(Prop))` for each key it wants to survive past that render. Discovered while building the UC3 plan-change preview/confirm flow in `src/Web/Pages/Subscriptions/Mine.cshtml.cs`: without `Keep()`, the "Confirm" POST always reported "no pending preview" even though the preview banner had just rendered correctly on the page before it.

**How to apply:** Whenever a Razor Pages flow in this repo needs a value to survive more than one request hop via `[TempData]`, call `TempData.Keep(nameof(Property))` in whichever `OnGet`/handler reads-and-displays it, for every key that must still be readable in the *next* request. Confirmed by testing the real cookie roundtrip with curl (extracting `Set-Cookie: .AspNetCore.Mvc.CookieTempDataProvider=...; expires=...` — an expiring value means it was cleared, not renewed).
