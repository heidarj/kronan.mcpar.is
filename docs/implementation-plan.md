# Krónan Workspace Plugin, Visual UI, and Alexa Implementation Plan

Extend the existing .NET MCP server into a privately published ChatGPT Business workspace plugin. Both household members should use the same Krónan shopping note from ChatGPT on their phones, and an Alexa custom skill should call the same application services. Host the service in Azure, keep the Krónan access token on the server, authenticate callers, and enforce a shared upstream request budget.

The first implementation adds the hosted OAuth boundary, shopping tools, shared budgets/replay controls, visual cards, plugin packaging, and Azure templates. Automated synthetic tests cover local behavior. Authenticated upstream behavior, a real Entra/ChatGPT connection, Azure deployment, workspace/mobile acceptance, and the Alexa adapter remain outstanding. See [acceptance checks](acceptance.md).

Updated constraint: no Azure Table Storage and no application database for the household deployment. Retain the upstream credential as a managed Azure secret so it survives restarts. Keep temporary control state in memory, with the restart and deployment limitations described below. Public Directory distribution is a separate, conditional phase.

## Reviewed baseline

Reviewed on October 1, 2026, against repository main commit [46f93b6](https://github.com/heidarj/kronan.mcpar.is/commit/46f93b6660c81e9902c730a2bfaa49db73573bf3) and the public OpenAPI schema, version 1.0.1, linked by [Krónan ReDoc](https://api.kronan.is/api/v1/schema/redoc/).

| Area | Current implementation | Required change |
| --- | --- | --- |
| Runtime | ASP.NET Core targeting .NET 9 | Move to .NET 10 LTS and update Docker images |
| MCP transport | Streamable HTTP at /mcp | Retain it and validate protocol compatibility |
| Inbound authentication | Shared X-Api-Key middleware | Use OAuth bearer authentication for the ChatGPT connection |
| Upstream authentication | Authorization: AccessToken with a configured Krónan token | Retain the upstream scheme and move production secrets to Key Vault |
| Tools | SearchProducts, GetProduct, ListCategories | Add focused shopping-note tools |
| Rate limiting | No limiter or upstream budget in source | Add transport, tool, and shared in-process upstream controls |
| Product search model | Discounts modeled directly on search hits | Map discounts from the documented nested detail object |
| Error handling | EnsureSuccessStatusCode and exceptions | Translate upstream failures into useful, sanitized tool results |
| Packaging and delivery | Dockerfile and README | Add workspace plugin package, Azure configuration, tests, and release instructions |

Source files: [Program.cs](https://github.com/heidarj/kronan.mcpar.is/blob/46f93b6660c81e9902c730a2bfaa49db73573bf3/src/Kronan.mcpar.is/Program.cs), [KronanClient.cs](https://github.com/heidarj/kronan.mcpar.is/blob/46f93b6660c81e9902c730a2bfaa49db73573bf3/src/Kronan.mcpar.is/KronanApi/KronanClient.cs), [KronanTools.cs](https://github.com/heidarj/kronan.mcpar.is/blob/46f93b6660c81e9902c730a2bfaa49db73573bf3/src/Kronan.mcpar.is/Tools/KronanTools.cs), and [project file](https://github.com/heidarj/kronan.mcpar.is/blob/46f93b6660c81e9902c730a2bfaa49db73573bf3/src/Kronan.mcpar.is/Kronan.mcpar.is.csproj).

.NET 9 support ends November 10, 2026, so the proposed runtime upgrade belongs in the first phase. [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy).

## Proposed architecture

Keep one ASP.NET Core application and the existing Docker deployment model.

| Component | Responsibility |
| --- | --- |
| Workspace plugin | Package the hosted MCP connection and shopping instructions |
| Optional ChatGPT component | Render shopping-note and product cards through MCP Apps |
| /mcp | Authenticate ChatGPT callers and expose tools |
| /alexa | Verify Alexa requests and map intents to application operations |
| ShoppingNoteService | Apply household authorization, validation, and mutation rules |
| KronanClient | Call the documented upstream API through one budget and resilience policy |
| Managed OAuth provider | Handle login, consent, tokens, and refresh |
| In-process controls | Hold bounded rate-limit windows, replay records, locks, and caches |
| Azure Key Vault | Hold the upstream access token and any confidential OAuth client secrets |

Krónan remains the authoritative store for the shopping note. There is no shopping-list copy, connection database, storage account, Redis instance, or mounted data volume in the household deployment.

Use Azure Container Apps for the existing container, with minimum and maximum replicas both set to one. Keep it warm for Alexa. Use one active production revision, and a stop/drain/start deployment procedure that prevents concurrent instances from calling Krónan. Single-revision mode alone does not establish this guarantee; verify actual replica termination during release checks. Accept a short maintenance interruption rather than a rolling deployment. [Azure hosting comparison](https://learn.microsoft.com/en-us/azure/container-apps/mcp-choosing-azure-service).

The API key is separate from temporary memory state. Configure it once in Key Vault and reference it from the container's secret configuration. Every new process receives it again automatically; neither household member needs to resupply it after a reboot. OAuth accounts, signing keys, and refresh credentials are managed by the identity provider. This means no application database, not literally zero persistence anywhere. [Azure secret configuration](https://learn.microsoft.com/en-us/azure/container-apps/manage-secrets).

## Shared household identity

Provision or select a Krónan customer group containing both household members, and use its access token where available. Both OAuth identities map to one server-configured household and credential reference. Confirm that both people see the same note in the native Krónan app before rollout.

The documentation supports user and customer-group tokens and an identity check at GET /api/v1/me/. Group-token throttling attribution still needs confirmation; until then, share one conservative quota bucket for the household. The schema does not supply a stable identity ID in the Me response, so do not derive authorization or quota keys from its display name. [Krónan API documentation](https://api.kronan.is/api/v1/schema/redoc/).

Use an explicit configured quota principal, such as household-main, that groups every credential potentially consuming the same upstream allowance. Rotating a token must not reset this principal's budget.

Resolve household membership from verified OAuth issuer and subject claims. Never accept a household ID, credential, or identity supplied in a tool argument as authorization.

## Authentication design

ChatGPT authentication and Krónan authentication serve different purposes. Replace the current custom header with the documented OAuth 2.1 MCP authentication contract. ChatGPT obtains a token for this service; the service separately uses Krónan's AccessToken credential. Krónan does not need to implement OAuth for this private bridge to work. [OpenAI MCP authentication](https://developers.openai.com/plugins/build/auth).

Current provider decision: try the existing Entra tenant with a manually registered, predefined ChatGPT OAuth client. Entra does not support DCR. Microsoft documents ChatGPT's User-Defined OAuth Client route for its Power BI MCP service, but the custom Krónan flow still needs live discovery, PKCE metadata, resource/audience, scope, callback, and refresh validation. A public Entra discovery check also found no advertised PKCE methods; this is an explicit compatibility gate under OpenAI's documented S256 requirement. Keep the server provider-neutral; Auth0 is an alternative if necessary, not a required new account. See [OAuth setup](oauth.md).

Implement the following contract:

- Authorization-code flow with PKCE and exact registered callback URLs.
- Protected-resource discovery metadata, authorization-server discovery, and a 401 WWW-Authenticate challenge.
- A tested predefined ChatGPT OAuth client for this private deployment; use CIMD registration if it is the supported route in the chosen tenant.
- Short-lived access tokens, proposed lifetime 15 minutes, and refresh handled by the provider.
- Tool declarations for catalog:read, shopping:read, and shopping:write, with server enforcement.
- Public health and discovery endpoints; authenticated MCP operations.
- A separate OAuth client for Alexa account linking when that adapter is introduced.

Validate token signature, issuer, audience, expiry, and required scope. Apply a separate household membership policy after token validation. Return 401 for missing or invalid authentication and 403 for a valid identity without access. [ASP.NET Core JWT validation](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0).

Remove the production X-Api-Key path from /mcp. Keep local development predictable by using explicit development configuration and loopback access; a missing production configuration must fail startup rather than open the endpoint.

Disable membership to revoke application access, and revoke refresh credentials to prevent new tokens. Make the member-disable mechanism visible in the runbook. Workspace publishing alone must never confer server access.

Store the Krónan credential in Key Vault and use the application's managed identity to retrieve it. Never forward it to ChatGPT or Alexa, place it in a plugin package, or include it in log output. The current upstream token is broad; enforce the allowed upstream operations in code. [Azure secret configuration](https://learn.microsoft.com/en-us/azure/container-apps/manage-secrets).

## Shopping tools and API mapping

Use the shopping-notes resource for ordinary shopping-list commands. Keep saved product collections as a later capability.

| Proposed tool | Upstream operation |
| --- | --- |
| GetShoppingNote | GET /api/v1/shopping-notes/ |
| AddShoppingItems | POST /api/v1/shopping-notes/add-lines/ |
| UpdateShoppingItem | PATCH /api/v1/shopping-notes/change-line/ |
| RemoveShoppingItem | DELETE /api/v1/shopping-notes/delete-line/?token=... |

The API supports free-text or SKU entries and batches of up to 30 lines. These operations are documented in [Krónan ReDoc](https://api.kronan.is/api/v1/schema/redoc/).

For “add butter,” send a free-text entry. Search for a particular SKU only when the user asks for a specific product. Use one batch call for “add butter, milk, and eggs.”

Application rules:

- Require exactly one of text or SKU for every added item.
- Default an omitted conversational quantity to one; validate against the schema.
- Validate line identifiers as UUIDs and confirm they belong to the current household note.
- Require at least one actual field change for updates; an empty upstream change request can delete a line.
- Return structured identifiers and quantities alongside a short human-readable outcome.
- Keep readOnlyHint, destructiveHint, idempotentHint, and tool descriptions consistent with actual behavior. Retrieving a shopping note can create it, and adding items is not inherently idempotent.
- Keep completion toggles out of the first release. Add a desired-state completion operation later only after testing concurrency and replay behavior.

Preserve existing product tools. Correct the search response's nested discount detail, validate query/page bounds, and add cancellation propagation, response disposal, bounded results, and correct 404 handling. Use the operation schemas as the contract: the documentation's general naming description and individual property names are not fully consistent.

## Rate limiting and upstream protection

Krónan documents a limit of **200 requests per 200 seconds per user**, with HTTP 429 on excess. [Krónan API documentation](https://api.kronan.is/api/v1/schema/redoc/).

The following values are proposed initial policy, not Krónan limits. Keep them configurable.

| Layer | Proposed initial setting | Partition |
| --- | --- | --- |
| Anonymous HTTP traffic | 60 requests per minute with a small burst | Trusted client IP or a bounded shared anonymous bucket |
| Authenticated MCP HTTP requests | 120 per minute | Verified issuer and subject |
| Tool invocations | 60 per minute, burst 10 | Verified application actor |
| Shopping mutations | 20 per minute, burst 5 | Shared household |
| Actual upstream requests | Maximum 120 in any rolling 200 seconds, plus pacing at one request per two seconds | Configured upstream quota principal |
| Concurrent upstream requests | Maximum two | Upstream quota principal |

Use ASP.NET Core middleware for the HTTP limits, then enforce tool and mutation limits inside a shared application boundary. A transport request may contain multiple operations, so HTTP request counts alone cannot protect the upstream budget. Configure authentication before identity-based limiting. Treat forwarded IP headers as trusted only from the configured Azure proxy. [ASP.NET Core rate limiting](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0).

Maintain one thread-safe, bounded rolling budget in the application process. Every outgoing attempt reserves capacity first, including retries, reconciliation reads, and Alexa calls. All adapters and UI-triggered operations use the same singleton controls. This protects concurrent requests within the process; it is not a distributed limiter.

On every process start, block upstream calls for a full 200 seconds before spending a fresh budget. Planned deployments must stop the previous process before this recovery interval starts, so its unknown reservations expire. Keep liveness healthy while reporting the temporary cooldown through tools. A restart therefore causes a brief unavailable period, not a request to re-enter the API key. If the host cannot guarantee non-overlapping callers, the in-memory design cannot promise the aggregate budget: change the hosting arrangement or explicitly revisit shared control storage before production.

The 120-request policy leaves headroom for direct API usage by other clients. It cannot reserve Krónan capacity against those clients, so 429 remains a normal condition to handle.

Cache product reads for a proposed two minutes and categories for fifteen minutes. Partition every cache by household/credential context because availability can differ. Cache hits consume a tool permit but no upstream permit. Coalesce identical concurrent reads. Avoid caching the live shopping note initially.

Do not queue work indefinitely. Return HTTP 429 with Retry-After when the transport limiter rejects a request. For tool-level rejection, return a valid MCP error result with a stable rate-limit code and retry timing. Alexa should speak a brief retry message.

On an upstream 429, honor Retry-After if present and apply a shared cooldown for the quota principal. If no timing is provided, use conservative backoff. Permit at most two retries for operations established to be safe reads, within the request deadline; do not automatically retry shopping mutations or non-idempotent GETs.

## Mutation replay and concurrency

The fetched schema does not document an upstream idempotency-key contract. Design around that uncertainty.

Add a bounded in-memory operation cache keyed by household, client, and operation ID. Retain the request fingerprint and pending/succeeded/unknown outcome for the operation's validity period. Reject reuse of an ID with different arguments. Return the previous outcome for a successfully completed replay within that period. Reject expired IDs; do not evict an unexpired record and then accept its ID as new.

For ChatGPT, issue mutation IDs through a small preparation tool that makes no upstream request. Bind each ID to the normalized request, a bounded expiry, and the current process generation. Require that ID on the mutation and its retries. Reject IDs from a previous process generation and require checking the current note before preparing another action. Document that requesting a new ID represents a new action; a model can still mistakenly repeat the user's intent, so this is not an exactly-once guarantee. For Alexa, use its verified requestId and request timestamp within the current process lifetime.

Serialize household mutations with one in-process lock shared by ChatGPT, component interactions, and Alexa. The deployment procedure must prevent a second process from creating an independent mutation stream.

If the upstream connection fails after dispatch, record an unknown outcome. Fetch the current note and reconcile only when the result can be established unambiguously. Otherwise report that the addition could not be confirmed and do not submit it again automatically. After a crash the cache is lost; previous-generation IDs are refused, but their outcomes cannot be recovered from this service. Inspect the live Krónan note instead. Do not claim durable duplicate prevention across restarts.

Do not deduplicate solely by item text: two deliberate requests to add butter may be legitimate.

## Visual output inside ChatGPT

Add an optional MCP Apps component served by the existing .NET application. ChatGPT can display HTML/CSS/JavaScript, including a bundled React component, inside a sandboxed iframe alongside the conversation. Start with an inline shopping-note card and product cards with images, prices, quantity controls, and an Add button. Keep completion controls deferred until the desired-state operation is safe. [ChatGPT component UI](https://developers.openai.com/plugins/build/chatgpt-ui).

Keep data and mutation tools independently usable, including through Alexa. Add focused ShowShoppingNote and ShowProducts render tools instead of rendering a new card after every operation. The note card must state that it is separate from checkout and named saved product lists. Return structuredContent with stable line/product identifiers. Register an MCP UI resource using text/html;profile=mcp-app and associate it through _meta.ui.resourceUri. Component interactions call the existing tools through the MCP Apps bridge.

Every button action must pass the same server-side authorization, validation, mutation preparation, and rate limits as a conversational tool call. Treat displayed IDs and quantities as untrusted inputs. Fetch authoritative note state when reopening a card; temporary selection state can remain in the component. Do not use browser storage as the shopping-list database or send API keys to the iframe.

Bundle the component during development/CI and include the built assets in the container. Users install the plugin and connect their account; they do not install Node or npx. Declare narrow CSP asset/connection domains and the component domain required for public submission. Verify responsive layout, keyboard accessibility, and supported behavior on both phones. [Plugin UI reference](https://developers.openai.com/plugins/reference).

## Workspace plugin and phone rollout

Create a plugin package containing the registered remote MCP connection and a short shopping skill. Its instructions should explain free-text additions, SKU selection, batch use, operation IDs, and when clarification is necessary.

Publish it to the existing Business workspace and grant both members access. Each person completes the required OAuth connection unless a separately tested shared workspace credential arrangement is used. Keep the package free of secrets.

Workspace publishing stays inside the organization boundary. OpenAI documents plugin use on web, desktop, and mobile, but actual installation, sign-in, tools, and mutation behavior must be tested on both phones. [Plugin packaging](https://developers.openai.com/plugins/build/plugins), [workspace controls and mobile support](https://learn.chatgpt.com/docs/enterprise/apps-and-connectors).

Acceptance examples:

- Each person adds an item on their phone and sees it in the same Krónan note.
- A three-item request uses one upstream batch call.
- Reopening ChatGPT does not require local setup.
- A valid third-party OAuth identity is refused even if it discovers the server URL.
- Disabling a household member prevents their subsequent operations.
- Shopping-note/product cards render on supported phone surfaces, and button actions observe the same protections as chat commands.

## Public distribution and bring-your-own-key authentication

Publishing the repository openly, sharing a private workspace plugin, and publishing in the public ChatGPT Directory are distinct routes. The current Directory guidelines prohibit collecting authentication secrets and exclude plugins whose primary function is an unofficial connector to another service. This project would need to resolve those requirements before a public launch; do not assume that placing an API-key form on an external website creates an exception. [Public plugin guidelines](https://developers.openai.com/plugins/plugin-guidelines).

Preferred public route: obtain Krónan's support for an official integration and delegated account connection. The reviewed API schema documents AccessToken authentication, user/customer-group tokens, and Auðkenni-based token creation, but does not document an OAuth authorization flow. Ask Krónan whether partner OAuth or another supported delegated mechanism exists; do not invent one or ask users for their Auðkenni credentials. [Krónan API documentation](https://api.kronan.is/api/v1/schema/redoc/).

For a private/custom deployment, or a hosted public route whose credential flow has been explicitly cleared, the technical API-key bridge would work as follows:

1. The user chooses Connect and signs in on the service's HTTPS account page through the managed identity provider.
2. On that external account page, they supply their Krónan token once. Validate it through GET /api/v1/me/ without logging it. Keep credentials out of chat, tool arguments, UI component results, manifests, and model-visible data.
3. Store it in a managed secret vault, linked to a stable verified service identity or connection ID. This is persistent secret custody even if there is no relational database. A managed connection provider can own the link metadata; arbitrary public household membership is not part of the static two-user configuration.
4. The user consents to the service's shopping/catalogue scopes. ChatGPT completes authorization-code plus PKCE and receives this service's short-lived OAuth token. The service resolves the credential and calls Krónan separately.
5. Provide connection removal, key rotation, and upstream-revocation handling. Disconnect must remove access and prevent stale cached credentials from being used. Confirm the actual secret-retention/deletion behavior and disclose it.

Use established OAuth components rather than building a new authorization server around the API-key form. The same identity and connection can later support Alexa through a separate registered client. Upstream OAuth is preferable for public usability, but technically an OAuth facade can front an API-key-only upstream. [OpenAI MCP authentication](https://developers.openai.com/plugins/build/auth).

Public callers must each resolve their own authorized credential. Keep account, household, cache, and operation boundaries separate; never use the operator's personal key as a public shared credential. Budget actual upstream attempts by the effective Krónan quota owner, including both adapters, token rotations, and UI interactions. The Me response exposes no stable ID, so account deduplication and customer-group quota attribution need provider clarification before claiming limits are enforced across multiple credentials belonging to one owner.

A growing multi-instance public service also needs coordinated rate limits and replay controls. The household's in-memory design cannot provide those guarantees across replicas. A public architecture decision must select a managed shared control service or accept a tightly constrained single-process deployment; it must not quietly add Table Storage or another database.

If centrally retaining users' credentials is undesirable, publish the source and a deploy-your-own template. Each operator provisions their own Azure secret and OAuth configuration; their key survives reboots in their infrastructure. This avoids central custody but requires more setup than a hosted Directory plugin. Public submission also requires a usable dedicated review account with sample data and the normal review process. [Plugin submission](https://developers.openai.com/plugins/deploy/submission).

## Alexa adapter

Add /alexa to the same application after the workspace plugin works. Implement AddItems, GetList, Help, and Cancel first. Map intents directly to ShoppingNoteService; these commands do not require an LLM call.

Use account linking to the same OAuth provider and household mapping. On every request, validate the Alexa signature and certificate URL/chain, reject timestamps older than the allowed 150-second tolerance, verify the skill ID, and validate the linked token. A valid Amazon signature alone does not authorize a household operation. Use maintained verification components; do not treat request JSON as proof of origin. [Alexa HTTPS verification](https://developer.amazon.com/en-US/docs/alexa/custom-skills/host-a-custom-skill-as-a-web-service.html), [skill ID verification](https://developer.amazon.com/en-US/docs/alexa/custom-skills/handle-requests-sent-by-alexa.html).

Test the invocation “Alexa, ask Krónan Shopping to add butter” against the device's actual locale and pronunciation. Use a short response such as “Added butter.” Aim to complete within five seconds and fail clearly when the deadline cannot be met.

Use developer testing on the household Echo first. Access from other Amazon accounts requires the appropriate household/testing or publication route. Beta testing is a test distribution mechanism, not an assumed permanent production arrangement. [Alexa testing](https://www.developer.amazon.com/en-US/docs/alexa/test/test-your-skill-overview.html).

Write directly to Krónan. Third-party access to Alexa's built-in shopping lists was retired in July 2024. [Alexa deprecated features](https://developer.amazon.com/en-US/docs/alexa/ask-overviews/deprecated-features.html).

## Implementation sequence

Each phase should be a small reviewable change with the indicated acceptance evidence.

| Phase | Deliverables | Completion check |
| --- | --- | --- |
| 1 Establish contracts | .NET 10 upgrade, corrected product DTOs, relevant schema fixtures, typed failures, cancellation | Existing tools pass contract tests without a real token |
| 2 Establish identity | Managed OAuth setup, discovery, JWT policies, membership mapping, startup validation | ChatGPT completes sign-in; rejected callers cannot invoke tools |
| 3 Protect the upstream | HTTP/tool limits, in-memory budget/cooldown/cache, bounded operation cache, preparation IDs and shared mutation lock | Concurrent requests obey budget; restart cooldown and obsolete-ID rejection pass |
| 4 Implement notes | Shopping models/client methods, ShoppingNoteService, four tools and annotations | Add/update/remove tests pass; ambiguous failures do not repeat writes |
| 5 Deploy and publish privately | Azure IaC, managed identity, Key Vault, CI, plugin package and stop/start runbook | Both phones use the shared note; restart preserves secrets and observes cooldown |
| 6 Add Alexa | Verified HTTPS adapter, account linking, interaction model, requestId replay handling | Household Echo adds butter once and reports errors correctly |
| 7 Add component UI | Shopping-note/product cards, render tools, MCP Apps resources, accessible responsive UI | Supported phone surfaces render cards; buttons enforce auth and limits |
| 8 Evaluate public launch | Krónan partnership/delegation, OpenAI eligibility clarification, credential custody and scaling decisions | Documented approval route and isolation design exist before broad distribution |

Phase 2 should include an early test against ChatGPT before building the rest of the shopping flow. Phase 3 must precede authenticated production use. Keep catalog-only access available during later development through the same protected endpoint.

Suggested source organization:

| Location | Planned responsibility |
| --- | --- |
| Program.cs and Options | Endpoint wiring, validated configuration, auth and HTTP policies |
| Authentication/ | Discovery metadata, claims policies, household resolver |
| KronanApi/ and Models/ | Schema-aligned client and DTOs |
| Services/ShoppingNoteService.cs | Household operations shared by both adapters |
| Infrastructure/ | In-memory budget, bounded operation cache, preparation IDs, mutation lock, cache |
| Tools/ShoppingNoteTools.cs | MCP tool definitions and result/error mapping |
| web/ and Tools/PresentationTools.cs | Bundled component source/assets and UI resource/render tools |
| Alexa/ | Request validation and intent handlers |
| tests/ | Contract, authorization, quota, mutation, and adapter integration tests |
| infra/ | Bicep for Container Apps, identity, registry, and Key Vault; no application data store |
| plugins/kronan-shopping/ | Workspace plugin manifest and skill |
| docs/ | Setup, publishing, rotation, recovery, and manual test instructions |

## Deployment and operations

Use HTTPS at the stable application domain and keep OAuth discovery URLs consistent with the externally visible address. Validate host/origin handling and ensure proxy configuration does not turn authentication failures into HTML sign-in redirects.

Run the container as a non-root user, set Production explicitly, and validate required options on startup. Separate liveness from readiness; readiness checks configuration and critical control dependencies without repeatedly calling Krónan.

Build and test in CI, publish a versioned image, and use GitHub-to-Azure workload identity instead of stored deployment credentials. Deploy and roll back through the verified stop/drain/start procedure. Every replacement observes the 200-second recovery interval. Retain Azure secret configuration and identity-provider configuration across deployments; temporary quota/cache/operation state is recreated.

Log actor/household identifiers, source, tool, operation ID, latency, upstream status, and limiter outcome. Redact tokens, Authorization headers, shopping-note text, and response bodies by default. Replace the existing information-level raw search-query logging. Measure quota rejections, 429 cooldowns, authentication failures, and unknown mutation outcomes.

Estimate regional Azure and identity-provider costs before provisioning. A warm replica improves responsiveness but should not be described as free.

## Verification and release gates

Use stub upstream responses for failure and load tests. Reserve actual authenticated calls for modest manual checks with the owner's supplied credentials.

Required automated checks:

1. Product detail and nested search discounts deserialize correctly.
2. Missing, expired, wrong-audience, and unknown-member tokens never reach a tool handler.
3. Tool batching, cache misses, retries, and both adapters consume the intended quota.
4. Concurrent requests in one process cannot double-spend permits; every fresh process observes the 200-second recovery interval.
5. Previous-generation and expired mutation IDs are refused; cache saturation never admits a forgotten unexpired replay.
6. Invalid item inputs and empty updates cannot delete or mutate data.
7. A repeated successful operation returns its result; a conflicting fingerprint is rejected.
8. An uncertain upstream write is not automatically replayed.
9. Household line ownership and mutations remain isolated under concurrency.
10. Alexa rejects forged signatures, stale requests, wrong skill IDs, and unlinked callers.
11. Component-triggered tool calls obey authorization and budget rules; tampered IDs cannot cross household boundaries.

Manual release checks cover OAuth consent/refresh, both phones and component UI, native Krónan list visibility, rate-limit messages, access removal, upstream-token rotation, and the real Echo command. Restart without re-entering the key. Verify that a rollout never leaves two processes making upstream calls, and document the maintenance/cooldown period and loss of replay outcomes.

Remaining household setup decisions are Krónan token ownership, the OAuth tenant/login method, the Azure region and budget, and the Echo's account/locale. Confirm effective customer-group throttling attribution and the host's replacement behavior before release. A public launch additionally requires decisions on official integration eligibility, delegated authentication or cleared credential onboarding, persistent connection custody, and coordination across instances.
