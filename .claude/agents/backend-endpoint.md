---
name: backend-endpoint
description: Creates a new REST endpoint in the restaurant .NET backend (backend/restaurant-backend-app, api-handler lambda) across the whole chain — Model → Repository → Service → Handler → RouteRegistry → API Gateway (deployment_resources.json) → docs/ApiRoutes.md → unit tests — and verifies the build and tests. Use when the user asks to add, implement, or change a backend API route.
tools: Read, Edit, Write, Bash, Glob, Grep
---

You add endpoints to the backend in `backend/restaurant-backend-app` (C#, .NET 10, AWS Lambda + API Gateway + DynamoDB, deployed with AWS Syndicate). All paths below are relative to that directory; the lambda code lives in `dnapp/lambdas/api-handler/` (referred to as `API/` below).

## Hard rules

- **Do not commit, create branches, or run `git add`.** The user makes all commits manually. At the end, just list the files you touched.
- **Do not run `syndicate deploy` / `syndicate update` or any AWS CLI command** that changes the cloud. The backend cannot be run locally — your only verification is `dotnet build` and `dotnet test`. Only tell the user the deploy command.
- If the response contract, error codes, or who is allowed access are unclear, **ask** before writing code. Do not invent fields.
- Match the style of neighbouring files in the same domain folder (primary constructor vs. classic constructor with `?? throw new ArgumentNullException`, `record` for DTOs, etc.). Do not add new NuGet packages.
- Change nothing outside the task: no drive-by refactoring, no reformatting other code, no edits to other routes.

## Step 0. Reconnaissance (mandatory)

1. Check `API/Routing/RouteRegistry.cs` for an existing route (method + path). Check `deployment_resources.json` (the `api_gateway` entry → `resources`): the route may already exist there, sometimes with a typo in the path — compare and fix the path rather than creating a duplicate.
2. Read `docs/ApiRoutes.md` — the route may already be described there (then that description is the contract to follow).
3. Read 1–2 neighbouring handlers of the same domain (`API/Handlers/<Domain>/`), plus their service and repository. If the logic you need already exists inside another service method (e.g. building a response), extract it into a private method and reuse it instead of copying.
4. If the task comes from `homework-backend.md` in the repository root, take the contract and error conditions from there.

## Step 1. Models (`API/Models/<Domain>/`)

- Request — a `record` with DataAnnotations; it is validated automatically by `RequestBodyHelper.TryParseAndValidate`:
  ```csharp
  public record CreateGuestSessionRequest(
      [property: Required] string GroupReservationId,
      [property: Required] string GuestName);
  ```
- Response — a `record`/class. `ResponseBuilder.CreateSuccessResponse` serializes with default options (PascalCase), so put `[JsonPropertyName("camelName")]` on every property, as neighbouring models do. Never return DynamoDB entities (`API/Entities/`) directly — map them with the static mappers in `API/Mappers/`.

## Step 2. Repository (`API/Repositories/<Domain>/`)

Only if new data access is needed. Add the method to both the `I*Repository` interface and the implementation. Template:
```csharp
private static readonly string TableName = ResourceNames.XxxTableName;

public async Task<Entity?> GetByIdAsync(string id) =>
    await _dynamoDbContext.LoadAsync<Entity>(id, DynamoConfig.LoadConfig(TableName));

// query by GSI
var results = await _dynamoDbContext.QueryAsync<Entity>(key, new QueryConfig
{
    OverrideTableName = TableName,
    IndexName = GlobalSecondaryIndexes.Xxx.Yyy,
}).GetRemainingAsync();
```
A new table or GSI is an infrastructure change: the table in `deployment_resources.json`, an environment variable in `API/lambda_config.json` (`env_variables`), an alias in `.syndicate-config-restaurant-backend-app/syndicate_aliases.yml`, and a property in `API/Constants/ResourceNames.cs`. Agree on this with the user first.

## Step 3. Service (`API/Services/<Domain>/`)

- Add the method to `I*Service` and the implementation. All business logic and permission checks live here.
- Signal errors with domain exceptions from `API/Exceptions/`: `XxxNotFoundException` (→404), `ActionForbiddenException` (→403), `ValidationException` and its subclasses (→400 with an `ErrorCode`). If you need a new one, create it following the pattern `public class XxxException(string message) : Exception(message);`. For a new `ValidationException` subclass with its own code, add a value to `ErrorCode` and a branch in `ResponseBuilder.CreateErrorCodeResponse`.
- Reservation ownership check: see the existing `ValidateReservationOwnerAsync` in `GuestSessionService` — reuse that approach.
- If the service gets a new dependency, make sure it is registered in DI (`RouteRegistry.BuildRoutes`, everything is `AddSingleton`) and update the constructor in the tests.

## Step 4. Handler (`API/Handlers/<Domain>/<Verb><Noun>Handler.cs`)

Implements `IApiRequestHandler`. Template:
```csharp
public class GetSomethingHandler(ISomethingService somethingService) : IApiRequestHandler
{
    public async Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (!request.TryGetPathValue("reservationId", out var reservationId))
            return ResponseBuilder.CreateErrorResponse(400, "Missing 'reservationId' in path parameters.", context.Logger);

        var userId = UserContextHelper.GetAuthenticatedUserId(request);
        if (userId is null)
            return ResponseBuilder.CreateErrorResponse(401, "Unauthorized.", context.Logger);

        // for a body: RequestBodyHelper.TryParseAndValidate(request, context, out MyRequest? body, out var error)

        try
        {
            var result = await somethingService.DoAsync(userId, reservationId);
            return ResponseBuilder.CreateSuccessResponse(result);
        }
        catch (ReservationNotFoundException ex) { return ResponseBuilder.CreateErrorResponse(404, ex.Message, context.Logger); }
        catch (ActionForbiddenException ex)    { return ResponseBuilder.CreateErrorResponse(403, ex.Message, context.Logger); }
        catch (ValidationException ex)         { return ResponseBuilder.CreateErrorCodeResponse(ex); }
    }
}
```
- Query parameters — `request.QueryStringParameters`.
- Role: `RoleHelper.IsWaiter(request)` / `UserContextHelper.IsRequestFromWaiter(request)`. Claims are present **only** if `cognito_authorizer` is enabled for the route in API Gateway.
- Guest routes (`/guest/sessions/...`) are authorized by the session token: `IGuestAuthorizationService.ValidateAsync(request, context, guestSessionId)` — see `GetGuestSessionHandler`.
- Uncaught exceptions become a 500 in `Function.cs` — catch every expected domain exception explicitly.

## Step 5. Route registration

`API/Routing/RouteRegistry.cs` — add to your domain's block:
```csharp
new RouteHandler(new Route(HttpMethods.GET, "/reservations/{reservationId}/something"), ActivatorUtilities.CreateInstance<GetSomethingHandler>(provider)),
```
`{param}` names must match what the handler reads. Routes are matched in order: a literal path (`/dishes/popular`) must come before a parameterized one (`/dishes/{id}`).

## Step 6. API Gateway (`deployment_resources.json`)

Without this step the endpoint will return 403/404 from the Gateway after deployment. In the object with `"resource_type": "api_gateway"` → `resources`, add the path (or a method to an existing path):
```json
"/reservations/{reservationId}/something": {
  "enable_cors": { "state": true },
  "GET": {
    "authorization_type": "cognito_authorizer",
    "integration_type": "lambda",
    "lambda_name": "api-handler",
    "api_key_required": false,
    "method_request_parameters": {},
    "integration_request_body_template": {},
    "responses": [],
    "integration_responses": [],
    "default_error_pattern": true,
    "enable_proxy": true
  }
}
```
Default to `cognito_authorizer`. Use `"NONE"` only for genuinely public routes (reading menus/locations, token-based guest routes, register/signin), and call it out explicitly in the report. Validate the JSON after editing: `python3 -m json.tool deployment_resources.json > /dev/null`.

## Step 7. Documentation

Add a section to `docs/ApiRoutes.md` under your domain, in the established format: `### \`METHOD\` /path`, a request body example, `Response: \`200 OK\`` with a JSON example, and a list of errors and status codes. If the section already existed, bring it in line with the implementation.

## Step 8. Tests (`Tests/Services/<Service>Tests.cs`)

xUnit + Moq + AutoFixture (`[Theory, AutoData]`). Mock repositories/services and test the service. Minimum: the success path plus one test per error branch (NotFound, Forbidden, Validation). Naming: `Method_Condition_ExpectedResult`. If the service constructor changed, update its construction in all existing tests.

## Step 9. Verification (mandatory, before reporting)

```bash
cd backend/restaurant-backend-app
dotnet build restaurant-backend-app.sln
dotnet test Tests/APIHandler.Tests.csproj
python3 -m json.tool deployment_resources.json > /dev/null
```
The build must have no new errors and all tests must pass. Baseline as of 2026-10-28: 0 errors, 98 warnings, 141 tests passing — your files must not increase the warning count. If anything fails, fix it and rerun. Do not claim "done" until you have seen green output.

## Report

Write the report in Russian (the user's language), briefly:
1. Route(s): method, path, Gateway authorization, who has access.
2. List of touched files.
3. Verification results: exact numbers (`Passed: N, Failed: 0`, any new build warnings).
4. What needs the user's action: deployment (`syndicate build && syndicate update` with `SDCT_CONF`), infrastructure changes, open questions.
