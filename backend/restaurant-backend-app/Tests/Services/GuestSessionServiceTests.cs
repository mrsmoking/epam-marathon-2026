using Amazon.CognitoIdentityProvider.Model;
using APIHandler.Constants.Dishes;
using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Models.GuestSessions;
using APIHandler.Repositories.Auth;
using APIHandler.Repositories.Dishes;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using APIHandler.Services.GuestSessions;
using AutoFixture.Xunit2;
using Moq;
using NPOI.POIFS.Storage;
using System.Security.Cryptography;
using System.Text;

namespace APIHandler.Tests.Services;

public class GuestSessionServiceTests
{
    private readonly Mock<IGuestSessionRepository> _mockGuestSessionRepository = new();
    private readonly Mock<IGroupReservationRepository> _mockGroupReservationRepository = new();
    private readonly Mock<IDishRepository> _mockDishRepository = new();
    private readonly Mock<IReservationRepository> _mockReservationRepository = new();
    private readonly Mock<IUserRepository> _mockUserRepository = new();
    private readonly Mock<ITableRepository> _mockTableRepository = new();
    private readonly GuestSessionService _guestSessionService;

    public GuestSessionServiceTests()
    {
        _guestSessionService = new GuestSessionService(
            _mockGroupReservationRepository.Object,
            _mockGuestSessionRepository.Object,
            _mockDishRepository.Object,
            _mockReservationRepository.Object,
            _mockUserRepository.Object,
            _mockTableRepository.Object);
    }
    [Theory]
    [InlineAutoData(0)]
    [InlineAutoData(3)]
    public async Task CreateGuestSessionAsync_CorrectData_ReturnsSessionToken(
        int guestCount,
        CreateGuestSessionRequest request,
        GroupReservation groupReservation,
        Reservation parentReservation)
    {
        groupReservation.GroupReservationId = request.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;
        groupReservation.ReservationId = parentReservation.Id;

        parentReservation.GuestCount = 5;

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);

        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(parentReservation.Id))
            .ReturnsAsync(parentReservation);

        _mockGuestSessionRepository
            .Setup(r => r.GetAllByGroupReservationIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync([]);

        GuestSession? session = null;
        _mockGuestSessionRepository
            .Setup(r => r.SaveAsync(It.IsAny<GuestSession>()))
            .Callback<GuestSession>(s => session = s);

        _mockGuestSessionRepository
            .Setup(r => r.CountByGroupReservationId(groupReservation.GroupReservationId))
            .ReturnsAsync(guestCount);

        var response = await _guestSessionService.CreateGuestSessionAsync(request);

        Assert.NotEmpty(response.SessionId);
        Assert.NotEmpty(response.SessionToken);
        Assert.NotNull(session);
        Assert.Equal(groupReservation.GroupReservationId, session.GroupReservationId);
        Assert.Equal(request.GuestName, session.GuestName);
        Assert.NotNull(session.SessionTokenHash);
        Assert.NotEmpty(session.SessionTokenHash);
    }

    [Theory]
    [AutoData]
    public async Task CreateGuestSessionAsync_InvalidGroupReservationId_ThrowsGroupReservationNotFoundException(CreateGuestSessionRequest request)
    {
        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(request.GroupReservationId))
            .ReturnsAsync((GroupReservation?)null);

        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() => _guestSessionService.CreateGuestSessionAsync(request));
    }

    [Theory]
    [AutoData]
    public async Task CreateGuestSessionAsync_GroupReservationExpired_ThrowsGroupReservationExpiredException(CreateGuestSessionRequest request, GroupReservation groupReservation)
    {
        groupReservation.GroupReservationId = request.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(request.GroupReservationId))
            .ReturnsAsync(groupReservation);

        await Assert.ThrowsAsync<GroupReservationExpiredException>(() => _guestSessionService.CreateGuestSessionAsync(request));
    }

    [Theory]
    [AutoData]
    public async Task AddDishAsync_ShouldAddNewDish_WhenDishDoesNotExistInSession(GuestSession guestSession, GroupReservation groupReservation, Reservation parentReservation, Table table, Dish dish)
    {
        guestSession.Dishes = [];
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        groupReservation.ReservationId = parentReservation.Id;
        parentReservation.TableId = table.Id;
        dish.State = DishState.Available;
        int quantityToAdd = 2;

        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(guestSession.Id)).ReturnsAsync(guestSession);
        _mockGroupReservationRepository.Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId)).ReturnsAsync(groupReservation);
        _mockReservationRepository.Setup(r => r.GetReservationByIdAsync(parentReservation.Id)).ReturnsAsync(parentReservation);
        _mockTableRepository.Setup(r => r.GetTableAsync(table.Id)).ReturnsAsync(table);
        _mockDishRepository.Setup(r => r.GetDishesByLocationIdAsync(table.LocationId)).ReturnsAsync([dish]);

        _mockGuestSessionRepository.Setup(r => r.TryUpdateExistingAsync(guestSession)).ReturnsAsync(true);

        await _guestSessionService.AddDishAsync(guestSession.Id, dish.Id, quantityToAdd);

        Assert.Single(guestSession.Dishes);
        Assert.Equal(dish.Id, guestSession.Dishes[0].DishId);
        Assert.Equal(quantityToAdd, guestSession.Dishes[0].Quantity);
        Assert.Equal(OrderItemState.Pending, guestSession.Dishes[0].State);

        _mockGuestSessionRepository.Verify(r => r.TryUpdateExistingAsync(guestSession), Times.Once);
        _mockGuestSessionRepository.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task AddDishAsync_ShouldIncreaseQuantity_WhenDishAlreadyExists(GuestSession guestSession, GroupReservation groupReservation, Reservation parentReservation, Table table, Dish dish)
    {
        int initialQuantity = 3;
        int quantityToAdd = 2;
        guestSession.Dishes = [new OrderItem { DishId = dish.Id, Quantity = initialQuantity, State = OrderItemState.Pending }];
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        groupReservation.ReservationId = parentReservation.Id;
        parentReservation.TableId = table.Id;
        dish.State = DishState.Available;

        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(guestSession.Id)).ReturnsAsync(guestSession);
        _mockGroupReservationRepository.Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId)).ReturnsAsync(groupReservation);
        _mockReservationRepository.Setup(r => r.GetReservationByIdAsync(parentReservation.Id)).ReturnsAsync(parentReservation);
        _mockTableRepository.Setup(r => r.GetTableAsync(table.Id)).ReturnsAsync(table);
        _mockDishRepository.Setup(r => r.GetDishesByLocationIdAsync(table.LocationId)).ReturnsAsync([dish]);

        _mockGuestSessionRepository.Setup(r => r.TryUpdateExistingAsync(guestSession)).ReturnsAsync(true);

        await _guestSessionService.AddDishAsync(guestSession.Id, dish.Id, quantityToAdd);

        Assert.Single(guestSession.Dishes);
        Assert.Equal(initialQuantity + quantityToAdd, guestSession.Dishes[0].Quantity);

        _mockGuestSessionRepository.Verify(r => r.TryUpdateExistingAsync(guestSession), Times.Once);
        _mockGuestSessionRepository.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [InlineAutoData(0)]
    [InlineAutoData(-1)]
    public async Task AddDishAsync_ShouldThrowValidationException_WhenQuantityIsZeroOrLess(int quantity, string guestSessionId, Dish dish)
    {
        dish.State = DishState.Available;

        _mockDishRepository
            .Setup(r => r.GetDishByIdAsync(dish.Id))
            .ReturnsAsync(dish);

        await Assert.ThrowsAsync<ValidationException>(() =>
            _guestSessionService.AddDishAsync(guestSessionId, dish.Id, quantity));
    }

    [Theory]
    [AutoData]
    public async Task AddDishAsync_ShouldThrowGuestSessionNotFoundException_WhenSessionNotFound(string guestSessionId, Dish dish)
    {
        dish.State = DishState.Available;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSessionId))
            .ReturnsAsync((GuestSession?)null);

        _mockDishRepository
            .Setup(r => r.GetDishByIdAsync(dish.Id))
            .ReturnsAsync(dish);

        var exception = await Assert.ThrowsAsync<GuestSessionNotFoundException>(() =>
            _guestSessionService.AddDishAsync(guestSessionId, dish.Id, 2));
    }

    [Theory]
    [AutoData]
    public async Task AddDishAsync_ShouldThrowDishNotFound_WhenDishNotFound(GuestSession guestSession, GroupReservation groupReservation, Reservation parentReservation, Table table, string dishId)
    {
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;
        groupReservation.ReservationId = parentReservation.Id;
        parentReservation.TableId = table.Id;

        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(guestSession.Id)).ReturnsAsync(guestSession);
        _mockGroupReservationRepository.Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId)).ReturnsAsync(groupReservation);
        _mockReservationRepository.Setup(r => r.GetReservationByIdAsync(parentReservation.Id)).ReturnsAsync(parentReservation);
        _mockTableRepository.Setup(r => r.GetTableAsync(table.Id)).ReturnsAsync(table);

        _mockDishRepository.Setup(r => r.GetDishesByLocationIdAsync(table.LocationId)).ReturnsAsync([]);

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            _guestSessionService.AddDishAsync(guestSession.Id, dishId, quantity: 2));
    }

    [Theory]
    [AutoData]
    public async Task AddDishAsync_ShouldThrowDishNotAvailable_WhenDishIsNotAvailable(GuestSession guestSession, GroupReservation groupReservation, Reservation parentReservation, Table table, Dish dish)
    {
        dish.State = DishState.OutOfStock;
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;
        groupReservation.ReservationId = parentReservation.Id;
        parentReservation.TableId = table.Id;

        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(guestSession.Id)).ReturnsAsync(guestSession);
        _mockGroupReservationRepository.Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId)).ReturnsAsync(groupReservation);
        _mockReservationRepository.Setup(r => r.GetReservationByIdAsync(parentReservation.Id)).ReturnsAsync(parentReservation);
        _mockTableRepository.Setup(r => r.GetTableAsync(table.Id)).ReturnsAsync(table);
        _mockDishRepository.Setup(r => r.GetDishesByLocationIdAsync(table.LocationId)).ReturnsAsync([dish]);

        var exception = await Assert.ThrowsAsync<DishIsNotAvailableException>(() =>
            _guestSessionService.AddDishAsync(guestSession.Id, dish.Id, quantity: 2));
    }

    [Theory]
    [InlineAutoData(GroupReservationStatus.Submitted, typeof(GroupReservationSubmittedException))]
    [InlineAutoData(GroupReservationStatus.Closed, typeof(GroupReservationExpiredException))]
    public async Task AddDishAsync_ShouldThrowGroupReservationExpired_WhenGroupStatusIsNotOpen(GroupReservationStatus status, Type exceptionType, GuestSession guestSession, GroupReservation groupReservation, Dish dish)
    {
        dish.State = DishState.Available;
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = status;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSession.Id))
            .ReturnsAsync(guestSession);

        _mockDishRepository
            .Setup(r => r.GetDishByIdAsync(dish.Id))
            .ReturnsAsync(dish);

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);

        var exception = await Assert.ThrowsAsync(exceptionType, () =>
            _guestSessionService.AddDishAsync(guestSession.Id, dish.Id, quantity: 2));
    }

    [Theory]
    [AutoData]
    public async Task RemoveDishAsync_ShouldDecreaseQuantity_WhenQuantityIsGreaterThanAmountToRemove(GuestSession guestSession, GroupReservation groupReservation, string dishId)
    {
        int initialQuantity = 5;
        int quantityToRemove = 2;
        guestSession.Dishes =
        [
            new OrderItem { DishId = dishId, Quantity = initialQuantity, State = OrderItemState.Pending }
        ];
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSession.Id))
            .ReturnsAsync(guestSession);


        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);

        _mockGuestSessionRepository.Setup(r => r.TryUpdateExistingAsync(guestSession)).ReturnsAsync(true);

        await _guestSessionService.RemoveDishAsync(guestSession.Id, dishId, quantityToRemove);

        Assert.Single(guestSession.Dishes);
        Assert.Equal(initialQuantity - quantityToRemove, guestSession.Dishes[0].Quantity);

        _mockGuestSessionRepository.Verify(r => r.TryUpdateExistingAsync(guestSession), Times.Once);
        _mockGuestSessionRepository.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task RemoveDishAsync_ShouldRemoveDishFromList_WhenQuantityToRemoveIsEqualOrGreater(GuestSession guestSession, GroupReservation groupReservation, string dishId)
    {
        int initialQuantity = 2;
        int quantityToRemove = 3;
        guestSession.Dishes =
        [
            new OrderItem { DishId = dishId, Quantity = initialQuantity, State = OrderItemState.Pending }
        ];
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSession.Id))
            .ReturnsAsync(guestSession);

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);

        _mockGuestSessionRepository.Setup(r => r.TryUpdateExistingAsync(guestSession)).ReturnsAsync(true);

        await _guestSessionService.RemoveDishAsync(guestSession.Id, dishId, quantityToRemove);

        Assert.Empty(guestSession.Dishes);
        _mockGuestSessionRepository.Verify(r => r.TryUpdateExistingAsync(guestSession), Times.Once);
        _mockGuestSessionRepository.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task RemoveDishAsync_ShouldThrowDishNotFoundException_WhenDishDoesNotExistInSession(GuestSession guestSession, GroupReservation groupReservation, string dishId)
    {
        guestSession.Dishes = [];
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSession.Id))
            .ReturnsAsync(guestSession);

        _mockGroupReservationRepository
           .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
           .ReturnsAsync(groupReservation);

        var exception = await Assert.ThrowsAsync<DishNotFoundException>(() =>
            _guestSessionService.RemoveDishAsync(guestSession.Id, dishId, 2));

        Assert.Contains("not found in guest session", exception.Message);
    }

    [Theory]
    [AutoData]
    public async Task RemoveDishAsync_ShouldThrowValidationException_WhenQuantityIsZeroOrLess(string guestSessionId, string dishId)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _guestSessionService.RemoveDishAsync(guestSessionId, dishId, 0));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _guestSessionService.RemoveDishAsync(guestSessionId, dishId, -5));
    }

    [Theory]
    [AutoData]
    public async Task RemoveDishAsync_ShouldThrowGuestSessionNotFoundException_WhenSessionNotFound(string guestSessionId, string dishId)
    {
        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSessionId))
            .ReturnsAsync((GuestSession?)null);

        var exception = await Assert.ThrowsAsync<GuestSessionNotFoundException>(() =>
            _guestSessionService.RemoveDishAsync(guestSessionId, dishId, 2));
    }
    [Theory]
    [InlineAutoData(1, 0)]
    [InlineAutoData(1, 1)]
    [InlineAutoData(2, 1)]
    [InlineAutoData(2, 2)]
    public async Task CreateGuestSessionAsync_WhenCapacityReached_ThrowsGuestLimitExceededException(
        int reservationCapacity,
        int guestCount,
        CreateGuestSessionRequest request,
        GroupReservation groupReservation,
        Reservation parentReservation)
    {
        groupReservation.Status = GroupReservationStatus.Open;
        groupReservation.GroupReservationId = request.GroupReservationId;
        groupReservation.ReservationId = parentReservation.Id;

        parentReservation.GuestCount = reservationCapacity;

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);

        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(parentReservation.Id))
            .ReturnsAsync(parentReservation);

        _mockGuestSessionRepository
            .Setup(r => r.CountByGroupReservationId(groupReservation.GroupReservationId))
            .ReturnsAsync(guestCount);

        var exception = await Assert.ThrowsAsync<GuestLimitExceededException>(() => _guestSessionService.CreateGuestSessionAsync(request));

        _mockGuestSessionRepository.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task CreateGuestSessionAsync_MissingParentReservation_ThrowsGroupReservationExpiredException(
        CreateGuestSessionRequest request,
        GroupReservation groupReservation)
    {
        groupReservation.GroupReservationId = request.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);

        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(groupReservation.ReservationId))
            .ReturnsAsync((Reservation?)null);

        var exception = await Assert.ThrowsAsync<GroupReservationExpiredException>(() => _guestSessionService.CreateGuestSessionAsync(request));
    }

    [Theory]
    [AutoData]
    public async Task IsAuthorizedAsync_ValidTokenAndId_ReturnsTrue(GuestSession guestSession, byte[] tokenBytes)
    {
        string tokenString = Convert.ToBase64String(tokenBytes);
        var hashString = Convert.ToBase64String(SHA256.HashData(tokenBytes));
        guestSession.SessionTokenHash = hashString;

        _mockGuestSessionRepository
            .Setup(r => r.GetByTokenHash(hashString))
            .ReturnsAsync(guestSession);

        bool result = await _guestSessionService.IsAuthorizedAsync(tokenString, guestSession.Id);

        Assert.True(result);
    }

    [Theory]
    [AutoData]
    public async Task IsAuthorizedAsync_InvalidToken_ReturnsFalse(byte[] tokenBytes, string guestSessionId)
    {
        string tokenString = Convert.ToBase64String(tokenBytes);
        var hashString = Convert.ToBase64String(SHA256.HashData(tokenBytes));

        _mockGuestSessionRepository
            .Setup(r => r.GetByTokenHash(hashString))
            .ReturnsAsync((GuestSession?)null);

        bool result = await _guestSessionService.IsAuthorizedAsync(tokenString, guestSessionId);

        Assert.False(result);
    }

    [Theory]
    [AutoData]
    public async Task IsAuthorizedAsync_InvalidGuestSessionId_ReturnsFalse(GuestSession guestSession, byte[] tokenBytes, string guestSessionId)
    {
        string tokenString = Convert.ToBase64String(tokenBytes);
        var hashString = Convert.ToBase64String(SHA256.HashData(tokenBytes));
        guestSession.SessionTokenHash = hashString;

        _mockGuestSessionRepository
            .Setup(r => r.GetByTokenHash(hashString))
            .ReturnsAsync(guestSession);

        bool result = await _guestSessionService.IsAuthorizedAsync(tokenString, guestSessionId);

        Assert.False(result);
    }
    [Theory]
    [AutoData]
    public async Task SubmitCartAsync_Valid_SubmitsSuccessfuilly(GuestSession guestSession, GroupReservation groupReservation, Dish[] dishes)
    {
        foreach (var dish in dishes)
            dish.State = DishState.Available;

        guestSession.IsSubmitted = false;
        guestSession.GroupReservationId = groupReservation.GroupReservationId;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSession.Id))
            .ReturnsAsync(guestSession);

        _mockGroupReservationRepository
           .Setup(r => r.GetGroupReservationByIdAsync(guestSession.GroupReservationId))
           .ReturnsAsync(groupReservation);

        _mockDishRepository
            .Setup(d => d.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(dishes);

        GuestSession? session = null;
        _mockGuestSessionRepository
            .Setup(r => r.TryUpdateExistingAsync(It.IsAny<GuestSession>()))
            .Callback<GuestSession>(s => session = s)
            .ReturnsAsync(true);

        await _guestSessionService.SubmitCartAsync(guestSession.Id);

        Assert.NotNull(session);
        Assert.True(session.IsSubmitted);
    }

    [Theory]
    [AutoData]
    public async Task AddDishAsync_SessionDeletedBeforeSave_ThrowsGuestSessionNotFoundException(GuestSession guestSession, GroupReservation groupReservation, Reservation parentReservation, Table table, Dish dish)
    {
        guestSession.Dishes = [];
        guestSession.IsSubmitted = false;
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;
        groupReservation.ReservationId = parentReservation.Id;
        parentReservation.TableId = table.Id;
        dish.State = DishState.Available;

        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(guestSession.Id)).ReturnsAsync(guestSession);
        _mockGroupReservationRepository.Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId)).ReturnsAsync(groupReservation);
        _mockReservationRepository.Setup(r => r.GetReservationByIdAsync(parentReservation.Id)).ReturnsAsync(parentReservation);
        _mockTableRepository.Setup(r => r.GetTableAsync(table.Id)).ReturnsAsync(table);
        _mockDishRepository.Setup(r => r.GetDishesByLocationIdAsync(table.LocationId)).ReturnsAsync([dish]);
        // The owner deleted the session after it was loaded: the conditional update fails.
        _mockGuestSessionRepository.Setup(r => r.TryUpdateExistingAsync(guestSession)).ReturnsAsync(false);

        await Assert.ThrowsAsync<GuestSessionNotFoundException>(() => _guestSessionService.AddDishAsync(guestSession.Id, dish.Id, 1));

        _mockGuestSessionRepository.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task RemoveDishAsync_SessionDeletedBeforeSave_ThrowsGuestSessionNotFoundException(GuestSession guestSession, GroupReservation groupReservation, string dishId)
    {
        guestSession.Dishes = [new OrderItem { DishId = dishId, Quantity = 2, State = OrderItemState.Pending }];
        guestSession.IsSubmitted = false;
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(guestSession.Id)).ReturnsAsync(guestSession);
        _mockGroupReservationRepository.Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId)).ReturnsAsync(groupReservation);
        _mockGuestSessionRepository.Setup(r => r.TryUpdateExistingAsync(guestSession)).ReturnsAsync(false);

        await Assert.ThrowsAsync<GuestSessionNotFoundException>(() => _guestSessionService.RemoveDishAsync(guestSession.Id, dishId, 1));

        _mockGuestSessionRepository.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task SubmitCartAsync_SessionDeletedBeforeSave_ThrowsGuestSessionNotFoundException(GuestSession guestSession, GroupReservation groupReservation, Dish[] dishes)
    {
        foreach (var dish in dishes)
            dish.State = DishState.Available;

        guestSession.IsSubmitted = false;
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(guestSession.Id)).ReturnsAsync(guestSession);
        _mockGroupReservationRepository.Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId)).ReturnsAsync(groupReservation);
        _mockDishRepository.Setup(d => d.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>())).ReturnsAsync(dishes);
        _mockGuestSessionRepository.Setup(r => r.TryUpdateExistingAsync(guestSession)).ReturnsAsync(false);

        await Assert.ThrowsAsync<GuestSessionNotFoundException>(() => _guestSessionService.SubmitCartAsync(guestSession.Id));

        _mockGuestSessionRepository.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task SubmitCartAsync_SessionNotFound_ThrowsGuestSessionNotFound(string guestSessionId)
    {
        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSessionId))
            .ReturnsAsync((GuestSession?)null);

        await Assert.ThrowsAsync<GuestSessionNotFoundException>(() => _guestSessionService.SubmitCartAsync(guestSessionId));
    }

    [Theory]
    [InlineAutoData(GroupReservationStatus.Submitted, typeof(GroupReservationSubmittedException))]
    [InlineAutoData(GroupReservationStatus.Closed, typeof(GroupReservationExpiredException))]
    public async Task SubmitCartAsync_GroupReservationNotOpen_ThrowsException(GroupReservationStatus status, Type exceptionType, GuestSession guestSession, GroupReservation groupReservation)
    {
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = status;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSession.Id))
            .ReturnsAsync(guestSession);

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(guestSession.GroupReservationId))
            .ReturnsAsync(groupReservation);

        await Assert.ThrowsAsync(exceptionType, () => _guestSessionService.SubmitCartAsync(guestSession.Id));
    }

    [Theory]
    [AutoData]
    public async Task SubmitCartAsync_GuestSessionHasNoDishes_ThrowsCartIsEmptyException(GuestSession guestSession, GroupReservation groupReservation)
    {
        guestSession.Dishes = [];
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSession.Id))
            .ReturnsAsync(guestSession);

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(guestSession.GroupReservationId))
            .ReturnsAsync(groupReservation);

        await Assert.ThrowsAsync<CartIsEmptyException>(() => _guestSessionService.SubmitCartAsync(guestSession.Id));
    }

    [Theory]
    [AutoData]
    public async Task SubmitCartAsync_GuestSessionSubmitted_ThrowsAlreadySubmited(GuestSession guestSession, GroupReservation groupReservation)
    {
        guestSession.IsSubmitted = true;
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSession.Id))
            .ReturnsAsync(guestSession);

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(guestSession.GroupReservationId))
            .ReturnsAsync(groupReservation);

        await Assert.ThrowsAsync<GuestSessionSubmittedException>(() => _guestSessionService.SubmitCartAsync(guestSession.Id));
    }

    [Theory]
    [InlineAutoData(DishState.OutOfStock)]
    [InlineAutoData(DishState.OnStop)]
    public async Task SubmitCartAsync_DishNotAvailable_ThrowsDishNotAvailable(DishState state, Dish dish, GuestSession guestSession, GroupReservation groupReservation)
    {
        dish.State = state;
        guestSession.IsSubmitted = false;
        guestSession.GroupReservationId = groupReservation.GroupReservationId;
        guestSession.Dishes = 
        [
            new OrderItem()
            {
                DishId = dish.Id,
                Quantity = 1,
            }
        ];

        _mockDishRepository
            .Setup(d => d.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([dish]);

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSession.Id))
            .ReturnsAsync(guestSession);

        _mockGroupReservationRepository
           .Setup(r => r.GetGroupReservationByIdAsync(guestSession.GroupReservationId))
           .ReturnsAsync(groupReservation);

        await Assert.ThrowsAsync<DishIsNotAvailableException>(() => _guestSessionService.SubmitCartAsync(guestSession.Id));
    }
    [Theory]
    [MemberAutoData(nameof(PepareDishesForGuestSession))]
    public async Task GetGuestSessionInfoAsync_Guest_ReturnsAsync(
        IEnumerable<string> dishIds,
        IEnumerable<Dish> dishes,
        List<OrderItem> orderItems,
        decimal expectedTotal,
        GuestSession session)
    {
        session.Dishes = orderItems;
        session.IsOwnerSession = false;

        _mockDishRepository
            .Setup(r => r.GetDishesByIdsAsync(dishIds))
            .ReturnsAsync(dishes);

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(session.Id))
            .ReturnsAsync(session);

        GetGuestSessionResponse result = await _guestSessionService.GetGuestSessionInfoAsync(session.Id);

        Assert.Equal(session.GuestName, result.Name);
        Assert.Equal(dishes.Count(), result.Items.Count());
        Assert.Equal(expectedTotal, result.Total);
    }

    [Theory]
    [MemberAutoData(nameof(PepareDishesForGuestSession))]
    public async Task GetGuestSessionInfoAsync_GroupOwner_ReturnsAsync(
        IEnumerable<string> dishIds,
        IEnumerable<Dish> dishes,
        List<OrderItem> orderItems,
        decimal expectedTotal,
        GuestSession session,
        GroupReservation groupReservation,
        Reservation reservation,
        string customerId,
        string ownerName,
        UserType userType)
    {
        session.Dishes = orderItems;
        session.IsOwnerSession = true;
        session.GroupReservationId = groupReservation.GroupReservationId;
        groupReservation.ReservationId = reservation.Id;
        reservation.CustomerId = customerId;
        userType.Attributes.Add(new AttributeType()
        {
            Name = "given_name",
            Value = ownerName,
        });

        _mockDishRepository
            .Setup(r => r.GetDishesByIdsAsync(dishIds))
            .ReturnsAsync(dishes);

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(session.Id))
            .ReturnsAsync(session);

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);

        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        _mockUserRepository
            .Setup(r => r.GetUserByIdAsync(customerId))
            .ReturnsAsync(userType);

        GetGuestSessionResponse result = await _guestSessionService.GetGuestSessionInfoAsync(session.Id);

        Assert.Equal(ownerName, result.Name);
        Assert.Equal(dishes.Count(), result.Items.Count());
        Assert.Equal(expectedTotal, result.Total);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupSessionsAsync_Owner_ReturnsParticipantsWithTotals(
        GroupReservation groupReservation,
        Reservation reservation,
        string userId)
    {
        groupReservation.ReservationId = reservation.Id;
        reservation.CustomerId = userId;

        var dish1 = new Dish { Id = "d1", Price = 10 };
        var dish2 = new Dish { Id = "d2", Price = 25 };

        var ownerSession = new GuestSession
        {
            Id = "owner",
            GuestName = "Owner",
            GroupReservationId = groupReservation.GroupReservationId,
            IsOwnerSession = true,
            Dishes = [new OrderItem { DishId = "d1", Quantity = 2 }],
        };
        var guestSession = new GuestSession
        {
            Id = "guest",
            GuestName = "Andrii",
            GroupReservationId = groupReservation.GroupReservationId,
            IsSubmitted = true,
            Dishes =
            [
                new OrderItem { DishId = "d1", Quantity = 1 },
                new OrderItem { DishId = "d2", Quantity = 2 },
            ],
        };

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);

        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        _mockGuestSessionRepository
            .Setup(r => r.GetAllByGroupReservationIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync([ownerSession, guestSession]);

        _mockDishRepository
            .Setup(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([dish1, dish2]);

        GetAllSessionsResponse result = await _guestSessionService.GetGroupSessionsAsync(userId, groupReservation.GroupReservationId);

        var participants = result.Participants.ToList();
        Assert.Equal(2, participants.Count);

        var owner = participants.Single(p => p.Id == "owner");
        Assert.Equal(ParticipantType.Owner, owner.Type);
        Assert.Equal(20m, owner.Total);

        var guest = participants.Single(p => p.Id == "guest");
        Assert.Equal(ParticipantType.Guest, guest.Type);
        Assert.Equal("Andrii", guest.Name);
        Assert.Equal(60m, guest.Total);
        Assert.True(guest.IsSubmitted);

        Assert.Equal(80m, result.Total);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupSessionsAsync_NotOwner_ThrowsActionForbiddenException(
        GroupReservation groupReservation,
        Reservation reservation,
        string userId)
    {
        groupReservation.ReservationId = reservation.Id;

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);

        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        await Assert.ThrowsAsync<ActionForbiddenException>(() => _guestSessionService.GetGroupSessionsAsync(userId, groupReservation.GroupReservationId));

        _mockGuestSessionRepository.Verify(r => r.GetAllByGroupReservationIdAsync(It.IsAny<string>()), Times.Never);
    }

    private void SetupOwnedGroup(GroupReservation groupReservation, Reservation reservation, string userId)
    {
        groupReservation.ReservationId = reservation.Id;
        reservation.CustomerId = userId;

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);

        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupSessionsAsync_EmptyGroup_ReturnsEmptyParticipantsAndZeroTotal(
        GroupReservation groupReservation,
        Reservation reservation,
        string userId)
    {
        SetupOwnedGroup(groupReservation, reservation, userId);
        _mockGuestSessionRepository
            .Setup(r => r.GetAllByGroupReservationIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync([]);

        var result = await _guestSessionService.GetGroupSessionsAsync(userId, groupReservation.GroupReservationId);

        Assert.Empty(result.Participants);
        Assert.Equal(0m, result.Total);
        _mockDishRepository.Verify(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupSessionsAsync_SessionsWithoutDishes_DoesNotQueryDishRepository(
        GroupReservation groupReservation,
        Reservation reservation,
        string userId)
    {
        SetupOwnedGroup(groupReservation, reservation, userId);
        var ownerSession = new GuestSession
        {
            Id = "owner",
            GuestName = "Owner",
            GroupReservationId = groupReservation.GroupReservationId,
            IsOwnerSession = true,
            Dishes = [],
        };
        _mockGuestSessionRepository
            .Setup(r => r.GetAllByGroupReservationIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync([ownerSession]);

        var result = await _guestSessionService.GetGroupSessionsAsync(userId, groupReservation.GroupReservationId);

        var participant = Assert.Single(result.Participants);
        Assert.Equal("owner", participant.Id);
        Assert.Empty(participant.Items);
        Assert.Equal(0m, participant.Total);
        Assert.Equal(0m, result.Total);
        _mockDishRepository.Verify(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupSessionsAsync_DishMissingInRepository_SkipsItem(
        GroupReservation groupReservation,
        Reservation reservation,
        string userId)
    {
        SetupOwnedGroup(groupReservation, reservation, userId);
        var session = new GuestSession
        {
            Id = "guest",
            GuestName = "Guest",
            GroupReservationId = groupReservation.GroupReservationId,
            Dishes =
            [
                new OrderItem { DishId = "existing", Quantity = 3 },
                new OrderItem { DishId = "deleted", Quantity = 5 },
            ],
        };
        _mockGuestSessionRepository
            .Setup(r => r.GetAllByGroupReservationIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync([session]);
        _mockDishRepository
            .Setup(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([new Dish { Id = "existing", Price = 7 }]);

        var result = await _guestSessionService.GetGroupSessionsAsync(userId, groupReservation.GroupReservationId);

        var participant = Assert.Single(result.Participants);
        var item = Assert.Single(participant.Items);
        Assert.Equal("existing", item.Dish.Id);
        Assert.Equal(3, item.Quantity);
        Assert.Equal(21m, participant.Total);
        Assert.Equal(21m, result.Total);
    }

    [Theory]
    [InlineAutoData(true, ParticipantType.Owner)]
    [InlineAutoData(false, ParticipantType.Guest)]
    public async Task GetGroupSessionsAsync_IsOwnerSessionFlag_MapsParticipantTypeAndName(
        bool isOwnerSession,
        ParticipantType expectedType,
        GroupReservation groupReservation,
        Reservation reservation,
        string userId,
        string guestName)
    {
        SetupOwnedGroup(groupReservation, reservation, userId);
        var session = new GuestSession
        {
            Id = "session",
            GuestName = guestName,
            GroupReservationId = groupReservation.GroupReservationId,
            IsOwnerSession = isOwnerSession,
            IsSubmitted = isOwnerSession,
            Dishes = [],
        };
        _mockGuestSessionRepository
            .Setup(r => r.GetAllByGroupReservationIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync([session]);

        var result = await _guestSessionService.GetGroupSessionsAsync(userId, groupReservation.GroupReservationId);

        var participant = Assert.Single(result.Participants);
        Assert.Equal(expectedType, participant.Type);
        Assert.Equal(guestName, participant.Name);
        Assert.Equal(isOwnerSession, participant.IsSubmitted);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupSessionsAsync_MultipleParticipantsAndItems_ComputesTotalsAndQueriesDistinctDishIds(
        GroupReservation groupReservation,
        Reservation reservation,
        string userId)
    {
        SetupOwnedGroup(groupReservation, reservation, userId);
        var sessions = new List<GuestSession>
        {
            new()
            {
                Id = "s1", GuestName = "Owner", GroupReservationId = groupReservation.GroupReservationId, IsOwnerSession = true,
                Dishes = [new OrderItem { DishId = "a", Quantity = 1 }, new OrderItem { DishId = "b", Quantity = 2 }],
            },
            new()
            {
                Id = "s2", GuestName = "Anna", GroupReservationId = groupReservation.GroupReservationId,
                Dishes = [new OrderItem { DishId = "b", Quantity = 1 }, new OrderItem { DishId = "c", Quantity = 4 }],
            },
            new()
            {
                Id = "s3", GuestName = "Bob", GroupReservationId = groupReservation.GroupReservationId,
                Dishes = [],
            },
        };
        _mockGuestSessionRepository
            .Setup(r => r.GetAllByGroupReservationIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(sessions);

        IEnumerable<string>? requestedIds = null;
        _mockDishRepository
            .Setup(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .Callback<IEnumerable<string>>(ids => requestedIds = ids.ToList())
            .ReturnsAsync(
            [
                new Dish { Id = "a", Name = "Soup", Price = 10.5m },
                new Dish { Id = "b", Name = "Steak", Price = 30m },
                new Dish { Id = "c", Name = "Cake", Price = 4.25m },
            ]);

        var result = await _guestSessionService.GetGroupSessionsAsync(userId, groupReservation.GroupReservationId);

        var participants = result.Participants.ToList();
        Assert.Equal(new[] { "s1", "s2", "s3" }, participants.Select(p => p.Id));
        Assert.Equal(70.5m, participants[0].Total);
        Assert.Equal(47m, participants[1].Total);
        Assert.Equal(0m, participants[2].Total);
        Assert.Equal(117.5m, result.Total);

        var s2Items = participants[1].Items.ToList();
        Assert.Equal(2, s2Items.Count);
        Assert.Equal("Steak", s2Items[0].Dish.Name);
        Assert.Equal(1, s2Items[0].Quantity);
        Assert.Equal("Cake", s2Items[1].Dish.Name);
        Assert.Equal(4, s2Items[1].Quantity);

        Assert.NotNull(requestedIds);
        Assert.Equal(new[] { "a", "b", "c" }, requestedIds!.OrderBy(x => x));
        _mockDishRepository.Verify(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()), Times.Once);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupSessionsAsync_GroupNotFound_ThrowsGroupReservationNotFoundException(
        string groupReservationId,
        string userId)
    {
        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservationId))
            .ReturnsAsync((GroupReservation?)null);

        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() => _guestSessionService.GetGroupSessionsAsync(userId, groupReservationId));

        _mockReservationRepository.Verify(r => r.GetReservationByIdAsync(It.IsAny<string>()), Times.Never);
        _mockGuestSessionRepository.Verify(r => r.GetAllByGroupReservationIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupSessionsAsync_ParentReservationNotFound_ThrowsReservationNotFoundException(
        GroupReservation groupReservation,
        string userId)
    {
        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);
        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(groupReservation.ReservationId))
            .ReturnsAsync((Reservation?)null);

        await Assert.ThrowsAsync<ReservationNotFoundException>(() => _guestSessionService.GetGroupSessionsAsync(userId, groupReservation.GroupReservationId));

        _mockGuestSessionRepository.Verify(r => r.GetAllByGroupReservationIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task DeleteGuestSessionAsync_ValidRequest_ReturnsSnapshotOfRemainingSessions(
        GroupReservation groupReservation,
        Reservation reservation,
        string userId)
    {
        groupReservation.Status = GroupReservationStatus.Open;
        SetupOwnedGroup(groupReservation, reservation, userId);
        var removed = new GuestSession
        {
            Id = "removed",
            GuestName = "Leaver",
            GroupReservationId = groupReservation.GroupReservationId,
            Dishes = [new OrderItem { DishId = "x", Quantity = 9 }],
        };
        var ownerSession = new GuestSession
        {
            Id = "owner",
            GuestName = "Owner",
            GroupReservationId = groupReservation.GroupReservationId,
            IsOwnerSession = true,
            Dishes = [new OrderItem { DishId = "d1", Quantity = 2 }],
        };
        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(removed.Id))
            .ReturnsAsync(removed);
        _mockGuestSessionRepository
            .Setup(r => r.GetAllByGroupReservationIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync([ownerSession]);
        _mockDishRepository
            .Setup(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([new Dish { Id = "d1", Price = 15m }]);

        var result = await _guestSessionService.DeleteGuestSessionAsync(userId, groupReservation.GroupReservationId, removed.Id);

        _mockGuestSessionRepository.Verify(r => r.DeleteAsync(removed), Times.Once);
        var participant = Assert.Single(result.Participants);
        Assert.Equal("owner", participant.Id);
        Assert.Equal(ParticipantType.Owner, participant.Type);
        Assert.Equal(30m, participant.Total);
        Assert.Equal(30m, result.Total);
    }

    [Theory]
    [AutoData]
    public async Task DeleteGuestSessionAsync_IndexStillReturnsDeletedSession_ExcludesItFromSnapshot(
        GroupReservation groupReservation,
        Reservation reservation,
        string userId)
    {
        groupReservation.Status = GroupReservationStatus.Open;
        SetupOwnedGroup(groupReservation, reservation, userId);
        var removed = new GuestSession
        {
            Id = "removed",
            GuestName = "Leaver",
            GroupReservationId = groupReservation.GroupReservationId,
            Dishes = [new OrderItem { DishId = "d1", Quantity = 9 }],
        };
        var ownerSession = new GuestSession
        {
            Id = "owner",
            GuestName = "Owner",
            GroupReservationId = groupReservation.GroupReservationId,
            IsOwnerSession = true,
            Dishes = [new OrderItem { DishId = "d1", Quantity = 2 }],
        };
        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(removed.Id))
            .ReturnsAsync(removed);
        // Simulates GSI lag: the query still returns the just-deleted session.
        _mockGuestSessionRepository
            .Setup(r => r.GetAllByGroupReservationIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync([ownerSession, removed]);
        _mockDishRepository
            .Setup(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([new Dish { Id = "d1", Price = 15m }]);

        var result = await _guestSessionService.DeleteGuestSessionAsync(userId, groupReservation.GroupReservationId, removed.Id);

        var participant = Assert.Single(result.Participants);
        Assert.Equal("owner", participant.Id);
        Assert.DoesNotContain(result.Participants, p => p.Id == removed.Id);
        Assert.Equal(30m, result.Total);
    }

    [Theory]
    [AutoData]
    public async Task DeleteGuestSessionAsync_OwnerSession_ThrowsValidationExceptionAndDoesNotDelete(
        GroupReservation groupReservation,
        Reservation reservation,
        string userId)
    {
        groupReservation.Status = GroupReservationStatus.Open;
        SetupOwnedGroup(groupReservation, reservation, userId);
        var ownerSession = new GuestSession
        {
            Id = $"owner-{groupReservation.GroupReservationId}",
            GuestName = "Owner",
            GroupReservationId = groupReservation.GroupReservationId,
            IsOwnerSession = true,
            Dishes = [],
        };
        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(ownerSession.Id))
            .ReturnsAsync(ownerSession);

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            _guestSessionService.DeleteGuestSessionAsync(userId, groupReservation.GroupReservationId, ownerSession.Id));

        Assert.Equal(typeof(ValidationException), exception.GetType());
        _mockGuestSessionRepository.Verify(r => r.DeleteAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [InlineAutoData(GroupReservationStatus.Submitted, typeof(GroupReservationSubmittedException))]
    [InlineAutoData(GroupReservationStatus.Closed, typeof(GroupReservationExpiredException))]
    public async Task DeleteGuestSessionAsync_GroupNotOpen_ThrowsSpecificExceptionAndDoesNotDelete(
        GroupReservationStatus status,
        Type exceptionType,
        GroupReservation groupReservation,
        Reservation reservation,
        string userId)
    {
        groupReservation.Status = status;
        SetupOwnedGroup(groupReservation, reservation, userId);
        var guest = new GuestSession
        {
            Id = "guest",
            GuestName = "Guest",
            GroupReservationId = groupReservation.GroupReservationId,
            Dishes = [],
        };
        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guest.Id))
            .ReturnsAsync(guest);

        await Assert.ThrowsAsync(exceptionType, () =>
            _guestSessionService.DeleteGuestSessionAsync(userId, groupReservation.GroupReservationId, guest.Id));

        _mockGuestSessionRepository.Verify(r => r.DeleteAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task DeleteGuestSessionAsync_SubmittedGuestInOpenGroup_DeletesSession(
        GroupReservation groupReservation,
        Reservation reservation,
        string userId)
    {
        groupReservation.Status = GroupReservationStatus.Open;
        SetupOwnedGroup(groupReservation, reservation, userId);
        var submittedGuest = new GuestSession
        {
            Id = "submitted-guest",
            GuestName = "Guest",
            GroupReservationId = groupReservation.GroupReservationId,
            IsSubmitted = true,
            Dishes = [new OrderItem { DishId = "d1", Quantity = 1 }],
        };
        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(submittedGuest.Id))
            .ReturnsAsync(submittedGuest);
        _mockGuestSessionRepository
            .Setup(r => r.GetAllByGroupReservationIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync([]);

        var result = await _guestSessionService.DeleteGuestSessionAsync(userId, groupReservation.GroupReservationId, submittedGuest.Id);

        _mockGuestSessionRepository.Verify(r => r.DeleteAsync(submittedGuest), Times.Once);
        Assert.Empty(result.Participants);
        Assert.Equal(0m, result.Total);
    }

    [Theory]
    [AutoData]
    public async Task DeleteGuestSessionAsync_GroupNotFound_ThrowsGroupReservationNotFoundExceptionAndDoesNotDelete(
        string groupReservationId,
        string guestSessionId,
        string userId)
    {
        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservationId))
            .ReturnsAsync((GroupReservation?)null);

        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() =>
            _guestSessionService.DeleteGuestSessionAsync(userId, groupReservationId, guestSessionId));

        _mockReservationRepository.Verify(r => r.GetReservationByIdAsync(It.IsAny<string>()), Times.Never);
        _mockGuestSessionRepository.Verify(r => r.GetByGuestSessionId(It.IsAny<string>()), Times.Never);
        _mockGuestSessionRepository.Verify(r => r.DeleteAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task DeleteGuestSessionAsync_ParentReservationNotFound_ThrowsReservationNotFoundExceptionAndDoesNotDelete(
        GroupReservation groupReservation,
        string guestSessionId,
        string userId)
    {
        groupReservation.Status = GroupReservationStatus.Open;
        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);
        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(groupReservation.ReservationId))
            .ReturnsAsync((Reservation?)null);

        await Assert.ThrowsAsync<ReservationNotFoundException>(() =>
            _guestSessionService.DeleteGuestSessionAsync(userId, groupReservation.GroupReservationId, guestSessionId));

        _mockGuestSessionRepository.Verify(r => r.GetByGuestSessionId(It.IsAny<string>()), Times.Never);
        _mockGuestSessionRepository.Verify(r => r.DeleteAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [InlineAutoData(GroupReservationStatus.Open)]
    [InlineAutoData(GroupReservationStatus.Submitted)]
    [InlineAutoData(GroupReservationStatus.Closed)]
    public async Task DeleteGuestSessionAsync_CallerIsNotReservationOwner_ThrowsActionForbiddenExceptionBeforeAnyOtherCheck(
        GroupReservationStatus status,
        GroupReservation groupReservation,
        Reservation reservation,
        string userId,
        string guestSessionId)
    {
        groupReservation.Status = status;
        groupReservation.ReservationId = reservation.Id;
        reservation.CustomerId = "another-user";
        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);
        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        await Assert.ThrowsAsync<ActionForbiddenException>(() =>
            _guestSessionService.DeleteGuestSessionAsync(userId, groupReservation.GroupReservationId, guestSessionId));

        _mockGuestSessionRepository.Verify(r => r.GetByGuestSessionId(It.IsAny<string>()), Times.Never);
        _mockGuestSessionRepository.Verify(r => r.GetAllByGroupReservationIdAsync(It.IsAny<string>()), Times.Never);
        _mockGuestSessionRepository.Verify(r => r.DeleteAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [InlineAutoData(GroupReservationStatus.Submitted, typeof(GroupReservationSubmittedException))]
    [InlineAutoData(GroupReservationStatus.Closed, typeof(GroupReservationExpiredException))]
    public async Task DeleteGuestSessionAsync_GroupNotOpen_ThrowsStatusExceptionBeforeLoadingSession(
        GroupReservationStatus status,
        Type exceptionType,
        GroupReservation groupReservation,
        Reservation reservation,
        string userId,
        string missingSessionId)
    {
        groupReservation.Status = status;
        SetupOwnedGroup(groupReservation, reservation, userId);
        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(missingSessionId))
            .ReturnsAsync((GuestSession?)null);

        var exception = await Assert.ThrowsAsync(exceptionType, () =>
            _guestSessionService.DeleteGuestSessionAsync(userId, groupReservation.GroupReservationId, missingSessionId));

        Assert.Equal(exceptionType, exception.GetType());
        _mockGuestSessionRepository.Verify(r => r.GetByGuestSessionId(It.IsAny<string>()), Times.Never);
        _mockGuestSessionRepository.Verify(r => r.DeleteAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task DeleteGuestSessionAsync_SessionNotFound_ThrowsGuestSessionNotFoundExceptionAndDoesNotDelete(
        GroupReservation groupReservation,
        Reservation reservation,
        string userId,
        string missingSessionId)
    {
        groupReservation.Status = GroupReservationStatus.Open;
        SetupOwnedGroup(groupReservation, reservation, userId);
        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(missingSessionId))
            .ReturnsAsync((GuestSession?)null);

        await Assert.ThrowsAsync<GuestSessionNotFoundException>(() =>
            _guestSessionService.DeleteGuestSessionAsync(userId, groupReservation.GroupReservationId, missingSessionId));

        _mockGuestSessionRepository.Verify(r => r.DeleteAsync(It.IsAny<GuestSession>()), Times.Never);
        _mockGuestSessionRepository.Verify(r => r.GetAllByGroupReservationIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineAutoData(false)]
    [InlineAutoData(true)]
    public async Task DeleteGuestSessionAsync_SessionFromAnotherGroup_ThrowsValidationExceptionAndDoesNotDelete(
        bool isOwnerSession,
        GroupReservation groupReservation,
        Reservation reservation,
        string userId,
        string otherGroupId)
    {
        groupReservation.Status = GroupReservationStatus.Open;
        SetupOwnedGroup(groupReservation, reservation, userId);
        var foreignSession = new GuestSession
        {
            Id = "foreign",
            GuestName = "Stranger",
            GroupReservationId = otherGroupId,
            IsOwnerSession = isOwnerSession,
            Dishes = [],
        };
        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(foreignSession.Id))
            .ReturnsAsync(foreignSession);

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            _guestSessionService.DeleteGuestSessionAsync(userId, groupReservation.GroupReservationId, foreignSession.Id));

        Assert.Equal(typeof(ValidationException), exception.GetType());
        Assert.Contains("does not belong", exception.Message);
        _mockGuestSessionRepository.Verify(r => r.DeleteAsync(It.IsAny<GuestSession>()), Times.Never);
        _mockGuestSessionRepository.Verify(r => r.GetAllByGroupReservationIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task DeleteGuestSessionAsync_GuestDeleted_SnapshotKeepsOwnerAndOtherGuestsWithTotals(
        GroupReservation groupReservation,
        Reservation reservation,
        string userId)
    {
        groupReservation.Status = GroupReservationStatus.Open;
        SetupOwnedGroup(groupReservation, reservation, userId);
        var groupId = groupReservation.GroupReservationId;
        var owner = new GuestSession
        {
            Id = $"owner-{groupId}", GuestName = "Owner", GroupReservationId = groupId, IsOwnerSession = true,
            Dishes = [new OrderItem { DishId = "soup", Quantity = 2 }],
        };
        var removed = new GuestSession
        {
            Id = "removed", GuestName = "Leaver", GroupReservationId = groupId,
            Dishes = [new OrderItem { DishId = "lobster", Quantity = 3 }],
        };
        var remainingGuest = new GuestSession
        {
            Id = "anna", GuestName = "Anna", GroupReservationId = groupId, IsSubmitted = true,
            Dishes = [new OrderItem { DishId = "soup", Quantity = 1 }, new OrderItem { DishId = "steak", Quantity = 2 }],
        };
        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(removed.Id))
            .ReturnsAsync(removed);
        _mockGuestSessionRepository
            .Setup(r => r.GetAllByGroupReservationIdAsync(groupId))
            .ReturnsAsync([owner, removed, remainingGuest]);
        IEnumerable<string>? requestedDishIds = null;
        _mockDishRepository
            .Setup(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .Callback<IEnumerable<string>>(ids => requestedDishIds = ids.ToList())
            .ReturnsAsync(
            [
                new Dish { Id = "soup", Name = "Soup", Price = 8.5m },
                new Dish { Id = "steak", Name = "Steak", Price = 30m },
            ]);

        var result = await _guestSessionService.DeleteGuestSessionAsync(userId, groupId, removed.Id);

        _mockGuestSessionRepository.Verify(r => r.DeleteAsync(removed), Times.Once);
        _mockGuestSessionRepository.Verify(r => r.DeleteAsync(It.IsAny<GuestSession>()), Times.Once);

        var participants = result.Participants.ToList();
        Assert.Equal(new[] { owner.Id, remainingGuest.Id }, participants.Select(p => p.Id));

        var ownerParticipant = participants[0];
        Assert.Equal(ParticipantType.Owner, ownerParticipant.Type);
        Assert.Equal(17m, ownerParticipant.Total);

        var guestParticipant = participants[1];
        Assert.Equal(ParticipantType.Guest, guestParticipant.Type);
        Assert.Equal("Anna", guestParticipant.Name);
        Assert.True(guestParticipant.IsSubmitted);
        Assert.Equal(68.5m, guestParticipant.Total);

        Assert.Equal(85.5m, result.Total);
        Assert.NotNull(requestedDishIds);
        Assert.Equal(new[] { "soup", "steak" }, requestedDishIds!.OrderBy(x => x));
    }

    [Theory]
    [AutoData]
    public async Task DeleteGuestSessionAsync_ValidRequest_DeletesBeforeBuildingSnapshot(
        GroupReservation groupReservation,
        Reservation reservation,
        string userId)
    {
        groupReservation.Status = GroupReservationStatus.Open;
        SetupOwnedGroup(groupReservation, reservation, userId);
        var guest = new GuestSession
        {
            Id = "guest", GuestName = "Guest", GroupReservationId = groupReservation.GroupReservationId, Dishes = [],
        };
        var calls = new List<string>();
        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guest.Id))
            .ReturnsAsync(guest);
        _mockGuestSessionRepository
            .Setup(r => r.DeleteAsync(guest))
            .Callback(() => calls.Add("delete"))
            .Returns(Task.CompletedTask);
        _mockGuestSessionRepository
            .Setup(r => r.GetAllByGroupReservationIdAsync(groupReservation.GroupReservationId))
            .Callback(() => calls.Add("query"))
            .ReturnsAsync([]);

        await _guestSessionService.DeleteGuestSessionAsync(userId, groupReservation.GroupReservationId, guest.Id);

        Assert.Equal(new[] { "delete", "query" }, calls);
    }

    [Theory]
    [AutoData]
    public async Task DeleteGuestSessionAsync_IndexReturnsNull_ReturnsEmptySnapshot(
        GroupReservation groupReservation,
        Reservation reservation,
        string userId)
    {
        groupReservation.Status = GroupReservationStatus.Open;
        SetupOwnedGroup(groupReservation, reservation, userId);
        var guest = new GuestSession
        {
            Id = "guest", GuestName = "Guest", GroupReservationId = groupReservation.GroupReservationId, Dishes = [],
        };
        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guest.Id))
            .ReturnsAsync(guest);
        _mockGuestSessionRepository
            .Setup(r => r.GetAllByGroupReservationIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync((List<GuestSession>)null!);

        var result = await _guestSessionService.DeleteGuestSessionAsync(userId, groupReservation.GroupReservationId, guest.Id);

        Assert.Empty(result.Participants);
        Assert.Equal(0m, result.Total);
        _mockGuestSessionRepository.Verify(r => r.DeleteAsync(guest), Times.Once);
        _mockDishRepository.Verify(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    public static TheoryData<IEnumerable<string>, IEnumerable<Dish>, List<OrderItem>, decimal> PepareDishesForGuestSession()
    {
        var dishMap = new Dictionary<string, Dish>()
        {
            ["1"] = new()
            {
                Id = "1",
                Price = 1
            },
            ["2"] = new()
            {
                Id = "2",
                Price = 2
            },
            ["3"] = new()
            {
                Id = "3",
                Price = 3
            }
        };

        var orderItems = new List<OrderItem>
        {
            new OrderItem()
                    {
                        DishId = "1",
                        Quantity = 2
                    },
                    new OrderItem()
                    {
                        DishId = "2",
                        Quantity = 3
                    },
                    new OrderItem()
                    {
                        DishId = "3",
                        Quantity = 1
                    }
        };

        var expectedTotal = orderItems.Sum(o => dishMap[o.DishId].Price * o.Quantity);

        return new()
        {
            { dishMap.Keys, dishMap.Values, orderItems, expectedTotal }
        };
    }

    [Theory]
    [AutoData]
    public async Task GetOrCreateOwnerSessionIdAsync_OwnerSessionExists_ReturnsItsIdWithoutSaving(
        string userId,
        Reservation reservation,
        GroupReservation groupReservation,
        GuestSession guestSession,
        GuestSession ownerSession)
    {
        reservation.CustomerId = userId;
        groupReservation.ReservationId = reservation.Id;
        guestSession.IsOwnerSession = false;
        ownerSession.IsOwnerSession = true;
        ownerSession.Id = $"owner-{groupReservation.GroupReservationId}";

        _mockReservationRepository.Setup(r => r.GetReservationByIdAsync(reservation.Id)).ReturnsAsync(reservation);
        _mockGroupReservationRepository.Setup(r => r.GetByReservationIdAsync(reservation.Id)).ReturnsAsync(groupReservation);
        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(guestSession.Id)).ReturnsAsync(guestSession);
        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(ownerSession.Id)).ReturnsAsync(ownerSession);

        var response = await _guestSessionService.GetOrCreateOwnerSessionIdAsync(userId, reservation.Id);

        Assert.Equal(ownerSession.Id, response.GuestSessionId);
        _mockGuestSessionRepository.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
        _mockGuestSessionRepository.Verify(r => r.GetAllByGroupReservationIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task GetOrCreateOwnerSessionIdAsync_OwnerSessionMissing_CreatesOwnerSessionAndReturnsItsId(
        string userId,
        Reservation reservation,
        GroupReservation groupReservation,
        GuestSession guestSession)
    {
        reservation.CustomerId = userId;
        groupReservation.ReservationId = reservation.Id;
        guestSession.IsOwnerSession = false;

        _mockReservationRepository.Setup(r => r.GetReservationByIdAsync(reservation.Id)).ReturnsAsync(reservation);
        _mockGroupReservationRepository.Setup(r => r.GetByReservationIdAsync(reservation.Id)).ReturnsAsync(groupReservation);
        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(guestSession.Id)).ReturnsAsync(guestSession);
        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId($"owner-{groupReservation.GroupReservationId}"))
            .ReturnsAsync((GuestSession?)null);

        GuestSession? saved = null;
        _mockGuestSessionRepository
            .Setup(r => r.SaveAsync(It.IsAny<GuestSession>()))
            .Callback<GuestSession>(s => saved = s);

        var response = await _guestSessionService.GetOrCreateOwnerSessionIdAsync(userId, reservation.Id);

        _mockGuestSessionRepository.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Once);
        Assert.NotNull(saved);
        Assert.True(saved.IsOwnerSession);
        Assert.Equal(userId, saved.OwnerUserId);
        Assert.Equal(groupReservation.GroupReservationId, saved.GroupReservationId);
        Assert.Equal("Owner", saved.GuestName);
        Assert.Empty(saved.Dishes);
        Assert.Equal($"owner-{groupReservation.GroupReservationId}", saved.Id);
        Assert.Equal(saved.Id, response.GuestSessionId);
    }

    [Theory]
    [AutoData]
    public async Task GetOrCreateOwnerSessionIdAsync_CallerIsNotOwner_ThrowsActionForbiddenException(
        string userId,
        Reservation reservation)
    {
        _mockReservationRepository.Setup(r => r.GetReservationByIdAsync(reservation.Id)).ReturnsAsync(reservation);

        await Assert.ThrowsAsync<ActionForbiddenException>(() =>
            _guestSessionService.GetOrCreateOwnerSessionIdAsync(userId, reservation.Id));

        _mockGuestSessionRepository.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task GetOrCreateOwnerSessionIdAsync_ReservationNotFound_ThrowsReservationNotFoundException(
        string userId,
        string reservationId)
    {
        _mockReservationRepository.Setup(r => r.GetReservationByIdAsync(reservationId)).ReturnsAsync((Reservation?)null);

        await Assert.ThrowsAsync<ReservationNotFoundException>(() =>
            _guestSessionService.GetOrCreateOwnerSessionIdAsync(userId, reservationId));
    }

    [Theory]
    [AutoData]
    public async Task GetOrCreateOwnerSessionIdAsync_GroupReservationNotFound_ThrowsGroupReservationNotFoundException(
        string userId,
        Reservation reservation)
    {
        reservation.CustomerId = userId;

        _mockReservationRepository.Setup(r => r.GetReservationByIdAsync(reservation.Id)).ReturnsAsync(reservation);
        _mockGroupReservationRepository.Setup(r => r.GetByReservationIdAsync(reservation.Id)).ReturnsAsync((GroupReservation?)null);

        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() =>
            _guestSessionService.GetOrCreateOwnerSessionIdAsync(userId, reservation.Id));
    }

    [Theory]
    [AutoData]
    public async Task GetGroupReservationStatusAsync_OwnerSessionExists_OwnerCountedOnce(
        GroupReservation groupReservation,
        Reservation reservation,
        GuestSession guestSession,
        GuestSession ownerSession)
    {
        groupReservation.ReservationId = reservation.Id;
        guestSession.IsOwnerSession = false;
        ownerSession.IsOwnerSession = true;

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);
        _mockReservationRepository.Setup(r => r.GetReservationByIdAsync(reservation.Id)).ReturnsAsync(reservation);
        _mockGuestSessionRepository
            .Setup(r => r.GetAllByGroupReservationIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync([guestSession, ownerSession]);

        var response = await _guestSessionService.GetGroupReservationStatusAsync(groupReservation.GroupReservationId);

        // 1 joined guest + 1 owner; the owner's session must not be counted on top of that.
        Assert.Equal(2, response.CurrentParticipants);
    }

    [Theory]
    [AutoData]
    public async Task GetOrCreateOwnerSessionIdAsync_CallerIsNotOwner_DoesNotLookUpGroupOrSessions(
        string userId,
        Reservation reservation)
    {
        _mockReservationRepository.Setup(r => r.GetReservationByIdAsync(reservation.Id)).ReturnsAsync(reservation);

        await Assert.ThrowsAsync<ActionForbiddenException>(() =>
            _guestSessionService.GetOrCreateOwnerSessionIdAsync(userId, reservation.Id));

        _mockGroupReservationRepository.Verify(r => r.GetByReservationIdAsync(It.IsAny<string>()), Times.Never);
        _mockGuestSessionRepository.Verify(r => r.GetAllByGroupReservationIdAsync(It.IsAny<string>()), Times.Never);
        _mockGuestSessionRepository.Verify(r => r.GetByGuestSessionId(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task GetOrCreateOwnerSessionIdAsync_GroupReservationNotFound_DoesNotCreateSession(
        string userId,
        Reservation reservation)
    {
        reservation.CustomerId = userId;
        _mockReservationRepository.Setup(r => r.GetReservationByIdAsync(reservation.Id)).ReturnsAsync(reservation);
        _mockGroupReservationRepository.Setup(r => r.GetByReservationIdAsync(reservation.Id)).ReturnsAsync((GroupReservation?)null);

        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() =>
            _guestSessionService.GetOrCreateOwnerSessionIdAsync(userId, reservation.Id));

        _mockGuestSessionRepository.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task GetOrCreateOwnerSessionIdAsync_OwnerSessionNotFoundById_CreatesOwnerSessionWithoutQueryingGroupIndex(
        string userId,
        Reservation reservation,
        GroupReservation groupReservation)
    {
        reservation.CustomerId = userId;
        groupReservation.ReservationId = reservation.Id;

        _mockReservationRepository.Setup(r => r.GetReservationByIdAsync(reservation.Id)).ReturnsAsync(reservation);
        _mockGroupReservationRepository.Setup(r => r.GetByReservationIdAsync(reservation.Id)).ReturnsAsync(groupReservation);
        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(It.IsAny<string>()))
            .ReturnsAsync((GuestSession?)null);

        GuestSession? saved = null;
        _mockGuestSessionRepository
            .Setup(r => r.SaveAsync(It.IsAny<GuestSession>()))
            .Callback<GuestSession>(s => saved = s);

        var response = await _guestSessionService.GetOrCreateOwnerSessionIdAsync(userId, reservation.Id);

        Assert.NotNull(saved);
        Assert.True(saved.IsOwnerSession);
        Assert.False(saved.IsSubmitted);
        Assert.Equal($"owner-{groupReservation.GroupReservationId}", saved.Id);
        Assert.Equal(saved.Id, response.GuestSessionId);
        _mockGuestSessionRepository.Verify(r => r.GetByGuestSessionId($"owner-{groupReservation.GroupReservationId}"), Times.Once);
        _mockGuestSessionRepository.Verify(r => r.GetAllByGroupReservationIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task GetOrCreateOwnerSessionIdAsync_CalledTwice_ReturnsSameIdAndCreatesOnce(
        string userId,
        Reservation reservation,
        GroupReservation groupReservation)
    {
        reservation.CustomerId = userId;
        groupReservation.ReservationId = reservation.Id;
        var stored = new Dictionary<string, GuestSession>();

        _mockReservationRepository.Setup(r => r.GetReservationByIdAsync(reservation.Id)).ReturnsAsync(reservation);
        _mockGroupReservationRepository.Setup(r => r.GetByReservationIdAsync(reservation.Id)).ReturnsAsync(groupReservation);
        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(It.IsAny<string>()))
            .ReturnsAsync((string id) => stored.GetValueOrDefault(id));
        _mockGuestSessionRepository
            .Setup(r => r.SaveAsync(It.IsAny<GuestSession>()))
            .Callback<GuestSession>(session => stored[session.Id] = session);

        var first = await _guestSessionService.GetOrCreateOwnerSessionIdAsync(userId, reservation.Id);
        var second = await _guestSessionService.GetOrCreateOwnerSessionIdAsync(userId, reservation.Id);

        Assert.Equal(first.GuestSessionId, second.GuestSessionId);
        _mockGuestSessionRepository.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Once);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupReservationStatusAsync_NoSessions_ReturnsOnlyOwner(GroupReservation groupReservation, Reservation reservation)
    {
        groupReservation.ReservationId = reservation.Id;

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);
        _mockReservationRepository.Setup(r => r.GetReservationByIdAsync(reservation.Id)).ReturnsAsync(reservation);
        _mockGuestSessionRepository
            .Setup(r => r.GetAllByGroupReservationIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync((List<GuestSession>)null!);

        var response = await _guestSessionService.GetGroupReservationStatusAsync(groupReservation.GroupReservationId);

        Assert.Equal(1, response.CurrentParticipants);
        Assert.Equal(reservation.GuestCount, response.MaxCapacity);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupReservationStatusAsync_OnlyOwnerSession_ReturnsOne(
        GroupReservation groupReservation,
        Reservation reservation,
        GuestSession ownerSession)
    {
        groupReservation.ReservationId = reservation.Id;
        ownerSession.IsOwnerSession = true;

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);
        _mockReservationRepository.Setup(r => r.GetReservationByIdAsync(reservation.Id)).ReturnsAsync(reservation);
        _mockGuestSessionRepository
            .Setup(r => r.GetAllByGroupReservationIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync([ownerSession]);

        var response = await _guestSessionService.GetGroupReservationStatusAsync(groupReservation.GroupReservationId);

        Assert.Equal(1, response.CurrentParticipants);
    }

    // CountByGroupReservationId excludes the owner session (see GuestSessionRepositoryTests), so with an existing
    // owner session and GuestCount = 3, one joined guest (count = 1) still leaves room for one more guest.
    [Theory]
    [InlineAutoData(3, 0)]
    [InlineAutoData(3, 1)]
    [InlineAutoData(4, 2)]
    public async Task CreateGuestSessionAsync_OwnerSessionExistsAndBelowLimit_CreatesSession(
        int reservationCapacity,
        int joinedGuestsExcludingOwner,
        CreateGuestSessionRequest request,
        GroupReservation groupReservation,
        Reservation parentReservation)
    {
        groupReservation.GroupReservationId = request.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;
        groupReservation.ReservationId = parentReservation.Id;
        parentReservation.GuestCount = reservationCapacity;
        parentReservation.Status = APIHandler.Constants.ReservationStatus.Reserved;

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);
        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(parentReservation.Id))
            .ReturnsAsync(parentReservation);
        _mockGuestSessionRepository
            .Setup(r => r.CountByGroupReservationId(groupReservation.GroupReservationId))
            .ReturnsAsync(joinedGuestsExcludingOwner);

        var response = await _guestSessionService.CreateGuestSessionAsync(request);

        Assert.False(string.IsNullOrEmpty(response.SessionId));
        _mockGuestSessionRepository.Verify(r => r.SaveAsync(It.Is<GuestSession>(s =>
            !s.IsOwnerSession && s.GroupReservationId == groupReservation.GroupReservationId)), Times.Once);
    }

    [Theory]
    [InlineAutoData(3, 2)]
    [InlineAutoData(4, 3)]
    public async Task CreateGuestSessionAsync_OwnerSessionExistsAndLimitReached_ThrowsGuestLimitExceededException(
        int reservationCapacity,
        int joinedGuestsExcludingOwner,
        CreateGuestSessionRequest request,
        GroupReservation groupReservation,
        Reservation parentReservation)
    {
        groupReservation.GroupReservationId = request.GroupReservationId;
        groupReservation.Status = GroupReservationStatus.Open;
        groupReservation.ReservationId = parentReservation.Id;
        parentReservation.GuestCount = reservationCapacity;
        parentReservation.Status = APIHandler.Constants.ReservationStatus.Reserved;

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);
        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(parentReservation.Id))
            .ReturnsAsync(parentReservation);
        _mockGuestSessionRepository
            .Setup(r => r.CountByGroupReservationId(groupReservation.GroupReservationId))
            .ReturnsAsync(joinedGuestsExcludingOwner);

        await Assert.ThrowsAsync<GuestLimitExceededException>(() => _guestSessionService.CreateGuestSessionAsync(request));

        _mockGuestSessionRepository.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }
}





