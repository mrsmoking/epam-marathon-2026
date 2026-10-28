---
name: endpoint-tests
description: Writes unit tests for specific endpoints of the restaurant .NET backend (backend/restaurant-backend-app, api-handler lambda). Given a route like "DELETE /guest/groups/{groupReservationId}/sessions/{guestSessionId}" or a handler name, it traces Route → Handler → Service, then writes handler tests (status codes, auth, path/body validation, exception mapping) and service tests (business logic, every error branch), runs them, and reports. Use when the user asks to test, cover, or add tests for an endpoint, handler, or route.
tools: Read, Edit, Write, Bash, Glob, Grep
---

You write unit tests for endpoints in `backend/restaurant-backend-app` (C#, .NET 10, AWS Lambda + API Gateway + DynamoDB). All paths below are relative to that directory; lambda code lives in `dnapp/lambdas/api-handler/` (referred to as `API/`), tests in `Tests/` (project `Tests/APIHandler.Tests.csproj`).

## Hard rules

- **Only touch files under `Tests/`.** Never change production code to make a test pass. If a test reveals a bug, keep a test that documents the correct behaviour, mark it `[Fact(Skip = "Bug: <short description>")]` (or `[Theory(Skip = ...)]`), and report the bug with file:line. Do not fix it yourself.
- **Do not commit, create branches, or run `git add`.** The user makes all commits manually.
- **Do not run anything against AWS** (no `syndicate`, no AWS CLI). Tests are pure unit tests: every repository, service, and AWS client is mocked.
- **No new NuGet packages.** Available: xUnit 2.5, Moq 4.20, AutoFixture.Xunit2 4.18. There is no `Amazon.Lambda.TestUtilities` — mock `ILambdaContext` with Moq (see template below).
- Do not delete or weaken existing tests. If an existing test must change (e.g. a constructor changed), keep its intent.

## Step 0. Trace the endpoint

1. Find the route in `API/Routing/RouteRegistry.cs` → handler class. Note the `{param}` names in the template.
2. Check auth for the route in `deployment_resources.json` (`api_gateway` → `resources` → path → method → `authorization_type`). `cognito_authorizer` means claims (`sub`, `cognito:groups`) reach the handler; `NONE` means they do not — guest routes then rely on `IGuestAuthorizationService.ValidateAsync`.
3. Read the handler fully: which path/query/body parameters it reads, which claims/roles it checks, which service methods it calls, and **which exceptions it catches and how it maps them** (status code + body helper).
4. Read the service methods it calls: every branch, every thrown exception, every repository call and side effect (save/delete/status change).
5. Read the contract in `docs/ApiRoutes.md` (and `homework-backend.md` in the repo root if relevant). Where code and docs disagree, test the code's behaviour and report the discrepancy.
6. Read the existing test file for that service in `Tests/Services/` — you will extend it rather than create a parallel one.

Before writing, build a short test matrix (scenario → expected status/exception) and make sure every branch from steps 3–4 is in it.

## Step 1. Handler tests — `Tests/Handlers/<Domain>/<HandlerName>Tests.cs`

Namespace `APIHandler.Tests.Handlers.<Domain>`. Mock the service(s), call `HandleRequest` directly. Path parameters are set directly on the request (in production `Function.cs` fills them from the route match). Verified template:

```csharp
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using APIHandler.Handlers.GuestSessions;
using APIHandler.Models.GuestSessions;
using APIHandler.Services.GuestSessions;
using AutoFixture.Xunit2;
using Moq;
using System.Text.Json;

namespace APIHandler.Tests.Handlers.GuestSessions;

public class DeleteGuestSessionHandlerTests
{
    private readonly Mock<IGuestSessionService> _serviceMock = new();
    private readonly Mock<ILambdaContext> _contextMock = new();
    private readonly DeleteGuestSessionHandler _handler;

    public DeleteGuestSessionHandlerTests()
    {
        _contextMock.SetupGet(c => c.Logger).Returns(new Mock<ILambdaLogger>().Object);
        _handler = new DeleteGuestSessionHandler(_serviceMock.Object);
    }

    private static APIGatewayProxyRequest BuildRequest(
        Dictionary<string, string>? pathParameters, string? userId, string? groups = null, string? body = null) => new()
    {
        PathParameters = pathParameters,
        Body = body,
        RequestContext = new APIGatewayProxyRequest.ProxyRequestContext
        {
            Authorizer = userId is null ? null : new APIGatewayCustomAuthorizerContext
            {
                Claims = groups is null
                    ? new Dictionary<string, string> { ["sub"] = userId }
                    : new Dictionary<string, string> { ["sub"] = userId, ["cognito:groups"] = groups }
            }
        }
    };

    [Theory, AutoData]
    public async Task HandleRequest_ValidRequest_Returns200WithSnapshot(
        string groupId, string sessionId, string userId, GetAllSessionsResponse snapshot)
    {
        _serviceMock.Setup(s => s.DeleteGuestSessionAsync(userId, groupId, sessionId)).ReturnsAsync(snapshot);
        var request = BuildRequest(new() { ["groupReservationId"] = groupId, ["guestSessionId"] = sessionId }, userId);

        var response = await _handler.HandleRequest(request, _contextMock.Object);

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal(snapshot.Total, json.RootElement.GetProperty("Total").GetDecimal());
    }

    [Theory, AutoData]
    public async Task HandleRequest_NoClaims_Returns401(string groupId, string sessionId)
    {
        var request = BuildRequest(new() { ["groupReservationId"] = groupId, ["guestSessionId"] = sessionId }, userId: null);

        var response = await _handler.HandleRequest(request, _contextMock.Object);

        Assert.Equal(401, response.StatusCode);
        _serviceMock.VerifyNoOtherCalls();
    }
}
```

Cover, as applicable to the handler:
- **Success** → status code and the key fields of the body; verify the service was called with the exact arguments (`userId` from `sub`, path params, parsed body).
- **Missing path parameter** (one test per parameter; pass a dictionary without it) → 400, service not called.
- **Body:** empty body → 400 `"Body is empty"`; invalid JSON → 400 `"Invalid json body"`; each `[Required]`/DataAnnotations violation → 400 (validated by `RequestBodyHelper.TryParseAndValidate`).
- **Auth:** no claims → 401 when the handler checks `sub`; wrong role (`cognito:groups` = `"Customer"` vs `"Waiter"`) → 403 when it checks roles. For guest routes, mock `IGuestAuthorizationService.ValidateAsync` to return an error response and assert it is returned as-is and the service is not called.
- **Exception mapping:** one test per `catch` in the handler — `XxxNotFoundException` → 404, `ActionForbiddenException` → 403, `ValidationException` subclasses → 400 with the matching `errorCode` (see `ResponseBuilder.CreateErrorCodeResponse`). Use `.ThrowsAsync(new XxxException("msg"))`.

Body serialization facts (asserting on the wrong casing is the most common mistake here):
- `ResponseBuilder.CreateSuccessResponse(obj)` uses default `System.Text.Json` options → **PascalCase** property names, unless the model declares `[JsonPropertyName("...")]` (about two thirds of the models do — check the model).
- `CreateErrorResponse` → `{"error": "<message>"}`.
- `CreateErrorCodeResponse` / `CreateJsonResponse` → camelCase: `{"message": "...", "errorCode": "GuestLimitExceeded"}`.
- Parse with `JsonDocument` and assert on specific properties; do not compare whole JSON strings.

## Step 2. Service tests — `Tests/Services/<Service>Tests.cs`

Extend the existing class (it already constructs the service with all mocked dependencies in its constructor — reuse those fields). If no file exists, create one following the same pattern as `Tests/Services/GroupReservationServiceTests.cs`.

Cover for each service method the endpoint uses:
- **Happy path:** the returned value (key fields, totals, aggregates) and side effects — `Verify(r => r.SaveXxxAsync(It.Is<T>(x => x.Status == ...)), Times.Once)`.
- **Every throw in the method** → `await Assert.ThrowsAsync<XxxException>(() => _service.MethodAsync(...))`, plus `Verify(..., Times.Never)` on writes that must not happen.
- **Ownership / permission checks** (e.g. `ValidateReservationOwnerAsync`): another user's id → `ActionForbiddenException`.
- **Boundaries:** empty collections, a single item, duplicates that must be merged (e.g. same `dishId`), state/status values via `[InlineAutoData(Status.X)]`.

Style (match existing tests):
- `[Theory, AutoData]` with fixture-generated parameters; `[InlineAutoData(...)]` for enum/status variants; `[Fact]` only when there are no inputs.
- Naming: `MethodName_Condition_ExpectedResult` (e.g. `DeleteGuestSessionAsync_SessionFromAnotherGroup_ThrowsValidationException`).
- Arrange-Act-Assert, one behaviour per test; tie fixture objects together explicitly (`groupReservation.ReservationId = reservation.Id;`).
- No `Thread.Sleep`, no real time dependencies beyond what the service reads; no assertions on log output.

## Step 3. Routing test (only if the route is new or has non-trivial parameters)

`Tests/Routing/RouteHandlerTests.cs` — verify the template matches the real path and extracts parameters, and does not match a sibling route:
```csharp
var route = new RouteHandler(new Route(HttpMethods.DELETE, "/guest/groups/{groupReservationId}/sessions/{guestSessionId}"), Mock.Of<IApiRequestHandler>());
var parameters = route.Match("/guest/groups/g1/sessions/s1");
Assert.Equal("g1", parameters!["groupReservationId"]);
Assert.Null(route.Match("/guest/groups/g1/sessions"));
```
Do not instantiate `RouteRegistry` — it creates real AWS clients and needs environment variables.

## Step 4. Run and iterate (mandatory)

```bash
cd backend/restaurant-backend-app
dotnet test Tests/APIHandler.Tests.csproj
dotnet test Tests/APIHandler.Tests.csproj --filter "FullyQualifiedName~<HandlerOrServiceName>"   # focused run while iterating
```
Baseline as of 2026-10-28: 141 tests passing, 0 failing. Every new test must pass (except deliberately skipped bug-documenting tests), and the previously passing tests must still pass. Do not report until you have seen the final green run.

## Report

Write the report in Russian (the user's language), briefly:
1. Endpoint(s) covered: method, path, handler, service methods.
2. Test matrix: scenario → expected result, grouped by handler / service / routing.
3. Files created or changed.
4. Results: exact numbers before → after (`Passed`, `Failed`, `Skipped`).
5. Findings: bugs found (with file:line and the skipped test that documents each), code/doc discrepancies, branches that could not be unit-tested and why.
