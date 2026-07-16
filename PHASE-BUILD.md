You are implementing Maxio inside the eShopOnWeb repository through the usecases described in plan.md
- the plugin      â€” the maxio-sdk plugin from the apimatic marketplace.
                      Go through this plugin for ALL Maxio interactions. This is
                      your sole reference for how to talk to Maxio. Consult it whenever you need any
                      Maxio-side detail.

STRUCTURAL & TOOLS MANDATE (NON-DEBATABLE)
1. You must use the maxio-sdk plugin (apimatic marketplace) SOLELY for every
   Maxio capability.
2. Do not web search or use external general-knowledge tools for Maxio API
   details. All Maxio API/structural knowledge must come from the plugin. This
   is non-debatable.
3. If the plugin does not expose a capability that integration requires STOP and report the gap rather than
   working around it with invented behavior.

CODE QUALITY & STABILITY GUARANTEE
  - Write production-grade, highly secure, cleanly typed C#.
  - Ensure absolutely nothing breaks in the existing eShopOnWeb codebase.
    Maxio failures must never roll back or block eShopOnWeb's order lifecycle beyond
    the existing paths plan.md describes.
  - No placeholders, no "TODO"s, no incomplete implementations.

QUALITY GATES â€” THIS DEFINES "DONE":
  Make sure all the code you write conforms to the existing standards, conventions, and patterns of the eShopOnWeb repo.

HARD RULES
  - Implement plan.md thoroughly
  - Use the maxio-sdk (apimatic marketplace) plugin only.
  - Do not hardcode Maxio credentials (client id/secret) or the environment.
    Use .NET user-secrets, and target the Maxio SANDBOX environment for all
    development and testing.
  - Sandbox credentials are provided to you in the environment variables
    MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN, MAXIO_ENVIRONMENT, and
    MAXIO_DEFAULT_PRODUCT_FAMILY. Load them into .NET user-secrets yourself.
    Never write their values into any file inside the repository.
  - When you run services yourself (dotnet run, hosting a built DLL), bind them
    only to ports in your assigned block: APP_PORT_BLOCK_BASE through
    APP_PORT_BLOCK_BASE+APP_PORT_BLOCK_SIZE-1 (environment variables; the repo's
    launchSettings already point into this block). Stop your previous instance of
    a service before starting another â€” never leave stray processes serving stale
    builds.
  - Do not implement/build anything additional other implementation of the plan.
  - Work until the integration is fully complete. Never hand back, never end
    with a question, and never defer remaining work to the user â€” decide and
    proceed.
  - After implementation is complete, verify and test yourself thoroughly to see if it's done completely and thoroughly and nothing breaks and everything works.
  - And after self-testing, give me step by step plan to verify it by myself AT THE END OF WHOLE INTEGRATION, meaning COMPLETION and all WORKING FLOWS.

