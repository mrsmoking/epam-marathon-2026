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

public class GetGroupSessionsHandlerTests
{
    private readonly Mock<IGuestSessionService> _serviceMock = new();
    private readonly Mock<ILambdaContext> _contextMock = new();
    private readonly GetGroupSessionsHandler _handler;

    public GetGroupSessionsHandlerTests()
    {
        _contextMock.SetupGet(c => c.Logger).Returns(new Mock<ILambdaLogger>().Object);
        _handler = new GetGroupSessionsHandler(_serviceMock.Object);
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

    private static APIGatewayProxyRequest BuildValidRequest(string groupId, string userId) =>
        BuildRequest(new() { ["groupReservationId"] = groupId }, new() { ["sub"] = userId });

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
        return json.RootElement.GetProperty("error").GetString()!;
    }

    [Theory, AutoData]
    public async Task HandleRequest_ValidRequest_Returns200WithCamelCaseSnapshot(string groupId, string userId)
    {
        var snapshot = new GetAllSessionsResponse(
        [
            new Participant("owner-id", ParticipantType.Owner, "Owner", [], 0m, false),
            new Participant("guest-id", ParticipantType.Guest, "Anna",
                [new OrderItemResponse { Dish = BuildDish("d1", "Steak", 30m), Quantity = 2 }], 60m, true),
        ], 60m);
        _serviceMock.Setup(s => s.GetGroupSessionsAsync(userId, groupId)).ReturnsAsync(snapshot);

        var response = await _handler.HandleRequest(BuildValidRequest(groupId, userId), _contextMock.Object);

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        var root = json.RootElement;
        Assert.Equal(60m, root.GetProperty("total").GetDecimal());

        var participants = root.GetProperty("participants");
        Assert.Equal(2, participants.GetArrayLength());

        var owner = participants[0];
        Assert.Equal("owner-id", owner.GetProperty("id").GetString());
        Assert.Equal("Owner", owner.GetProperty("type").GetString());
        Assert.Equal(0, owner.GetProperty("items").GetArrayLength());
        Assert.False(owner.GetProperty("isSubmitted").GetBoolean());

        var guest = participants[1];
        Assert.Equal("guest-id", guest.GetProperty("id").GetString());
        Assert.Equal("Guest", guest.GetProperty("type").GetString());
        Assert.Equal("Anna", guest.GetProperty("name").GetString());
        Assert.Equal(60m, guest.GetProperty("total").GetDecimal());
        Assert.True(guest.GetProperty("isSubmitted").GetBoolean());

        var item = guest.GetProperty("items")[0];
        Assert.Equal(2, item.GetProperty("quantity").GetInt32());
        var dish = item.GetProperty("dish");
        Assert.Equal("Steak", dish.GetProperty("name").GetString());
        Assert.Equal(30m, dish.GetProperty("price").GetDecimal());
        Assert.Equal(JsonValueKind.Null, dish.GetProperty("imageUrl").ValueKind);

        Assert.False(root.TryGetProperty("Participants", out _));
        Assert.False(guest.TryGetProperty("IsSubmitted", out _));

        _serviceMock.Verify(s => s.GetGroupSessionsAsync(userId, groupId), Times.Once);
        _serviceMock.VerifyNoOtherCalls();
    }

    [Theory, AutoData]
    public async Task HandleRequest_EmptyGroup_Returns200WithEmptyParticipants(string groupId, string userId)
    {
        _serviceMock.Setup(s => s.GetGroupSessionsAsync(userId, groupId))
            .ReturnsAsync(new GetAllSessionsResponse([], 0m));

        var response = await _handler.HandleRequest(BuildValidRequest(groupId, userId), _contextMock.Object);

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal(0, json.RootElement.GetProperty("participants").GetArrayLength());
        Assert.Equal(0m, json.RootElement.GetProperty("total").GetDecimal());
    }

    [Theory, AutoData]
    public async Task HandleRequest_MissingGroupReservationId_Returns400(string userId)
    {
        var request = BuildRequest(new Dictionary<string, string>(), new() { ["sub"] = userId });

        var response = await _handler.HandleRequest(request, _contextMock.Object);

        Assert.Equal(400, response.StatusCode);
        Assert.Contains("groupReservationId", ErrorOf(response));
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
    public async Task HandleRequest_EmptyGroupReservationId_Returns400(string userId)
    {
        var request = BuildRequest(new() { ["groupReservationId"] = "" }, new() { ["sub"] = userId });

        var response = await _handler.HandleRequest(request, _contextMock.Object);

        Assert.Equal(400, response.StatusCode);
        _serviceMock.VerifyNoOtherCalls();
    }

    [Theory, AutoData]
    public async Task HandleRequest_NoAuthorizer_Returns401(string groupId)
    {
        var request = BuildRequest(new() { ["groupReservationId"] = groupId }, claims: null);

        var response = await _handler.HandleRequest(request, _contextMock.Object);

        Assert.Equal(401, response.StatusCode);
        _serviceMock.VerifyNoOtherCalls();
    }

    [Theory, AutoData]
    public async Task HandleRequest_ClaimsWithoutSub_Returns401(string groupId)
    {
        var request = BuildRequest(
            new() { ["groupReservationId"] = groupId },
            new() { ["cognito:groups"] = "Customer" });

        var response = await _handler.HandleRequest(request, _contextMock.Object);

        Assert.Equal(401, response.StatusCode);
        _serviceMock.VerifyNoOtherCalls();
    }

    [Theory, AutoData]
    public async Task HandleRequest_NotOwner_Returns403(string groupId, string userId, string message)
    {
        _serviceMock.Setup(s => s.GetGroupSessionsAsync(userId, groupId))
            .ThrowsAsync(new ActionForbiddenException(message));

        var response = await _handler.HandleRequest(BuildValidRequest(groupId, userId), _contextMock.Object);

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(message, ErrorOf(response));
    }

    [Theory, AutoData]
    public async Task HandleRequest_GroupReservationNotFound_Returns404(string groupId, string userId)
    {
        _serviceMock.Setup(s => s.GetGroupSessionsAsync(userId, groupId))
            .ThrowsAsync(new GroupReservationNotFoundException());

        var response = await _handler.HandleRequest(BuildValidRequest(groupId, userId), _contextMock.Object);

        Assert.Equal(404, response.StatusCode);
        Assert.Equal("Group reservation was not found", ErrorOf(response));
    }

    [Theory, AutoData]
    public async Task HandleRequest_ReservationNotFound_Returns404(string groupId, string userId)
    {
        _serviceMock.Setup(s => s.GetGroupSessionsAsync(userId, groupId))
            .ThrowsAsync(new ReservationNotFoundException("Parent reservation not found."));

        var response = await _handler.HandleRequest(BuildValidRequest(groupId, userId), _contextMock.Object);

        Assert.Equal(404, response.StatusCode);
        Assert.False(string.IsNullOrEmpty(ErrorOf(response)));
    }
}
