# Krónan API tool request examples

Tools that accept `request` take an object with optional `path`, `query`, and `body` objects. Pass this object directly as the MCP `request` argument. Do not serialize it into a string, and do not include a URL, authorization header, or API key.

```json
{"request":{"path":{"slug":"01-00-00-avextir"},"query":{"page":1}}}
```

The examples below cover the inputs that commonly need a value from a preceding read.

| Tool | `request` example | Prerequisite |
| --- | --- | --- |
| `GetCategoryProducts` | `{"path":{"slug":"01-00-00-avextir"}}` | `ListCategories` supplies the slug. |
| `GetProductList` | `{"path":{"token":"…"}}` | `ListProductLists` supplies the token. |
| `GetOrder` | `{"path":{"token":"…"}}` | `ListOrders` supplies the token. |
| `GetProductByBarcode` | `{"path":{"barcode":"…"}}` | A real product barcode is required. |
| `ListProductsByTag` | `{"path":{"slug":"icelandic"},"query":{"page":1}}` | `ListProductTags` supplies the slug. |
| `GetRecipe` | `{"path":{"slug":"…"}}` | `ListRecipes` or `SearchRecipes` supplies the slug. |
| `GetStoreProduct` | `{"query":{"sku":"02400069"}}` | Use exactly one of `sku` or `barcode`. |
| `GetProductsBatch` | `{"body":{"skus":["02400069"]}}` | `skus` is required. |
| `SearchRecipes` | `{"body":{"query":"pasta","page":1}}` | All recipe filters are optional. |
| `PreviewCheckoutLines` | `{"body":{"lines":[{"sku":"02400069","quantity":1}]}}` | `lines` is required; this only previews. |
| `SearchStoreProducts` | `{"body":{"query":"hvítlaukur","store":"168"}}` | `query` and `store` are required; `store` is the Scan & Go `ext_id`, not `ext_id` as a body key. |
| `ListDeliverySlots` | `{"body":{"addressId":123}}` | `addressId` is required and comes from `ListAddresses`. |

The server rejects a missing required path/query/body field locally before sending a request upstream. Upstream validation remains the final authority for nested line-item shapes and enum values. `GetApiSchema` defaults to `format=json`; pass `{"query":{"format":"yaml"}}` only when a YAML response is intentional.
