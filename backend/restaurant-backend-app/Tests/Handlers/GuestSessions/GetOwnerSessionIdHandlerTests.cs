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

public class GetOwnerSessionIdHandlerTests
{
    private readonly Mock<IGuestSessionService> _serviceMock = new();
    private readonly Mock<ILambdaContext> _contextMock = new();
    private readonly GetOwnerSessionIdHandler _handler;

    public GetOwnerSessionIdHandlerTests()
    {
        _contextMock.SetupGet(c => c.Logger).Returns(new Mock<ILambdaLogger>().Object);
        _handler = new GetOwnerSessionIdHandler(_serviceMock.Object);
    }

    private static APIGatewayProxyRequest BuildRequest(
        Dictionary<string, string>? pathParameters, Dictionary<string, string>? claims) => new()
    {
        PathParameters = pathParameters,
        RequestContext = new APIGatewayProxyRequest.ProxyRequestContext
        {
            Authorizer = claims is null ? null : new APIGatewayCustomAuthorizerContext { Claims = claims }
        }
    };

    private static APIGatewayProxyRequest BuildValidRequest(string reservationId, string userId) =>
        BuildRequest(new() { ["reservationId"] = reservationId }, new() { ["sub"] = userId });

    private static string ErrorOf(APIGatewayProxyResponse response)
    {
        using var json = JsonDocument.Parse(response.Body);
        return json.RootElement.GetProperty("error").GetString()!;
    }

    [Theory, AutoData]
    public async Task HandleRequest_ValidRequest_Returns200WithCamelCaseGuestSessionId(
        string reservationId, string userId, string sessionId)
    {
        _serviceMock.Setup(s => s.GetOrCreateOwnerSessionIdAsync(userId, reservationId))
            .ReturnsAsync(new GetOwnerSessionIdResponse(sessionId));

        var response = await _handler.HandleRequest(BuildValidRequest(reservationId, userId), _contextMock.Object);

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal(sessionId, json.RootElement.GetProperty("guestSessionId").GetString());
        Assert.False(json.RootElement.TryGetProperty("GuestSessionId", out _));
        _serviceMock.Verify(s => s.GetOrCreateOwnerSessionIdAsync(userId, reservationId), Times.Once);
        _serviceMock.VerifyNoOtherCalls();
    }

    [Theory, AutoData]
    public async Task HandleRequest_MissingReservationId_Returns400(string userId)
    {
        var request = BuildRequest(new Dictionary<string, string>(), new() { ["sub"] = userId });

        var response = await _handler.HandleRequest(request, _contextMock.Object);

        Assert.Equal(400, response.StatusCode);
        Assert.Contains("reservationId", ErrorOf(response));
        _serviceMock.VerifyNoOtherCalls();
    }

    [Theory, AutoData]
    public async Task HandleRequest_NullPathParameters_Returns400(string userId)
    {
        var response = await _handler.HandleRequest(BuildRequest(null, new() { ["sub"] = userId }), _contextMock.Object);

        Assert.Equal(400, response.StatusCode);
        _serviceMock.VerifyNoOtherCalls();
    }

    [Theory, AutoData]
    public async Task HandleRequest_EmptyReservationId_Returns400(string userId)
    {
        var request = BuildRequest(new() { ["reservationId"] = "" }, new() { ["sub"] = userId });

        var response = await _handler.HandleRequest(request, _contextMock.Object);

        Assert.Equal(400, response.StatusCode);
        _serviceMock.VerifyNoOtherCalls();
    }

    [Theory, AutoData]
    public async Task HandleRequest_NoAuthorizer_Returns401(string reservationId)
    {
        var request = BuildRequest(new() { ["reservationId"] = reservationId }, claims: null);

        var response = await _handler.HandleRequest(request, _contextMock.Object);

        Assert.Equal(401, response.StatusCode);
        Assert.Equal("Unauthorized.", ErrorOf(response));
        _serviceMock.VerifyNoOtherCalls();
    }

    [Theory, AutoData]
    public async Task HandleRequest_ClaimsWithoutSub_Returns401(string reservationId)
    {
        var request = BuildRequest(
            new() { ["reservationId"] = reservationId },
            new() { ["cognito:groups"] = "Customer" });

        var response = await _handler.HandleRequest(request, _contextMock.Object);

        Assert.Equal(401, response.StatusCode);
        _serviceMock.VerifyNoOtherCalls();
    }

    [Theory, AutoData]
    public async Task HandleRequest_NotOwner_Returns403(string reservationId, string userId, string message)
    {
        _serviceMock.Setup(s => s.GetOrCreateOwnerSessionIdAsync(userId, reservationId))
            .ThrowsAsync(new ActionForbiddenException(message));

        var response = await _handler.HandleRequest(BuildValidRequest(reservationId, userId), _contextMock.Object);

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(message, ErrorOf(response));
    }

    [Theory, AutoData]
    public async Task HandleRequest_ReservationNotFound_Returns404(string reservationId, string userId)
    {
        _serviceMock.Setup(s => s.GetOrCreateOwnerSessionIdAsync(userId, reservationId))
            .ThrowsAsync(new ReservationNotFoundException("Reservation not found."));

        var response = await _handler.HandleRequest(BuildValidRequest(reservationId, userId), _contextMock.Object);

        Assert.Equal(404, response.StatusCode);
        Assert.Equal("Reservation not found.", ErrorOf(response));
    }

    [Theory, AutoData]
    public async Task HandleRequest_GroupReservationNotFound_Returns404(string reservationId, string userId)
    {
        _serviceMock.Setup(s => s.GetOrCreateOwnerSessionIdAsync(userId, reservationId))
            .ThrowsAsync(new GroupReservationNotFoundException());

        var response = await _handler.HandleRequest(BuildValidRequest(reservationId, userId), _contextMock.Object);

        Assert.Equal(404, response.StatusCode);
        Assert.Equal("Group reservation was not found", ErrorOf(response));
    }
}
