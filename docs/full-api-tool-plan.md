# Complete Krónan API tool implementation plan

Implement one explicitly mapped MCP endpoint tool for every operation in the published Krónan OpenAPI schema: **63 operations across 13 resource groups**. Retain useful preparation and visual helpers in addition to those endpoint tools. The existing implementation covers seven API operations; this plan adds 56 and extends the existing seven to cover their documented inputs and response behavior.

## Implementation status

Implemented on `feat/household-shopping-plugin`: a fixed 63-operation registry constrains method, route, path/query arguments, accepted non-success responses, scope, and mutation classification; the server registers the matching 63 endpoint tools plus preparation and presentation helpers. All non-read endpoints require an operation ID bound to the authenticated caller and exact request. Checkout submission, order creation, and slot reservations stay registered but disabled until `Api__EnableCommitOperations=true` is deliberately set. The 63-operation registry and complete 67-tool registration set are exercised by local contract and MCP integration tests.

Scope is the documented public API, including identity, addresses, checkout, named product lists, orders, delivery and pickup slots, gift-card reads, purchase history, recipes, catalogue, shopping notes, and the schema endpoint. Undocumented website APIs and features marked “coming soon” are outside this count. In particular, the schema does not offer gift-card payment initiation yet. Do not invent a payment endpoint to fill that gap.

Baseline: schema version 1.0.1 fetched October 6, 2026 from [the JSON schema](https://api.kronan.is/api/v1/schema/?format=json), with [ReDoc](https://api.kronan.is/api/v1/schema/redoc/) as the human-readable reference. The API is beta; coverage must track the actual operation set, not just its version string. Schema SHA256: `a203c8cf23f655619bc32cd10c658741daf80a569a620450db80201304a0c5d1`.

## Resource identity and website mapping

The first milestone is a diagnostic read flow: GetIdentity → GetActiveCheckout → ListProductLists → GetProductList for the exact named list → GetShoppingNote. Show the returned identity type/name, resource type, resource token, names, line count, and summed quantity as distinct fields. Identity names are not stable authorization IDs.

Compare these results with the user's eight cart items and one item in a list called “Shopping.” Check both distinct line count and quantity total; eight items need not mean eight lines. Resolve the exact list token from paginated results, and never select the first list or a similarly named list implicitly. If multiple lists share a name, ask the user to select one. Do not add, delete, or complete anything for this diagnostic exercise. GET checkout and shopping note can auto-create empty resources, so report that caveat before using those reads where no resource is known to exist.

Checkout is a candidate for the website cart, product lists are candidates for saved website lists, and shopping notes are a separate free-form resource. Confirm the mappings through actual read results and a matching signed-in user/customer-group context. A matching count alone is insufficient proof; compare products and quantities. Label unverified mappings as unverified rather than claiming synchronization.

Use precise tool descriptions and UI titles: “Shopping note,” “Saved product list: Shopping,” and “Active checkout.” Introduce “Cart” as an alias only after its mapping is verified. Replace ambiguous “Add to list” buttons with a chosen destination and exact action. Free text is supported by shopping notes; product lists and checkout accept products by SKU. Never convert a free-text note into a catalogue item silently.

## Endpoint coverage

Every row below has a dedicated typed MCP tool, direct method/path adapter, validation, authorization, error mapping, and contract tests. Paths are relative to `/api/v1/`. “Extend existing” means implementation exists for that operation but must be audited for all documented options. Read classification is semantic: several POST operations are reads; two GET operations can create state. “Commit” marks order/authorization-related operations requiring explicit review. Each named endpoint tool remains discoverable; domain categories must not silently hide endpoints or replace them with a generic arbitrary-HTTP tool.

### Addresses

| Method and path | Proposed tool | Status | Phase | Behavior |
| --- | --- | --- | --- | --- |
| `GET addresses/` | `ListAddresses` | New | 1 | Read |

### Categories

| Method and path | Proposed tool | Status | Phase | Behavior |
| --- | --- | --- | --- | --- |
| `GET categories/` | `ListCategories` | Extend existing | 2 | Read |
| `GET categories/{slug}/products/` | `GetCategoryProducts` | New | 2 | Read |

### Checkout

| Method and path | Proposed tool | Status | Phase | Behavior |
| --- | --- | --- | --- | --- |
| `GET checkout/` | `GetActiveCheckout` | New | 1 | Read with creation |
| `POST checkout/add-to-order/` | `AddCheckoutToActiveOrder` | New | 5 | Commit |
| `POST checkout/complete/` | `CompleteCheckout` | New | 5 | Commit |
| `POST checkout/lines/` | `UpdateCheckoutLines` | New | 3 | Write |
| `POST checkout/preview-lines/` | `PreviewCheckoutLines` | New | 2 | Read |

### Me

| Method and path | Proposed tool | Status | Phase | Behavior |
| --- | --- | --- | --- | --- |
| `GET me/` | `GetIdentity` | New | 1 | Read |

### Orders

| Method and path | Proposed tool | Status | Phase | Behavior |
| --- | --- | --- | --- | --- |
| `GET orders/` | `ListOrders` | New | 4 | Read |
| `GET orders/{token}/` | `GetOrder` | New | 4 | Read |
| `POST orders/{token}/delete-lines/` | `DeleteOrderLines` | New | 4 | Write |
| `POST orders/{token}/lines-toggle-substitution/` | `ToggleOrderLineSubstitution` | New | 4 | Write |
| `POST orders/{token}/lower-quantity-lines/` | `LowerOrderLineQuantities` | New | 4 | Write |
| `GET orders/currently-active/` | `GetActiveOrder` | New | 4 | Read |
| `GET orders/line-summary/` | `GetOrderLineSummary` | New | 4 | Read |

### Payments

| Method and path | Proposed tool | Status | Phase | Behavior |
| --- | --- | --- | --- | --- |
| `GET payments/balance/` | `GetGiftCardBalance` | New | 4 | Read |
| `GET payments/transactions/` | `ListGiftCardTransactions` | New | 4 | Read |

### Product Lists

| Method and path | Proposed tool | Status | Phase | Behavior |
| --- | --- | --- | --- | --- |
| `GET product-lists/` | `ListProductLists` | New | 1 | Read |
| `POST product-lists/` | `CreateProductList` | New | 3 | Write |
| `GET product-lists/{token}/` | `GetProductList` | New | 1 | Read |
| `PATCH product-lists/{token}/` | `UpdateProductList` | New | 3 | Write |
| `DELETE product-lists/{token}/` | `DeleteProductList` | New | 3 | Write |
| `POST product-lists/{token}/batch-add-items/` | `BatchAddProductListItems` | New | 3 | Write |
| `DELETE product-lists/{token}/delete-all-items/` | `ClearProductList` | New | 3 | Write |
| `POST product-lists/{token}/sort-items/` | `SortProductListItems` | New | 3 | Write |
| `POST product-lists/{token}/update-item/` | `UpdateProductListItem` | New | 3 | Write |

### Product Purchase Stats

| Method and path | Proposed tool | Status | Phase | Behavior |
| --- | --- | --- | --- | --- |
| `GET product-purchase-stats/` | `ListProductPurchaseStats` | New | 3 | Read |
| `PATCH product-purchase-stats/{id}/set-ignored/` | `SetProductPurchaseIgnored` | New | 3 | Write |

### Products

| Method and path | Proposed tool | Status | Phase | Behavior |
| --- | --- | --- | --- | --- |
| `GET products/{sku}/` | `GetProduct` | Extend existing | 2 | Read |
| `GET products/barcode/{barcode}/` | `GetProductByBarcode` | New | 2 | Read |
| `POST products/batch/` | `GetProductsBatch` | New | 2 | Read |
| `GET products/by-tag/{slug}/` | `ListProductsByTag` | New | 2 | Read |
| `GET products/favorites/` | `ListFavoriteProducts` | New | 2 | Read |
| `GET products/on-sale/` | `ListProductsOnSale` | New | 2 | Read |
| `POST products/search/` | `SearchProducts` | Extend existing | 2 | Read |
| `GET products/tags/` | `ListProductTags` | New | 2 | Read |

### Recipes

| Method and path | Proposed tool | Status | Phase | Behavior |
| --- | --- | --- | --- | --- |
| `GET recipes/` | `ListRecipes` | New | 3 | Read |
| `GET recipes/{slug}/` | `GetRecipe` | New | 3 | Read |
| `POST recipes/{slug}/favorite/` | `FavoriteRecipe` | New | 3 | Write |
| `DELETE recipes/{slug}/favorite/` | `UnfavoriteRecipe` | New | 3 | Write |
| `GET recipes/favorites/` | `ListFavoriteRecipes` | New | 3 | Read |
| `POST recipes/search/` | `SearchRecipes` | New | 3 | Read |

### Schema

| Method and path | Proposed tool | Status | Phase | Behavior |
| --- | --- | --- | --- | --- |
| `GET schema/` | `GetApiSchema` | New | 2 | Read |

### Shopping Notes

| Method and path | Proposed tool | Status | Phase | Behavior |
| --- | --- | --- | --- | --- |
| `GET shopping-notes/` | `GetShoppingNote` | Rename existing GetShoppingList | 3 | Read with creation |
| `POST shopping-notes/add-line/` | `AddShoppingNoteItem` | New | 3 | Write |
| `POST shopping-notes/add-lines/` | `AddShoppingItems` | Extend existing | 3 | Write |
| `PATCH shopping-notes/change-line/` | `UpdateShoppingItem` | Extend existing | 3 | Write |
| `PATCH shopping-notes/change-placement/` | `ReorderShoppingNoteLines` | New | 3 | Write |
| `DELETE shopping-notes/delete-line/` | `RemoveShoppingItem` | Extend existing | 3 | Write |
| `DELETE shopping-notes/delete-line-archived/` | `DeleteArchivedShoppingNoteLine` | New | 3 | Write |
| `DELETE shopping-notes/delete-shopping-note/` | `ClearShoppingNote` | New | 3 | Write |
| `GET shopping-notes/is-eligible-for-store-product-order/` | `CheckShoppingNoteStoreOrderEligibility` | New | 3 | Read |
| `GET shopping-notes/lines-archived/` | `ListArchivedShoppingNoteLines` | New | 3 | Read |
| `GET shopping-notes/product/` | `GetStoreProduct` | New | 3 | Read |
| `GET shopping-notes/scan-n-go-stores/` | `ListScanAndGoStores` | New | 3 | Read |
| `POST shopping-notes/search/` | `SearchStoreProducts` | New | 3 | Read |
| `POST shopping-notes/store-product-order/` | `SortShoppingNoteByStore` | New | 3 | Write |
| `PATCH shopping-notes/toggle-complete-on-line/` | `ToggleShoppingNoteLineCompletion` | New | 3 | Write |

### Slots

| Method and path | Proposed tool | Status | Phase | Behavior |
| --- | --- | --- | --- | --- |
| `POST slots/delivery/` | `ListDeliverySlots` | New | 4 | Read |
| `POST slots/delivery/reserve/` | `ReserveDeliverySlot` | New | 5 | Commit |
| `POST slots/pickup/` | `ListPickupSlots` | New | 4 | Read |
| `POST slots/pickup/reserve/` | `ReservePickupSlot` | New | 5 | Commit |

## Shared implementation

Keep the current .NET service and hosted Streamable HTTP transport. Add resource-specific clients/services/tools for checkout, product lists, orders, slots, identity/addresses, recipes, purchase statistics, payments, catalogue, and shopping notes. Keep transport DTOs separate from display models so field normalization never changes the upstream contract. Retain the real shared HttpClient configuration, response bounds, cancellation, credential handling, and request budget for every adapter.

Commit a reviewed OpenAPI snapshot and a machine-readable coverage manifest containing operationId, HTTP method/path, tool name, owning service, scope, behavior, implementation status, and tests. Generate DTOs and fixture scaffolding where useful, then review every wrapper. Do not blindly generate mutation semantics from HTTP verbs or descriptions. Keep the one-tool-per-operation mapping, including single versus batch additions and the schema operation.

Expose all documented filters, sort choices, enum values, flags, paths, bodies, and pagination mechanisms. Current SearchProducts needs its documented sorting/page sizing/detail/purchase-history options instead of hard-coded choices. GetApiSchema must provide the schema contract in bounded sections or through an MCP resource plus a content summary; it must not silently truncate a full document. Do not expose the API key, arbitrary base URLs, raw authorization headers, or caller-selected household identifiers as tool arguments.

Preserve schema wire casing, null/omitted distinctions, required arrays, UUID/integer/string identifier types, money in integer ISK, and documented UTC timestamps. Documentation prose and field definitions sometimes disagree about casing; use operation schemas and tested wire behavior, with explicit serialization names. Required fields remain required; do not invent defaults. Paginated tools return count/cursor/offset/page information and allow callers to continue. Clamp undocumented/unbounded page sizes conservatively, explain partial output, and never auto-fetch the entire API in one call. Cap schema/response sizes without presenting incomplete data as complete.

Map meaningful responses explicitly: 204 eligibility success becomes a boolean; its documented 404 means ineligible, not a transport error. A missing active order has a distinct “none” result. Validate content type before parsing and handle genuinely empty success bodies. Distinguish missing resources, invalid input, permission/credential failures, rate limits, and unknown mutation outcomes without exposing upstream internals.

## Authentication and sensitive data

Keep the server-held AccessToken separate from inbound OAuth. Validate signature, issuer, audience, expiry, household membership, and operation-specific scope for every tool and component call. Never derive authorization from the identity display name or tool arguments. Resolve nested list/order/line ownership against the authenticated upstream context before mutations; upstream authorization remains the final boundary.

Retain catalog:read and shopping:read/write for catalogue and shopping notes. Add separate identity:read, addresses:read, product-lists:read/write, checkout:read/write/submit, orders:read/write, slots:read/reserve, recipes:read/write, purchase-history:read/write, and payments:read permissions. The schema tool needs member access and docs:read. Keep permissions explicit in metadata and policies. Do not map existing shopping:write permission automatically to order submission or financial reads. Document OAuth consent/migration and test refusal when only old scopes are present. Provider-specific scope names must be configured and validated, including Entra oid/scp handling.

Addresses, order history, spending, gift-card balances, and transactions are sensitive. Return only requested data, avoid logging their contents, and use actor/credential-partitioned caches only where appropriate. Never cache private responses in a shared public catalogue bucket. Restrict financial/order fields in cards to the authorized viewer; keep credentials out of cards and plugin packages. Entra remains subject to the existing discovery/PKCE/callback/resource/refresh interoperability gate.

## Mutation safety and rate limiting

Generalize preparation to a typed operation enum covering every mutation. Bind prepared records to verified actor, exact upstream operation, target resource identifiers, normalized arguments, expiry, and the current process generation. Bind review snapshots to state/version fingerprints where available. Mutation tools require the matching operation ID; a generic preparation helper is additional to the 63 endpoint tools, not a replacement for them. Preserve old shopping preparation behavior through an explicit migration/compatibility path.

Use per-resource serialization within the process plus the shared upstream budget. Revalidate ownership and important preconditions immediately before dispatch. The API does not supply a documented conditional-write/idempotency guarantee; a reread/fingerprint reduces risk but does not eliminate concurrent website/app changes. Return replayed results as prior-action snapshots, and fetch current state when the UI needs it.

Keep the conservative global budget of 120 attempts per rolling 200 seconds, two-second dispatch pacing, two concurrent upstream requests, shared Retry-After cooldown, bounded tool/mutation bursts, and 200-second restart cooldown. Every lookup, preview, validation read, mutation, and reconciliation call spends that budget. Batch within each endpoint's limits: product lookup 100 SKUs, shopping/product-list additions 30, documented order operations up to 100 lines, historical summary up to 10 SKUs. Never bypass the limiter from cards or future Alexa adapters. Do not automatically split a user mutation into multiple batches without exposing partial-success semantics.

Preserve the no-application-database constraint and managed-secret credential persistence. Bounded in-memory replay records expire and disappear on restart; old IDs fail closed. Use one process and the established no-overlap deployment procedure. Do not claim durable exactly-once processing. Store recent raw upstream outcomes and state checks only in bounded memory. On network timeout or a failed response after possible dispatch, reconcile before proposing a new action; never retry toggles, additive writes, reservation, order creation, or checkout submission automatically.

For toggles, retain the explicit API toggle tools, mark them non-idempotent, and protect replay. Optionally add desired-state helpers for note completion and order substitution that read current state and toggle only when needed. Such helpers are additional to endpoint coverage. Unknown toggle outcomes still require reconciliation.

## Operations requiring particular care

- Checkout lines: `replace` defaults to **true** in the upstream schema. The tool must require an explicit append/replace choice and always send the chosen boolean. Show the existing items that replacement would remove. Preview product availability before changes; expose the exact API behavior for zero quantity and existing SKUs after validation, rather than assuming conventional cart semantics.
- Product lists: require an explicit list token for item changes. Quantity zero removes an item. Whole-list deletion and clear-all require a review of the named destination and affected items. No first-list selection shortcut.
- Shopping note: empty change-line bodies delete upstream. Keep the existing guard and use the dedicated deletion tool for ordinary removals; document that endpoint's deletion variant as a guarded explicit option if complete request-shape fidelity is required. Clear note, archived-line deletion, completion toggles, and reorder/sort each need correct replay handling. Respect documented query-array encoding for placement tokens.
- Orders: preserve server restrictions on picking-started lines, service lines, last remaining line, and quantity lowering. Zero quantity removes a line. Do not silently treat an order edit as a cart edit. Prepare a review for changes affecting an existing order.
- Reservations and checkout completion: slot reservation responses include an order token and `authorizedAmount`, so classify reservation as a potentially committing operation. The API descriptions are insufficient to establish its full ordering/payment effects. Confirm those semantics before enabling dispatch in a household deployment. Keep all four Commit tools implemented and discoverable, with a structured unavailable/precondition response until this gate is resolved rather than omitting them from coverage.
- Gift cards: balance and transactions are reads. No payment-initiation tool exists in this schema; do not imply checkout completion proves payment settlement. Historical order-line totals are not payment/refund accounting.

For Commit operations, prepare an exact review showing target order/checkout, products, quantities, amount and fees where known, address/pickup location, slot, and consequences. Any unknown monetary consequence must be stated. Require explicit user approval through the host's supported approval mechanism or an authenticated interactive confirmation before executing; a model-supplied `confirmed=true` is insufficient. Bind approval to the actor, arguments, review state, and expiration. Recheck stale availability/prices/slots before dispatch, and require renewed approval for material changes. No automatic live tests may place an order, reserve a slot, or authorize funds.

## Visual output

Keep raw endpoint tools usable without cards. Extend separate render helpers for shopping notes, product-list selection/details, checkout review, recipe details, available slots, and order details. Render helpers use the same services and budgets and do not count toward endpoint coverage. Reuse catalogue cards with explicit Add to shopping note, Add to saved list, or Add to checkout actions. Require list selection where necessary.

Each card shows the resource type/name and a short identifier, and separates live fetched state from a previous action snapshot. Display clear success/error/unknown statuses, explicit destructive confirmation, and a destination-specific Refresh action. Product-list edits must not refresh a shopping note. Payment/spend visualizations are optional and restricted to explicitly requested authorized data. Maintain text escaping, exact CDN CSP origins, host bridge compatibility, keyboard/mobile support, and server-side permission checks for every button.

## Delivery phases

1. **Identity and website reconciliation:** GetIdentity, ListAddresses, GetActiveCheckout, ListProductLists, GetProductList. Establish identity context and resource mapping using the eight-item cart and “Shopping” example without explicit mutations. Add diagnostic tools before any further shopping UI assumptions.
2. **Contract foundation and remaining catalogue reads:** snapshot/manifest coverage checks, shared DTO/error/pagination adapters, all products/categories tools, checkout preview, schema tool, preparation generalization, and scope infrastructure. Preserve existing tool compatibility while correcting their limited parameter coverage.
3. **Lists notes recipes and history:** all product-list and checkout-line mutations, remaining shopping-note tools, all recipes and purchase-stat tools, and the destination-specific cards. Extend existing shopping tools rather than creating two independent control paths.
4. **Orders slots and gift-card reads:** all order/history reads, guarded order-line mutations, delivery/pickup availability, and gift-card balance/transaction reads. Show cancellation/eligibility and historical-data semantics precisely.
5. **Commit operations:** implement/verify add-checkout-to-order, complete-checkout, delivery reservation, and pickup reservation with approval, stale-state handling, and uncertain-outcome reconciliation. Conduct live tests only after explicit authorization and verified consequences. Then validate the full hosted OAuth/workspace/mobile experience. Alexa remains a later adapter to these same services, not part of endpoint coverage.

Use focused commits/PRs for the phases, keep a human-readable coverage status report updated, and do not declare the whole API implemented after phase 1. A locally implemented but deployment-gated operation is reported separately from an enabled, live-verified tool.

## Verification and definition of complete

CI must compare the pinned OpenAPI operation set with the coverage manifest, reject missing/duplicate mappings and unexplained operations, and assert all endpoint tools are registered with expected scopes/annotations. A live-schema drift check produces a reviewed update; do not generate new production mutation tools automatically when upstream changes. Maintain the single-versus-batch distinctions and schema operation in coverage.

For each operation, contract tests verify HTTP method/path, input placement and casing, headers, required/optional/null values, bounds and enums, pagination, all documented response statuses, and sanitized failures. Also test implicit creation, replace=true behavior, toggles, ordering query arrays, optional pickup body, and deletion-by-zero/empty-body semantics. Generate fixtures from schema plus sanitized verified samples where available; synthetic tests alone do not establish upstream compatibility.

Cross-cutting integration tests cover expired/wrong-audience/wrong-issuer JWTs, outsiders and missing scopes, cross-resource IDs, bounded caches/results, shared quotas, cancellation, concurrency, operation argument conflicts, repeat delivery, post-dispatch unknown outcomes, restart/expiry, approval binding, stale checkout state, and unauthorized financial reads. UI tests cover destination selection, correct refresh source, precise labels, confirmation, partial pagination, private-data visibility, and uncertain-action recovery.

Live acceptance starts with identity and read comparisons. Use a deliberately named disposable saved list/note only after authorization for controlled writes. Verify website/API state after each authorized change instead of inferring it from a successful response. Treat slot/order/financial tests as a separate explicit approval step with a recovery procedure. Never run blanket “try every tool” against a real account.

Complete means all **63 operations** have an explicit mapped, implemented, registered and tested endpoint tool; all documented request variants are represented or have an explicit reviewed guard; no resource is silently omitted; every deployment gate and live-verification status is visible. Existing preparation/render tools and optional desired-state helpers are counted separately. Cart/list UI equivalence is claimed only where the real account comparison establishes it.
