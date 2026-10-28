using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using APIHandler.Handlers.GuestSessions;
using APIHandler.Models.Dishes;
using APIHandler.Models.GuestSessions;
using APIHandler.Models.Orders;
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
        Dictionary<string, string>? pathParameters, Dictionary<string, string>? claims) => new()
    {
        PathParameters = pathParameters,
        RequestContext = new APIGatewayProxyRequest.ProxyRequestContext
        {
            Authorizer = claims is null ? null : new APIGatewayCustomAuthorizerContext { Claims = claims }
        }
    };

    private static APIGatewayProxyRequest BuildValidRequest(string groupId, string sessionId, string userId) =>
        BuildRequest(
            new() { ["groupReservationId"] = groupId, ["guestSessionId"] = sessionId },
            new() { ["sub"] = userId });

    private static DishResponse BuildDish(string id, string name, decimal price) => new()
    {
        Id = id,
        Name = name,
        Description = "desc",
        DishType = "Main Course",
        Price = price,
        Weight = "300 g",
        Calories = "500 kcal",
        Proteins = "20 g",
        Fats = "10 g",
        Carbohydrates = "50 g",
        Vitamins = "A, C",
        ImageUrl = null,
        State = "Available",
    };

    private static string ErrorOf(APIGatewayProxyResponse response)
    {
        using var json = JsonDocument.Parse(response.Body);
        Assert.False(json.RootElement.TryGetProperty("errorCode", out _));
        return json.RootElement.GetProperty("error").GetString()!;
    }

    private static (string Message, string ErrorCode) ErrorCodeOf(APIGatewayProxyResponse response)
    {
        using var json = JsonDocument.Parse(response.Body);
        var root = json.RootElement;
        Assert.False(root.TryGetProperty("error", out _));
        Assert.False(root.TryGetProperty("Message", out _));
        var errorCode = root.GetProperty("errorCode");
        Assert.Equal(JsonValueKind.String, errorCode.ValueKind);
        return (root.GetProperty("message").GetString()!, errorCode.GetString()!);
    }

    [Theory, AutoData]
    public async Task HandleRequest_ValidRequest_Returns200WithCamelCaseSnapshot(string groupId, string sessionId, string userId)
    {
        var snapshot = new GetAllSessionsResponse(
        [
            new Participant("owner-id", ParticipantType.Owner, "Owner", [], 0m, false),
            new Participant("guest-id", ParticipantType.Guest, "Anna",
                [new OrderItemResponse { Dish = BuildDish("d1", "Steak", 30m), Quantity = 2 }], 60m, true),
        ], 60m);
        _serviceMock.Setup(s => s.DeleteGuestSessionAsync(userId, groupId, sessionId)).ReturnsAsync(snapshot);

        var response = await _handler.HandleRequest(BuildValidRequest(groupId, sessionId, userId), _contextMock.Object);

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        var root = json.RootElement;
        Assert.Equal(60m, root.GetProperty("total").GetDecimal());

        var participants = root.GetProperty("participants");
        Assert.Equal(2, participants.GetArrayLength());

        var owner = participants[0];
        Assert.Equal("owner-id", owner.GetProperty("id").GetString());
        Assert.Equal("Owner", owner.GetProperty("type").GetString());
        Assert.Equal("Owner", owner.GetProperty("name").GetString());
        Assert.Equal(0, owner.GetProperty("items").GetArrayLength());
        Assert.Equal(0m, owner.GetProperty("total").GetDecimal());
        Assert.False(owner.GetProperty("isSubmitted").GetBoolean());

        var guest = participants[1];
        Assert.Equal("guest-id", guest.GetProperty("id").GetString());
        Assert.Equal("Guest", guest.GetProperty("type").GetString());
        Assert.Equal("Anna", guest.GetProperty("name").GetString());
        Assert.Equal(60m, guest.GetProperty("total").GetDecimal());
        Assert.True(guest.GetProperty("isSubmitted").GetBoolean());

        var item = guest.GetProperty("items")[0];
        Assert.Equal(2, item.GetProperty("quantity").GetInt32());
        Assert.Equal("Steak", item.GetProperty("dish").GetProperty("name").GetString());
        Assert.Equal(30m, item.GetProperty("dish").GetProperty("price").GetDecimal());

        Assert.False(root.TryGetProperty("Participants", out _));
        Assert.False(root.TryGetProperty("Total", out _));
        Assert.False(guest.TryGetProperty("IsSubmitted", out _));

        _serviceMock.Verify(s => s.DeleteGuestSessionAsync(userId, groupId, sessionId), Times.Once);
        _serviceMock.VerifyNoOtherCalls();
    }

    [Theory, AutoData]
    public async Task HandleRequest_LastGuestDeleted_Returns200WithEmptyParticipants(string groupId, string sessionId, string userId)
    {
        _serviceMock.Setup(s => s.DeleteGuestSessionAsync(userId, groupId, sessionId))
            .ReturnsAsync(new GetAllSessionsResponse([], 0m));

        var response = await _handler.HandleRequest(BuildValidRequest(groupId, sessionId, userId), _contextMock.Object);

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal(0, json.RootElement.GetProperty("participants").GetArrayLength());
        Assert.Equal(0m, json.RootElement.GetProperty("total").GetDecimal());
    }

    [Theory, AutoData]
    public async Task HandleRequest_MissingGroupReservationId_Returns400(string sessionId, string userId)
    {
        var request = BuildRequest(new() { ["guestSessionId"] = sessionId }, new() { ["sub"] = userId });

        var response = await _handler.HandleRequest(request, _contextMock.Object);

        Assert.Equal(400, response.StatusCode);
        Assert.Contains("groupReservationId", ErrorOf(response));
        _serviceMock.VerifyNoOtherCalls();
    }

    [Theory, AutoData]
    public async Task HandleRequest_MissingGuestSessionId_Returns400(string groupId, string userId)
    {
        var request = BuildRequest(new() { ["groupReservationId"] = groupId }, new() { ["sub"] = userId });

        var response = await _handler.HandleRequest(request, _contextMock.Object);

        Assert.Equal(400, response.StatusCode);
        Assert.Contains("guestSessionId", ErrorOf(response));
        _serviceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineAutoData("", "s1")]
    [InlineAutoData("g1", "")]
    public async Task HandleRequest_EmptyPathParameter_Returns400(string groupId, string sessionId, string userId)
    {
        var request = BuildRequest(
            new() { ["groupReservationId"] = groupId, ["guestSessionId"] = sessionId },
            new() { ["sub"] = userId });

        var response = await _handler.HandleRequest(request, _contextMock.Object);

        Assert.Equal(400, response.StatusCode);
        _serviceMock.VerifyNoOtherCalls();
    }

    [Theory, AutoData]
    public async Task HandleRequest_NullPathParameters_Returns400(string userId)
    {
        var request = BuildRequest(null, new() { ["sub"] = userId });

        var response = await _handler.HandleRequest(request, _contextMock.Object);

        Assert.Equal(400, response.StatusCode);
        _serviceMock.VerifyNoOtherCalls();
    }

    [Theory, AutoData]
    public async Task HandleRequest_NoAuthorizer_Returns401(string groupId, string sessionId)
    {
        var request = BuildRequest(
            new() { ["groupReservationId"] = groupId, ["guestSessionId"] = sessionId },
            claims: null);

        var response = await _handler.HandleRequest(request, _contextMock.Object);

        Assert.Equal(401, response.StatusCode);
        Assert.Equal("Unauthorized.", ErrorOf(response));
        _serviceMock.VerifyNoOtherCalls();
    }

    [Theory, AutoData]
    public async Task HandleRequest_ClaimsWithoutSub_Returns401(string groupId, string sessionId)
    {
        var request = BuildRequest(
            new() { ["groupReservationId"] = groupId, ["guestSessionId"] = sessionId },
            new() { ["cognito:groups"] = "Customer" });

        var response = await _handler.HandleRequest(request, _contextMock.Object);

        Assert.Equal(401, response.StatusCode);
        _serviceMock.VerifyNoOtherCalls();
    }

    [Theory, AutoData]
    public async Task HandleRequest_NotOwner_Returns403(string groupId, string sessionId, string userId, string message)
    {
        _serviceMock.Setup(s => s.DeleteGuestSessionAsync(userId, groupId, sessionId))
            .ThrowsAsync(new ActionForbiddenException(message));

        var response = await _handler.HandleRequest(BuildValidRequest(groupId, sessionId, userId), _contextMock.Object);

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(message, ErrorOf(response));
    }

    [Theory, AutoData]
    public async Task HandleRequest_GroupReservationNotFound_Returns404(string groupId, string sessionId, string userId)
    {
        _serviceMock.Setup(s => s.DeleteGuestSessionAsync(userId, groupId, sessionId))
            .ThrowsAsync(new GroupReservationNotFoundException());

        var response = await _handler.HandleRequest(BuildValidRequest(groupId, sessionId, userId), _contextMock.Object);

        Assert.Equal(404, response.StatusCode);
        Assert.Equal("Group reservation was not found", ErrorOf(response));
    }

    [Theory, AutoData]
    public async Task HandleRequest_ReservationNotFound_Returns404(string groupId, string sessionId, string userId)
    {
        _serviceMock.Setup(s => s.DeleteGuestSessionAsync(userId, groupId, sessionId))
            .ThrowsAsync(new ReservationNotFoundException("Parent reservation not found."));

        var response = await _handler.HandleRequest(BuildValidRequest(groupId, sessionId, userId), _contextMock.Object);

        Assert.Equal(404, response.StatusCode);
        Assert.False(string.IsNullOrEmpty(ErrorOf(response)));
    }

    [Theory, AutoData]
    public async Task HandleRequest_GuestSessionNotFound_Returns404(string groupId, string sessionId, string userId, string message)
    {
        _serviceMock.Setup(s => s.DeleteGuestSessionAsync(userId, groupId, sessionId))
            .ThrowsAsync(new GuestSessionNotFoundException(message));

        var response = await _handler.HandleRequest(BuildValidRequest(groupId, sessionId, userId), _contextMock.Object);

        Assert.Equal(404, response.StatusCode);
        Assert.Equal(message, ErrorOf(response));
    }

    [Theory, AutoData]
    public async Task HandleRequest_GroupSubmitted_Returns400WithGroupReservationSubmittedCode(string groupId, string sessionId, string userId)
    {
        _serviceMock.Setup(s => s.DeleteGuestSessionAsync(userId, groupId, sessionId))
            .ThrowsAsync(new GroupReservationSubmittedException());

        var response = await _handler.HandleRequest(BuildValidRequest(groupId, sessionId, userId), _contextMock.Object);

        Assert.Equal(400, response.StatusCode);
        var (message, errorCode) = ErrorCodeOf(response);
        Assert.Equal("GroupReservationSubmitted", errorCode);
        Assert.Equal("Group reservation was submitted.", message);
    }

    [Theory, AutoData]
    public async Task HandleRequest_GroupClosed_Returns400WithGroupReservationExpiredCode(string groupId, string sessionId, string userId)
    {
        _serviceMock.Setup(s => s.DeleteGuestSessionAsync(userId, groupId, sessionId))
            .ThrowsAsync(new GroupReservationExpiredException());

        var response = await _handler.HandleRequest(BuildValidRequest(groupId, sessionId, userId), _contextMock.Object);

        Assert.Equal(400, response.StatusCode);
        var (message, errorCode) = ErrorCodeOf(response);
        Assert.Equal("GroupReservationExpired", errorCode);
        Assert.Equal("Group reservation is expired.", message);
    }

    [Theory]
    [InlineAutoData("Guest session does not belong to this group reservation.")]
    [InlineAutoData("The owner's session cannot be deleted.")]
    public async Task HandleRequest_ValidationException_Returns400WithUnknownCode(
        string serviceMessage, string groupId, string sessionId, string userId)
    {
        _serviceMock.Setup(s => s.DeleteGuestSessionAsync(userId, groupId, sessionId))
            .ThrowsAsync(new ValidationException(serviceMessage));

        var response = await _handler.HandleRequest(BuildValidRequest(groupId, sessionId, userId), _contextMock.Object);

        Assert.Equal(400, response.StatusCode);
        var (message, errorCode) = ErrorCodeOf(response);
        Assert.Equal("Unknown", errorCode);
        Assert.Equal(serviceMessage, message);
    }

    [Theory, AutoData]
    public async Task HandleRequest_UnexpectedException_Propagates(string groupId, string sessionId, string userId)
    {
        _serviceMock.Setup(s => s.DeleteGuestSessionAsync(userId, groupId, sessionId))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _handler.HandleRequest(BuildValidRequest(groupId, sessionId, userId), _contextMock.Object));
    }
}
