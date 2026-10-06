using System.Net;
using System.Text.Json;
using Kronan.McparIs.Authentication;
using Kronan.McparIs.Infrastructure;
using Kronan.McparIs.KronanApi;
using Kronan.McparIs.Models;
using Kronan.McparIs.Options;
using Microsoft.Extensions.Options;

namespace Kronan.McparIs.Services;

public sealed class ApiOperationService(KronanClient client, HouseholdAccess access, ApiMutationCoordinator mutations,
    IOptions<AuthenticationOptions> authentication, IOptions<ApiOptions> apiOptions)
{
    public async Task<ApiResult> ReadAsync(string operationId, JsonElement? request, CancellationToken cancellationToken)
    {
        var operation = ApiOperationRegistry.Get(operationId);
        if (operation.Mode != ApiOperationMode.Read) throw new ServiceFailure("invalid_input", "This API operation requires a prepared mutation.");
        Require(access, authentication.Value, operation.Scope);
        var bound = ApiOperationRegistry.Bind(operation, request);
        var response = await client.SendOperationAsync(operation.Method, bound.Path, bound.Body, operation.MayChangeState,
            Accepted(operation), ApiOperationRegistry.FeatureDescription(operation), cancellationToken);
        return new ApiResult(response.StatusCode, response.Content);
    }

    public PreparedApiOperation Prepare(string operationId, JsonElement? request)
    {
        var operation = ApiOperationRegistry.Get(operationId);
        if (operation.Mode == ApiOperationMode.Read) throw new ServiceFailure("invalid_input", "Read operations do not need preparation.");
        var actor = Require(access, authentication.Value, operation.Scope);
        ApiOperationRegistry.Bind(operation, request); // Validate before reserving an operation ID.
        return mutations.Prepare(actor, operation, request);
    }

    public async Task<ApiResult> ExecuteAsync(string operationId, string preparedOperationId, JsonElement? request, CancellationToken cancellationToken)
    {
        var operation = ApiOperationRegistry.Get(operationId);
        if (operation.Mode == ApiOperationMode.Read) throw new ServiceFailure("invalid_input", "Read operations do not accept an operation ID.");
        if (operation.Mode == ApiOperationMode.Commit && !apiOptions.Value.EnableCommitOperations)
            throw new ServiceFailure("commit_operations_disabled", "This order, checkout, or slot reservation operation is registered but disabled until the server owner enables commit operations after reviewing its real-world effects.");
        var actor = Require(access, authentication.Value, operation.Scope);
        var bound = ApiOperationRegistry.Bind(operation, request);
        var result = await mutations.ExecuteAsync(actor, operation, preparedOperationId, request, async token =>
        {
            var response = await client.SendOperationAsync(operation.Method, bound.Path, bound.Body, operation.MayChangeState,
                Accepted(operation), ApiOperationRegistry.FeatureDescription(operation), token);
            return new ApiResult(response.StatusCode, response.Content);
        }, cancellationToken);
        return result;
    }

    private static IReadOnlySet<HttpStatusCode>? Accepted(ApiOperationDefinition operation) => operation.AcceptedNonSuccess is { Length: > 0 } statuses
        ? statuses.Select(status => (HttpStatusCode)status).ToHashSet() : null;

    private static HouseholdActor Require(HouseholdAccess householdAccess, AuthenticationOptions options, ApiOperationScope scope) => scope switch
    {
        ApiOperationScope.Catalog => householdAccess.Require(options.CatalogScope),
        ApiOperationScope.ShoppingRead => householdAccess.Require(options.ShoppingReadScope),
        ApiOperationScope.ShoppingWrite => householdAccess.Require(options.ShoppingWriteScope),
        ApiOperationScope.AccountRead => householdAccess.Require(options.AccountReadScope),
        ApiOperationScope.AccountWrite => householdAccess.Require(options.AccountWriteScope),
        ApiOperationScope.PaymentsRead => householdAccess.Require(options.PaymentsReadScope),
        ApiOperationScope.Commit => householdAccess.Require(options.CheckoutCommitScope),
        _ => throw new InvalidOperationException()
    };
}
