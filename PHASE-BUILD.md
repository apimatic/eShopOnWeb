You are implementing a Maxio integration inside this eShopOnWeb repository.

Your task is defined by plan.md at the repository root. Read it end-to-end before writing any code. It defines the use cases, the eShop integration conventions you must follow, the architecture, the configuration (incl. the Maxio credentials and the seeded handles/IDs), the testing strategy (Â§8), and the implementation order.

Use the Maxio OpenAPI specification as your sole source of truth for all information regarding the Maxio integration. You must consult the specification whenever you need any Maxio-side detail. The specification is provided in this repository at `maxio-spec/openapi.yaml`, with all referenced components under `maxio-spec/components/`.

Implementation approach: talk to Maxio over HTTP only. Do not use any external SDK, client library, plugin, or package. How to do the HTTP is your decision.

Execute the plan. For every step:

Re-read the relevant section of plan.md.
Resolve the "how" against the Maxio OpenAPI specification.
Implement against the existing eShop patterns (mirror Catalog.API and other existing services â€” do not invent new conventions).

After all the use cases are done: tell me how to test it, with proper step-by-step testing instructions. Test each use case yourself as well.

Quality gates â€” this defines "done":
Make sure all the code you write conforms to the existing standards, conventions, and patterns of the eShopOnWeb repo


Hard rules:
Talk to Maxio API over HTTP only. Do not use any external SDK, client library, plugin, or package.
Consult the Maxio OpenAPI specification for every Maxio-side detail.
Do not hardcode the Maxio API key. Use .NET user-secrets, as plan.md specifies.
Sandbox credentials are provided to you in the environment variables MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN, MAXIO_ENVIRONMENT, and MAXIO_DEFAULT_PRODUCT_FAMILY. Load them into .NET user-secrets yourself. Never write their values into any file inside the repository.
Do not deviate from plan.md's use cases or eShopOnWeb integration conventions.
Work until the integration is fully complete. Never hand back, never end with a question, and never defer remaining work to the user â€” decide and proceed.
If the Maxio OpenAPI specification does not document a capability listed in plan.md, stop and report the gap rather than guessing or inventing one.

