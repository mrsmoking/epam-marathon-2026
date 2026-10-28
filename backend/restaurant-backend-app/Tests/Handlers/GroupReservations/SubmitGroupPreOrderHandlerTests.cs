using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using APIHandler.Handlers.GroupReservations;
using APIHandler.Models.GroupReservation;
using APIHandler.Services.GroupReservations;
using AutoFixture.Xunit2;
using Moq;
using System.Text.Json;

namespace APIHandler.Tests.Handlers.GroupReservations;

public class SubmitGroupPreOrderHandlerTests
{
    private readonly Mock<IGroupReservationService> _serviceMock = new();
    private readonly Mock<ILambdaContext> _contextMock = new();
    private readonly SubmitGroupPreOrderHandler _handler;

    public SubmitGroupPreOrderHandlerTests()
    {
        _contextMock.SetupGet(c => c.Logger).Returns(new Mock<ILambdaLogger>().Object);
        _handler = new SubmitGroupPreOrderHandler(_serviceMock.Object);
    }

    private static APIGatewayProxyRequest BuildRequest(
        Dictionary<string, string>? pathParameters, Dictionary<string, string>? claims, string? body) => new()
    {
        PathParameters = pathParameters,
        Body = body,
        RequestContext = new APIGatewayProxyRequest.ProxyRequestContext
        {
            Authorizer = claims is null ? null : new APIGatewayCustomAuthorizerContext { Claims = claims }
        }
    };

    private static string BodyFor(string reservationId) => JsonSerializer.Serialize(new { reservationId });

    private static APIGatewayProxyRequest BuildValidRequest(string groupId, string userId, string reservationId) =>
        BuildRequest(new() { ["groupReservationId"] = groupId }, new() { ["sub"] = userId }, BodyFor(reservationId));

    private static string ErrorOf(APIGatewayProxyResponse response)
    {
        using var json = JsonDocument.Parse(response.Body);
        return json.RootElement.GetProperty("error").GetString()!;
    }

    private void SetupThrows(string userId, string groupId, string reservationId, Exception exception) =>
        _serviceMock.Setup(s => s.SubmitGroupPreOrderAsync(userId, groupId, reservationId)).ThrowsAsync(exception);

    [Theory, AutoData]
    public async Task HandleRequest_ValidRequest_Returns200WithCamelCaseItems(string groupId, string userId, string reservationId)
    {
        _serviceMock.Setup(s => s.SubmitGroupPreOrderAsync(userId, groupId, reservationId))
            .ReturnsAsync([new GroupPreOrderItemResponse("dish1", 3), new GroupPreOrderItemResponse("dish2", 2)]);

        var response = await _handler.HandleRequest(BuildValidRequest(groupId, userId, reservationId), _contextMock.Object);

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        var root = json.RootElement;
        Assert.Equal(JsonValueKind.Array, root.ValueKind);
        Assert.Equal(2, root.GetArrayLength());
        Assert.Equal("dish1", root[0].GetProperty("dishId").GetString());
        Assert.Equal(3, root[0].GetProperty("quantity").GetInt32());
        Assert.Equal("dish2", root[1].GetProperty("dishId").GetString());
        Assert.Equal(2, root[1].GetProperty("quantity").GetInt32());
        Assert.False(root[0].TryGetProperty("DishId", out _));
        Assert.False(root[0].TryGetProperty("Quantity", out _));

        _serviceMock.Verify(s => s.SubmitGroupPreOrderAsync(userId, groupId, reservationId), Times.Once);
        _serviceMock.VerifyNoOtherCalls();
    }

    [Theory, AutoData]
    public async Task HandleRequest_PascalCaseBodyProperty_IsAccepted(string groupId, string userId, string reservationId)
    {
        _serviceMock.Setup(s => s.SubmitGroupPreOrderAsync(userId, groupId, reservationId)).ReturnsAsync([]);
        var request = BuildRequest(
            new() { ["groupReservationId"] = groupId },
            new() { ["sub"] = userId },
            JsonSerializer.Serialize(new { ReservationId = reservationId }));

        var response = await _handler.HandleRequest(request, _contextMock.Object);

        Assert.Equal(200, response.StatusCode);
        _serviceMock.Verify(s => s.SubmitGroupPreOrderAsync(userId, groupId, reservationId), Times.Once);
    }

    [Theory, AutoData]
    public async Task HandleRequest_MissingGroupReservationId_Returns400(string userId, string reservationId)
    {
        var request = BuildRequest(new Dictionary<string, string>(), new() { ["sub"] = userId }, BodyFor(reservationId));

        var response = await _handler.HandleRequest(request, _contextMock.Object);

        Assert.Equal(400, response.StatusCode);
        Assert.Contains("groupReservationId", ErrorOf(response));
        _serviceMock.VerifyNoOtherCalls();
    }

    [Theory, AutoData]
    public async Task HandleRequest_NullPathParameters_Returns400(string userId, string reservationId)
    {
        var request = BuildRequest(null, new() { ["sub"] = userId }, BodyFor(reservationId));

        var response = await _handler.HandleRequest(request, _contextMock.Object);

        Assert.Equal(400, response.StatusCode);
        _serviceMock.VerifyNoOtherCalls();
    }

    [Theory, AutoData]
    public async Task HandleRequest_NoAuthorizer_Returns401(string groupId, string reservationId)
    {
        var request = BuildRequest(new() { ["groupReservationId"] = groupId }, claims: null, BodyFor(reservationId));

        var response = await _handler.HandleRequest(request, _contextMock.Object);

        Assert.Equal(401, response.StatusCode);
        Assert.Equal("Unauthorized.", ErrorOf(response));
        _serviceMock.VerifyNoOtherCalls();
    }

    [Theory, AutoData]
    public async Task HandleRequest_ClaimsWithoutSub_Returns401(string groupId, string reservationId)
    {
        var request = BuildRequest(
            new() { ["groupReservationId"] = groupId },
            new() { ["cognito:groups"] = "Customer" },
            BodyFor(reservationId));

        var response = await _handler.HandleRequest(request, _contextMock.Object);

        Assert.Equal(401, response.StatusCode);
        _serviceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineAutoData(null)]
    [InlineAutoData("")]
    public async Task HandleRequest_EmptyBody_Returns400(string? body, string groupId, string userId)
    {
        var request = BuildRequest(new() { ["groupReservationId"] = groupId }, new() { ["sub"] = userId }, body);

        var response = await _handler.HandleRequest(request, _contextMock.Object);

        Assert.Equal(400, response.StatusCode);
        Assert.Equal("Body is empty", ErrorOf(response));
        _serviceMock.VerifyNoOtherCalls();
    }

    [Theory, AutoData]
    public async Task HandleRequest_JsonNullBody_Returns400(string groupId, string userId)
    {
        var request = BuildRequest(new() { ["groupReservationId"] = groupId }, new() { ["sub"] = userId }, "null");

        var response = await _handler.HandleRequest(request, _contextMock.Object);

        Assert.Equal(400, response.StatusCode);
        Assert.Equal("Body is empty", ErrorOf(response));
        _serviceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineAutoData("{not json")]
    [InlineAutoData("[]")]
    [InlineAutoData("{\"reservationId\": 123}")]
    public async Task HandleRequest_InvalidJsonBody_Returns400(string body, string groupId, string userId)
    {
        var request = BuildRequest(new() { ["groupReservationId"] = groupId }, new() { ["sub"] = userId }, body);

        var response = await _handler.HandleRequest(request, _contextMock.Object);

        Assert.Equal(400, response.StatusCode);
        Assert.Equal("Invalid json body", ErrorOf(response));
        _serviceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineAutoData("{}")]
    [InlineAutoData("{\"reservationId\": null}")]
    [InlineAutoData("{\"reservationId\": \"\"}")]
    public async Task HandleRequest_MissingReservationIdInBody_Returns400WithValidationErrors(string body, string groupId, string userId)
    {
        var request = BuildRequest(new() { ["groupReservationId"] = groupId }, new() { ["sub"] = userId }, body);

        var response = await _handler.HandleRequest(request, _contextMock.Object);

        Assert.Equal(400, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        Assert.True(json.RootElement.TryGetProperty("title", out _));
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty("ReservationId", out _));
        _serviceMock.VerifyNoOtherCalls();
    }

    [Theory, AutoData]
    public async Task HandleRequest_NotOwner_Returns403(string groupId, string userId, string reservationId, string message)
    {
        SetupThrows(userId, groupId, reservationId, new ActionForbiddenException(message));

        var response = await _handler.HandleRequest(BuildValidRequest(groupId, userId, reservationId), _contextMock.Object);

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(message, ErrorOf(response));
    }

    [Theory, AutoData]
    public async Task HandleRequest_GroupReservationNotFound_Returns404(string groupId, string userId, string reservationId)
    {
        SetupThrows(userId, groupId, reservationId, new GroupReservationNotFoundException());

        var response = await _handler.HandleRequest(BuildValidRequest(groupId, userId, reservationId), _contextMock.Object);

        Assert.Equal(404, response.StatusCode);
        Assert.Equal("Group reservation was not found", ErrorOf(response));
    }

    // ReservationNotFoundException derives from ValidationException; it must still map to 404, not to the 400 errorCode body.
    [Theory, AutoData]
    public async Task HandleRequest_ReservationNotFound_Returns404NotErrorCodeResponse(string groupId, string userId, string reservationId)
    {
        SetupThrows(userId, groupId, reservationId, new ReservationNotFoundException("Parent reservation not found."));

        var response = await _handler.HandleRequest(BuildValidRequest(groupId, userId, reservationId), _contextMock.Object);

        Assert.Equal(404, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        Assert.False(string.IsNullOrEmpty(json.RootElement.GetProperty("error").GetString()));
        Assert.False(json.RootElement.TryGetProperty("errorCode", out _));
    }

    [Theory, AutoData]
    public async Task HandleRequest_GroupReservationSubmitted_Returns400WithStringErrorCode(string groupId, string userId, string reservationId)
    {
        SetupThrows(userId, groupId, reservationId, new GroupReservationSubmittedException());

        var response = await _handler.HandleRequest(BuildValidRequest(groupId, userId, reservationId), _contextMock.Object);

        Assert.Equal(400, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        var root = json.RootElement;
        Assert.Equal("Group reservation was submitted.", root.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.String, root.GetProperty("errorCode").ValueKind);
        Assert.Equal("GroupReservationSubmitted", root.GetProperty("errorCode").GetString());
        Assert.False(root.TryGetProperty("Message", out _));
        Assert.False(root.TryGetProperty("ErrorCode", out _));
    }

    [Theory, AutoData]
    public async Task HandleRequest_GroupReservationExpired_Returns400WithGroupReservationExpiredCode(string groupId, string userId, string reservationId)
    {
        SetupThrows(userId, groupId, reservationId, new GroupReservationExpiredException());

        var response = await _handler.HandleRequest(BuildValidRequest(groupId, userId, reservationId), _contextMock.Object);

        Assert.Equal(400, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal("Group reservation is expired.", json.RootElement.GetProperty("message").GetString());
        Assert.Equal("GroupReservationExpired", json.RootElement.GetProperty("errorCode").GetString());
    }

    [Theory]
    [InlineAutoData("Reservation ID does not match the group reservation.")]
    [InlineAutoData("There are no submitted guest sessions in the group.")]
    [InlineAutoData("Submitted guest sessions contain no dishes.")]
    [InlineAutoData("Active pre-order for reservation 'r1' not found.")]
    public async Task HandleRequest_GenericValidationException_Returns400WithUnknownCode(
        string message, string groupId, string userId, string reservationId)
    {
        SetupThrows(userId, groupId, reservationId, new ValidationException(message));

        var response = await _handler.HandleRequest(BuildValidRequest(groupId, userId, reservationId), _contextMock.Object);

        Assert.Equal(400, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal(message, json.RootElement.GetProperty("message").GetString());
        Assert.Equal("Unknown", json.RootElement.GetProperty("errorCode").GetString());
    }
}
