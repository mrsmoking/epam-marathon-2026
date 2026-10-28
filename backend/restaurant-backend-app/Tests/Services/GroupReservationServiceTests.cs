using APIHandler.Constants;
using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Models.GroupReservation;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Orders;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using APIHandler.Services.Auth;
using APIHandler.Services.GroupReservations;
using APIHandler.Services.Locations;
using AutoFixture.Xunit2;
using Moq;

namespace APIHandler.Tests.Services;

public class GroupReservationServiceTests
{
    private readonly Mock<IGroupReservationRepository> _groupReservationRepositoryMock = new();
    private readonly Mock<ITableRepository> _tableRepositoryMock = new();
    private readonly Mock<IReservationRepository> _reservationRepositoryMock = new();
    private readonly Mock<IGuestSessionRepository> _guestSessionRepositoryMock = new();
    private readonly Mock<IUserService> _userServiceMock = new();
    private readonly Mock<ILocationService> _locationServiceMock = new();
    private readonly Mock<IOrderRepository> _orderRepositoryMock = new();

    private readonly GroupReservationService _service;

    public GroupReservationServiceTests()
    {
        _service = new GroupReservationService(
            _groupReservationRepositoryMock.Object,
            _tableRepositoryMock.Object,
            _reservationRepositoryMock.Object,
            _guestSessionRepositoryMock.Object,
            _userServiceMock.Object,
            _locationServiceMock.Object,
            _orderRepositoryMock.Object);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupReservationByTokenAsync_ValidInput_ReturnsGroupReservationInfo(
        GroupReservation groupReservation,
        Table table,
        Reservation reservation,
        Location location,
        string expectedUserName)
    {
        groupReservation.ReservationId = reservation.Id;
        groupReservation.Status = GroupReservationStatus.Open;
        reservation.TableId = table.Id;
        table.LocationId = location.Id;

        _groupReservationRepositoryMock
            .Setup(r => r.GetGroupReservationByTokenAsync(groupReservation.ReservationToken))
            .ReturnsAsync(groupReservation);

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        _guestSessionRepositoryMock
            .Setup(r => r.CountByGroupReservationId(groupReservation.GroupReservationId))
            .ReturnsAsync(0);

        _tableRepositoryMock
            .Setup(r => r.GetTableAsync(table.Id))
            .ReturnsAsync(table);

        _locationServiceMock
            .Setup(s => s.GetLocationByIdAsync(table.LocationId))
            .ReturnsAsync(location);

        _userServiceMock
            .Setup(s => s.GetUserName(reservation.CustomerId!))
            .ReturnsAsync(expectedUserName);

        var result = await _service.GetGroupReservationByTokenAsync(groupReservation.ReservationToken);

        Assert.Equal(groupReservation.GroupReservationId, result.GroupReservationId);
        Assert.Equal(table.LocationId, result.LocationId);
        Assert.Equal(reservation.GuestCount, result.MaxGuestCount);
        Assert.Equal(reservation.StartTime, result.StartTime);
        Assert.Equal(reservation.EndTime, result.EndTime);
        Assert.Equal(table.TableNumber, result.TableNumber);
        Assert.Equal("Green & Tasty", result.RestaurantName);
        Assert.Equal(location.Address, result.LocationAddress);
        Assert.Equal(expectedUserName, result.InviterName);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupReservationByTokenAsync_InvalidToken_ThrowsNotFound(string token)
    {
        _groupReservationRepositoryMock
            .Setup(r => r.GetGroupReservationByTokenAsync(token))
            .ReturnsAsync((GroupReservation?)null);

        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() => _service.GetGroupReservationByTokenAsync(token));
    }

    [Theory]
    [InlineAutoData(GroupReservationStatus.Closed)]
    [InlineAutoData(GroupReservationStatus.Submitted)]
    public async Task GetGroupReservationByTokenAsync_GroupReservationIsNotOpen_ThrowsException(
        GroupReservationStatus status,
        GroupReservation groupReservation)
    {
        groupReservation.Status = status;

        _groupReservationRepositoryMock
            .Setup(r => r.GetGroupReservationByTokenAsync(groupReservation.ReservationToken))
            .ReturnsAsync(groupReservation);

        await Assert.ThrowsAsync<GroupReservationExpiredException>(() => _service.GetGroupReservationByTokenAsync(groupReservation.ReservationToken));
    }

    [Theory]
    [AutoData]
    public async Task GetGroupReservationByTokenAsync_WhenGuestCountIsMax_ThrowsGuestLimitExceeded(GroupReservation groupReservation, Reservation reservation)
    {
        groupReservation.Status = GroupReservationStatus.Open;
        groupReservation.ReservationId = reservation.Id;

        _groupReservationRepositoryMock
            .Setup(r => r.GetGroupReservationByTokenAsync(groupReservation.ReservationToken))
            .ReturnsAsync(groupReservation);

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        _guestSessionRepositoryMock
            .Setup(r => r.CountByGroupReservationId(groupReservation.GroupReservationId))
            .ReturnsAsync(reservation.GuestCount);

        await Assert.ThrowsAsync<GuestLimitExceededException>(() => _service.GetGroupReservationByTokenAsync(groupReservation.ReservationToken));
    }

    [Theory]
    [AutoData]
    public async Task CloseGroupReservationAsync_ValidInput_UpdatesGroupReservationStatus(GroupReservation groupReservation)
    {
        _groupReservationRepositoryMock
            .Setup(r => r.GetByReservationIdAsync(groupReservation.ReservationId))
            .ReturnsAsync(groupReservation);

        GroupReservation? savedGroupReservation = null;
        _groupReservationRepositoryMock
            .Setup(r => r.SaveGroupReservationAsync(It.IsAny<GroupReservation>()))
            .Callback<GroupReservation>(r => savedGroupReservation = r);

        await _service.CloseGroupReservationAsync(groupReservation.ReservationId);

        Assert.NotNull(savedGroupReservation);
        Assert.Equal(GroupReservationStatus.Closed, savedGroupReservation.Status);
    }

    [Theory]
    [AutoData]
    public async Task CloseGroupReservationAsync_GroupReservationNotFound_DoesNotThrowException(string reservationId)
    {
        _groupReservationRepositoryMock
            .Setup(r => r.GetByReservationIdAsync(reservationId))
            .ReturnsAsync((GroupReservation?)null);

        await _service.CloseGroupReservationAsync(reservationId);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupReservationInviteTokenAsync_ValidInput_ReturnsToken(Reservation reservation, GroupReservation groupReservation)
    {
        reservation.Status = ReservationStatus.Reserved;
        reservation.GuestCount = 3;
        groupReservation.ReservationId = reservation.Id;
        groupReservation.Status = GroupReservationStatus.Open;

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        _groupReservationRepositoryMock
            .Setup(r => r.GetByReservationIdAsync(reservation.Id))
            .ReturnsAsync(groupReservation);

        GetGroupInviteTokenResponse result = await _service.GetGroupReservationInviteTokenAsync(reservation.Id);

        Assert.Equal(groupReservation.ReservationToken, result.Token);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupReservationInviteTokenAsync_InvalidReservationId_ThrowsNotFound(string reservationId)
    {
        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservationId))
            .ReturnsAsync((Reservation?)null);

        await Assert.ThrowsAsync<ReservationNotFoundException>(() => _service.GetGroupReservationInviteTokenAsync(reservationId));
    }

    [Theory]
    [AutoData]
    public async Task GetGroupReservationInviteTokenAsync_OneGuest_ThrowsValidationException(Reservation reservation)
    {
        reservation.Status = ReservationStatus.Reserved;
        reservation.GuestCount = 1;

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        await Assert.ThrowsAsync<ValidationException>(() => _service.GetGroupReservationInviteTokenAsync(reservation.Id));
    }

    [Theory]
    [InlineAutoData(ReservationStatus.Completed)]
    [InlineAutoData(ReservationStatus.Cancelled)]
    [InlineAutoData(ReservationStatus.InProgress)]
    public async Task GetGroupReservationInviteTokenAsync_StatusIsNotReserved_ThrowsValidationException(ReservationStatus reservationStatus, Reservation reservation)
    {
        reservation.Status = reservationStatus;
        reservation.GuestCount = 1;

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        await Assert.ThrowsAsync<ValidationException>(() => _service.GetGroupReservationInviteTokenAsync(reservation.Id));
    }

    [Theory]
    [InlineAutoData(GroupReservationStatus.Closed, typeof(GroupReservationExpiredException))]
    [InlineAutoData(GroupReservationStatus.Submitted, typeof(GroupReservationSubmittedException))]
    public async Task GetGroupReservationInviteTokenAsync_GroupStatusIsNotOpen_ThrowsException(GroupReservationStatus status, Type type, Reservation reservation, GroupReservation groupReservation)
    {
        reservation.Status = ReservationStatus.Reserved;
        reservation.GuestCount = 3;
        groupReservation.ReservationId = reservation.Id;
        groupReservation.Status = status;

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        _groupReservationRepositoryMock
            .Setup(r => r.GetByReservationIdAsync(reservation.Id))
            .ReturnsAsync(groupReservation);

        await Assert.ThrowsAsync(type, () => _service.GetGroupReservationInviteTokenAsync(reservation.Id));
    }

    private static GuestSession CreateSession(string groupReservationId, bool isSubmitted, params (string DishId, int Quantity)[] dishes) => new()
    {
        Id = Guid.NewGuid().ToString(),
        GroupReservationId = groupReservationId,
        GuestName = "Guest",
        IsSubmitted = isSubmitted,
        Dishes = dishes.Select(d => new OrderItem { DishId = d.DishId, Quantity = d.Quantity }).ToList(),
    };

    private static Order CreateOrder(string reservationId, string customerId, List<OrderItem> dishes) => new()
    {
        OrderId = Guid.NewGuid().ToString(),
        CustomerId = customerId,
        ReservationId = reservationId,
        ReservationAddress = "Address",
        ReservationTime = "Time",
        Dishes = dishes,
        Status = OrderStatus.Created,
    };

    private void SetupGroupAndReservation(GroupReservation groupReservation, Reservation reservation, string userId)
    {
        groupReservation.ReservationId = reservation.Id;
        groupReservation.Status = GroupReservationStatus.Open;
        reservation.CustomerId = userId;

        _groupReservationRepositoryMock
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        _groupReservationRepositoryMock
            .Setup(r => r.TryUpdateStatusAsync(groupReservation.GroupReservationId, GroupReservationStatus.Open, GroupReservationStatus.Submitted))
            .ReturnsAsync(true);
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_ValidInput_MergesSubmittedSessionsIntoOrderAndSubmits(
        GroupReservation groupReservation,
        Reservation reservation,
        string userId)
    {
        SetupGroupAndReservation(groupReservation, reservation, userId);
        string groupId = groupReservation.GroupReservationId;

        var sessions = new List<GuestSession>
        {
            CreateSession(groupId, true, ("dish1", 1), ("dish2", 2)),
            CreateSession(groupId, true, ("dish1", 2), ("dish3", 0)),
            CreateSession(groupId, false, ("dish1", 10), ("dish4", 5)),
        };
        _guestSessionRepositoryMock
            .Setup(r => r.GetAllByGroupReservationIdAsync(groupId))
            .ReturnsAsync(sessions);

        var order = CreateOrder(reservation.Id, userId,
        [
            new OrderItem { DishId = "dish2", Quantity = 1, State = OrderItemState.Confirmed },
            new OrderItem { DishId = "dish5", Quantity = 4, State = OrderItemState.Pending },
        ]);
        _orderRepositoryMock
            .Setup(r => r.GetOrderByReservationIdAsync(reservation.Id))
            .ReturnsAsync(order);

        var calls = new List<string>();
        _groupReservationRepositoryMock
            .Setup(r => r.TryUpdateStatusAsync(groupId, GroupReservationStatus.Open, GroupReservationStatus.Submitted))
            .Callback(() => calls.Add("claim"))
            .ReturnsAsync(true);
        _orderRepositoryMock
            .Setup(r => r.SaveOrderAsync(order))
            .Callback(() => calls.Add("order"))
            .Returns(Task.CompletedTask);

        var result = await _service.SubmitGroupPreOrderAsync(userId, groupId, reservation.Id);

        Assert.Equal(2, result.Count);
        Assert.Equal(new GroupPreOrderItemResponse("dish1", 3), result[0]);
        Assert.Equal(new GroupPreOrderItemResponse("dish2", 2), result[1]);

        Assert.Equal(3, order.Dishes.Count);
        Assert.Equal(3, order.Dishes.Single(d => d.DishId == "dish2").Quantity);
        Assert.Equal(OrderItemState.Pending, order.Dishes.Single(d => d.DishId == "dish2").State);
        Assert.Equal(4, order.Dishes.Single(d => d.DishId == "dish5").Quantity);
        var added = order.Dishes.Single(d => d.DishId == "dish1");
        Assert.Equal(3, added.Quantity);
        Assert.Equal(OrderItemState.Pending, added.State);
        Assert.DoesNotContain(order.Dishes, d => d.DishId == "dish4");

        Assert.Equal(OrderStatus.SubmittedByCustomer, order.Status);
        Assert.Equal(["claim", "order"], calls);
        _groupReservationRepositoryMock.Verify(r => r.SaveGroupReservationAsync(It.IsAny<GroupReservation>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_NoSubmittedSessions_ThrowsValidationException(
        GroupReservation groupReservation,
        Reservation reservation,
        string userId)
    {
        SetupGroupAndReservation(groupReservation, reservation, userId);
        string groupId = groupReservation.GroupReservationId;

        _guestSessionRepositoryMock
            .Setup(r => r.GetAllByGroupReservationIdAsync(groupId))
            .ReturnsAsync([CreateSession(groupId, false, ("dish1", 1))]);

        await Assert.ThrowsAsync<ValidationException>(() => _service.SubmitGroupPreOrderAsync(userId, groupId, reservation.Id));

        _orderRepositoryMock.Verify(r => r.SaveOrderAsync(It.IsAny<Order>()), Times.Never);
        _groupReservationRepositoryMock.Verify(r => r.SaveGroupReservationAsync(It.IsAny<GroupReservation>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_UserIsNotOwner_ThrowsActionForbiddenException(
        GroupReservation groupReservation,
        Reservation reservation,
        string ownerId,
        string otherUserId)
    {
        SetupGroupAndReservation(groupReservation, reservation, ownerId);

        await Assert.ThrowsAsync<ActionForbiddenException>(() =>
            _service.SubmitGroupPreOrderAsync(otherUserId, groupReservation.GroupReservationId, reservation.Id));

        _orderRepositoryMock.Verify(r => r.SaveOrderAsync(It.IsAny<Order>()), Times.Never);
        _groupReservationRepositoryMock.Verify(r => r.SaveGroupReservationAsync(It.IsAny<GroupReservation>()), Times.Never);
    }

    private void VerifyNothingSaved()
    {
        _orderRepositoryMock.Verify(r => r.SaveOrderAsync(It.IsAny<Order>()), Times.Never);
        _groupReservationRepositoryMock.Verify(r => r.SaveGroupReservationAsync(It.IsAny<GroupReservation>()), Times.Never);
        _groupReservationRepositoryMock.Verify(r => r.TryUpdateStatusAsync(
            It.IsAny<string>(), It.IsAny<GroupReservationStatus>(), It.IsAny<GroupReservationStatus>()), Times.Never);
    }

    private void SetupSessions(string groupId, params GuestSession[] sessions) =>
        _guestSessionRepositoryMock
            .Setup(r => r.GetAllByGroupReservationIdAsync(groupId))
            .ReturnsAsync(sessions.ToList());

    private Order SetupOrder(Reservation reservation, string userId, List<OrderItem> dishes, OrderStatus status = OrderStatus.Created)
    {
        var order = CreateOrder(reservation.Id, userId, dishes);
        order.Status = status;
        _orderRepositoryMock
            .Setup(r => r.GetOrderByReservationIdAsync(reservation.Id))
            .ReturnsAsync(order);
        return order;
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_GroupReservationNotFound_ThrowsGroupReservationNotFoundException(
        string userId,
        string groupId,
        string reservationId)
    {
        _groupReservationRepositoryMock
            .Setup(r => r.GetGroupReservationByIdAsync(groupId))
            .ReturnsAsync((GroupReservation?)null);

        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() =>
            _service.SubmitGroupPreOrderAsync(userId, groupId, reservationId));

        _reservationRepositoryMock.Verify(r => r.GetReservationByIdAsync(It.IsAny<string>()), Times.Never);
        VerifyNothingSaved();
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_ReservationNotFound_ThrowsReservationNotFoundException(
        GroupReservation groupReservation,
        string userId)
    {
        groupReservation.Status = GroupReservationStatus.Open;
        _groupReservationRepositoryMock
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);
        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(groupReservation.ReservationId))
            .ReturnsAsync((Reservation?)null);

        await Assert.ThrowsAsync<ReservationNotFoundException>(() =>
            _service.SubmitGroupPreOrderAsync(userId, groupReservation.GroupReservationId, groupReservation.ReservationId));

        _guestSessionRepositoryMock.Verify(r => r.GetAllByGroupReservationIdAsync(It.IsAny<string>()), Times.Never);
        VerifyNothingSaved();
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_ReservationIdMismatch_ThrowsValidationException(
        GroupReservation groupReservation,
        Reservation reservation,
        string userId,
        string otherReservationId)
    {
        SetupGroupAndReservation(groupReservation, reservation, userId);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.SubmitGroupPreOrderAsync(userId, groupReservation.GroupReservationId, otherReservationId));

        Assert.Equal("Reservation ID does not match the group reservation.", ex.Message);
        _guestSessionRepositoryMock.Verify(r => r.GetAllByGroupReservationIdAsync(It.IsAny<string>()), Times.Never);
        _orderRepositoryMock.Verify(r => r.GetOrderByReservationIdAsync(It.IsAny<string>()), Times.Never);
        VerifyNothingSaved();
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_GroupAlreadySubmitted_ThrowsGroupReservationSubmittedException(
        GroupReservation groupReservation,
        Reservation reservation,
        string userId)
    {
        SetupGroupAndReservation(groupReservation, reservation, userId);
        groupReservation.Status = GroupReservationStatus.Submitted;

        await Assert.ThrowsAsync<GroupReservationSubmittedException>(() =>
            _service.SubmitGroupPreOrderAsync(userId, groupReservation.GroupReservationId, reservation.Id));

        _guestSessionRepositoryMock.Verify(r => r.GetAllByGroupReservationIdAsync(It.IsAny<string>()), Times.Never);
        VerifyNothingSaved();
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_GroupClosed_ThrowsGroupReservationExpiredException(
        GroupReservation groupReservation,
        Reservation reservation,
        string userId)
    {
        SetupGroupAndReservation(groupReservation, reservation, userId);
        groupReservation.Status = GroupReservationStatus.Closed;

        await Assert.ThrowsAsync<GroupReservationExpiredException>(() =>
            _service.SubmitGroupPreOrderAsync(userId, groupReservation.GroupReservationId, reservation.Id));

        _guestSessionRepositoryMock.Verify(r => r.GetAllByGroupReservationIdAsync(It.IsAny<string>()), Times.Never);
        VerifyNothingSaved();
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_NoSessionsAtAll_ThrowsValidationException(
        GroupReservation groupReservation,
        Reservation reservation,
        string userId)
    {
        SetupGroupAndReservation(groupReservation, reservation, userId);
        SetupSessions(groupReservation.GroupReservationId);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.SubmitGroupPreOrderAsync(userId, groupReservation.GroupReservationId, reservation.Id));

        Assert.Equal("There are no submitted guest sessions in the group.", ex.Message);
        VerifyNothingSaved();
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_SubmittedSessionsWithoutValidDishes_ThrowsValidationException(
        GroupReservation groupReservation,
        Reservation reservation,
        string userId)
    {
        SetupGroupAndReservation(groupReservation, reservation, userId);
        string groupId = groupReservation.GroupReservationId;
        var nullDishesSession = CreateSession(groupId, true);
        nullDishesSession.Dishes = null!;
        SetupSessions(groupId,
            CreateSession(groupId, true),
            CreateSession(groupId, true, ("dish1", 0), ("dish2", -3)),
            CreateSession(groupId, true, ("", 2), ("   ", 1)),
            nullDishesSession,
            CreateSession(groupId, false, ("dish3", 5)));

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.SubmitGroupPreOrderAsync(userId, groupId, reservation.Id));

        Assert.Equal("Submitted guest sessions contain no dishes.", ex.Message);
        _orderRepositoryMock.Verify(r => r.GetOrderByReservationIdAsync(It.IsAny<string>()), Times.Never);
        VerifyNothingSaved();
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_OrderMissing_ThrowsValidationException(
        GroupReservation groupReservation,
        Reservation reservation,
        string userId)
    {
        SetupGroupAndReservation(groupReservation, reservation, userId);
        string groupId = groupReservation.GroupReservationId;
        SetupSessions(groupId, CreateSession(groupId, true, ("dish1", 1)));
        _orderRepositoryMock
            .Setup(r => r.GetOrderByReservationIdAsync(reservation.Id))
            .ReturnsAsync((Order?)null);

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.SubmitGroupPreOrderAsync(userId, groupId, reservation.Id));

        Assert.Equal(GroupReservationStatus.Open, groupReservation.Status);
        VerifyNothingSaved();
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_OrderCancelled_ThrowsValidationExceptionAndLeavesOrderUntouched(
        GroupReservation groupReservation,
        Reservation reservation,
        string userId)
    {
        SetupGroupAndReservation(groupReservation, reservation, userId);
        string groupId = groupReservation.GroupReservationId;
        SetupSessions(groupId, CreateSession(groupId, true, ("dish1", 1)));
        var order = SetupOrder(reservation, userId,
            [new OrderItem { DishId = "dish1", Quantity = 2, State = OrderItemState.Pending }],
            OrderStatus.Cancelled);

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.SubmitGroupPreOrderAsync(userId, groupId, reservation.Id));

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(2, Assert.Single(order.Dishes).Quantity);
        Assert.Equal(GroupReservationStatus.Open, groupReservation.Status);
        VerifyNothingSaved();
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_ExistingItemCanceled_RevivesLineAsPendingWithNewQuantity(
        GroupReservation groupReservation,
        Reservation reservation,
        string userId)
    {
        SetupGroupAndReservation(groupReservation, reservation, userId);
        string groupId = groupReservation.GroupReservationId;
        SetupSessions(groupId, CreateSession(groupId, true, ("dish1", 2)));
        var canceled = new OrderItem { DishId = "dish1", Quantity = 5, State = OrderItemState.Canceled };
        var order = SetupOrder(reservation, userId, [canceled]);

        await _service.SubmitGroupPreOrderAsync(userId, groupId, reservation.Id);

        var line = Assert.Single(order.Dishes);
        Assert.Same(canceled, line);
        Assert.Equal(2, line.Quantity);
        Assert.Equal(OrderItemState.Pending, line.State);
        _orderRepositoryMock.Verify(r => r.SaveOrderAsync(order), Times.Once);
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_ExistingItemConfirmed_SumsQuantityAndResetsToPending(
        GroupReservation groupReservation,
        Reservation reservation,
        string userId)
    {
        SetupGroupAndReservation(groupReservation, reservation, userId);
        string groupId = groupReservation.GroupReservationId;
        SetupSessions(groupId, CreateSession(groupId, true, ("dish1", 2)));
        var confirmed = new OrderItem { DishId = "dish1", Quantity = 3, State = OrderItemState.Confirmed };
        var order = SetupOrder(reservation, userId, [confirmed]);

        await _service.SubmitGroupPreOrderAsync(userId, groupId, reservation.Id);

        var line = Assert.Single(order.Dishes);
        Assert.Equal(5, line.Quantity);
        Assert.Equal(OrderItemState.Pending, line.State);
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_ExistingItemPending_SumsQuantityAndKeepsPending(
        GroupReservation groupReservation,
        Reservation reservation,
        string userId)
    {
        SetupGroupAndReservation(groupReservation, reservation, userId);
        string groupId = groupReservation.GroupReservationId;
        SetupSessions(groupId, CreateSession(groupId, true, ("dish1", 2)));
        var pending = new OrderItem { DishId = "dish1", Quantity = 1, State = OrderItemState.Pending };
        var order = SetupOrder(reservation, userId, [pending]);

        await _service.SubmitGroupPreOrderAsync(userId, groupId, reservation.Id);

        var line = Assert.Single(order.Dishes);
        Assert.Equal(3, line.Quantity);
        Assert.Equal(OrderItemState.Pending, line.State);
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_DuplicateLinesForSameDish_UpdatesFirstLineOnly(
        GroupReservation groupReservation,
        Reservation reservation,
        string userId)
    {
        SetupGroupAndReservation(groupReservation, reservation, userId);
        string groupId = groupReservation.GroupReservationId;
        SetupSessions(groupId, CreateSession(groupId, true, ("dish1", 2)));
        var canceled = new OrderItem { DishId = "dish1", Quantity = 5, State = OrderItemState.Canceled };
        var active = new OrderItem { DishId = "dish1", Quantity = 1, State = OrderItemState.Pending };
        var order = SetupOrder(reservation, userId, [canceled, active]);

        await _service.SubmitGroupPreOrderAsync(userId, groupId, reservation.Id);

        // Matches the waiter confirm/cancel lookup (first line by DishId); no extra line is added.
        Assert.Equal(2, order.Dishes.Count);
        Assert.Equal(2, canceled.Quantity);
        Assert.Equal(OrderItemState.Pending, canceled.State);
        Assert.Equal(1, active.Quantity);
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_NonPositiveQuantitiesAndEmptyDishIds_AreIgnored(
        GroupReservation groupReservation,
        Reservation reservation,
        string userId)
    {
        SetupGroupAndReservation(groupReservation, reservation, userId);
        string groupId = groupReservation.GroupReservationId;
        SetupSessions(groupId,
            CreateSession(groupId, true, ("dish1", 3), ("dish2", 0), ("", 4)),
            CreateSession(groupId, true, ("dish1", -2), ("dish3", -1), ("dish2", 1)));
        var order = SetupOrder(reservation, userId, []);

        var result = await _service.SubmitGroupPreOrderAsync(userId, groupId, reservation.Id);

        Assert.Equal(
            [new GroupPreOrderItemResponse("dish1", 3), new GroupPreOrderItemResponse("dish2", 1)],
            result);
        Assert.Equal(2, order.Dishes.Count);
        Assert.DoesNotContain(order.Dishes, d => d.DishId == "dish3" || d.DishId == "");
        Assert.All(order.Dishes, d => Assert.True(d.Quantity > 0));
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_DishesFromSeveralSessions_ReturnedInFirstAppearanceOrder(
        GroupReservation groupReservation,
        Reservation reservation,
        string userId)
    {
        SetupGroupAndReservation(groupReservation, reservation, userId);
        string groupId = groupReservation.GroupReservationId;
        SetupSessions(groupId,
            CreateSession(groupId, true, ("dishC", 1), ("dishA", 1)),
            CreateSession(groupId, true, ("dishB", 2), ("dishC", 4)));
        SetupOrder(reservation, userId, []);

        var result = await _service.SubmitGroupPreOrderAsync(userId, groupId, reservation.Id);

        Assert.Equal(["dishC", "dishA", "dishB"], result.Select(r => r.DishId));
        Assert.Equal([5, 1, 2], result.Select(r => r.Quantity));
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_SubmittedOwnerSession_IsIncluded(
        GroupReservation groupReservation,
        Reservation reservation,
        string userId)
    {
        SetupGroupAndReservation(groupReservation, reservation, userId);
        string groupId = groupReservation.GroupReservationId;
        var ownerSession = CreateSession(groupId, true, ("dish1", 2));
        ownerSession.IsOwnerSession = true;
        ownerSession.OwnerUserId = userId;
        SetupSessions(groupId, ownerSession, CreateSession(groupId, true, ("dish1", 1)));
        SetupOrder(reservation, userId, []);

        var result = await _service.SubmitGroupPreOrderAsync(userId, groupId, reservation.Id);

        Assert.Equal(new GroupPreOrderItemResponse("dish1", 3), Assert.Single(result));
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_OrderDishesNull_CreatesListWithPendingItems(
        GroupReservation groupReservation,
        Reservation reservation,
        string userId)
    {
        SetupGroupAndReservation(groupReservation, reservation, userId);
        string groupId = groupReservation.GroupReservationId;
        SetupSessions(groupId, CreateSession(groupId, true, ("dish1", 2)));
        var order = SetupOrder(reservation, userId, []);
        order.Dishes = null!;

        await _service.SubmitGroupPreOrderAsync(userId, groupId, reservation.Id);

        var item = Assert.Single(order.Dishes);
        Assert.Equal("dish1", item.DishId);
        Assert.Equal(2, item.Quantity);
        Assert.Equal(OrderItemState.Pending, item.State);
    }

    [Theory]
    [InlineAutoData(OrderStatus.Created)]
    [InlineAutoData(OrderStatus.Submitted)]
    [InlineAutoData(OrderStatus.SubmittedByCustomer)]
    public async Task SubmitGroupPreOrderAsync_ActiveOrderStatus_ClaimsGroupAndSavesOrderAsSubmittedByCustomer(
        OrderStatus initialStatus,
        GroupReservation groupReservation,
        Reservation reservation,
        string userId)
    {
        SetupGroupAndReservation(groupReservation, reservation, userId);
        string groupId = groupReservation.GroupReservationId;
        SetupSessions(groupId, CreateSession(groupId, true, ("dish1", 1)));
        SetupOrder(reservation, userId, [], initialStatus);

        await _service.SubmitGroupPreOrderAsync(userId, groupId, reservation.Id);

        _orderRepositoryMock.Verify(r => r.SaveOrderAsync(It.Is<Order>(o =>
            o.Status == OrderStatus.SubmittedByCustomer && o.ReservationId == reservation.Id)), Times.Once);
        _groupReservationRepositoryMock.Verify(r => r.TryUpdateStatusAsync(
            groupId, GroupReservationStatus.Open, GroupReservationStatus.Submitted), Times.Once);
        _groupReservationRepositoryMock.Verify(r => r.SaveGroupReservationAsync(It.IsAny<GroupReservation>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_ClaimFails_ThrowsGroupReservationSubmittedAndDoesNotSaveOrder(
        GroupReservation groupReservation,
        Reservation reservation,
        string userId)
    {
        SetupGroupAndReservation(groupReservation, reservation, userId);
        string groupId = groupReservation.GroupReservationId;
        SetupSessions(groupId, CreateSession(groupId, true, ("dish1", 2)));
        var existing = new OrderItem { DishId = "dish1", Quantity = 1, State = OrderItemState.Confirmed };
        var order = SetupOrder(reservation, userId, [existing]);
        _groupReservationRepositoryMock
            .Setup(r => r.TryUpdateStatusAsync(groupId, GroupReservationStatus.Open, GroupReservationStatus.Submitted))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<GroupReservationSubmittedException>(() =>
            _service.SubmitGroupPreOrderAsync(userId, groupId, reservation.Id));

        Assert.Equal(1, existing.Quantity);
        Assert.Equal(OrderItemState.Confirmed, existing.State);
        Assert.NotEqual(OrderStatus.SubmittedByCustomer, order.Status);
        _orderRepositoryMock.Verify(r => r.SaveOrderAsync(It.IsAny<Order>()), Times.Never);
        _groupReservationRepositoryMock.Verify(r => r.TryUpdateStatusAsync(
            groupId, GroupReservationStatus.Submitted, GroupReservationStatus.Open), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_OrderSaveFails_RevertsGroupToOpenAndRethrows(
        GroupReservation groupReservation,
        Reservation reservation,
        string userId)
    {
        SetupGroupAndReservation(groupReservation, reservation, userId);
        string groupId = groupReservation.GroupReservationId;
        SetupSessions(groupId, CreateSession(groupId, true, ("dish1", 2)));
        SetupOrder(reservation, userId, []);
        var failure = new InvalidOperationException("DynamoDB is down");
        _orderRepositoryMock
            .Setup(r => r.SaveOrderAsync(It.IsAny<Order>()))
            .ThrowsAsync(failure);
        _groupReservationRepositoryMock
            .Setup(r => r.TryUpdateStatusAsync(groupId, GroupReservationStatus.Submitted, GroupReservationStatus.Open))
            .ReturnsAsync(true);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.SubmitGroupPreOrderAsync(userId, groupId, reservation.Id));

        Assert.Same(failure, thrown);
        _groupReservationRepositoryMock.Verify(r => r.TryUpdateStatusAsync(
            groupId, GroupReservationStatus.Submitted, GroupReservationStatus.Open), Times.Once);
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_OrderSaveAndRevertFail_RethrowsOriginalException(
        GroupReservation groupReservation,
        Reservation reservation,
        string userId)
    {
        SetupGroupAndReservation(groupReservation, reservation, userId);
        string groupId = groupReservation.GroupReservationId;
        SetupSessions(groupId, CreateSession(groupId, true, ("dish1", 2)));
        SetupOrder(reservation, userId, []);
        var failure = new InvalidOperationException("Order save failed");
        _orderRepositoryMock
            .Setup(r => r.SaveOrderAsync(It.IsAny<Order>()))
            .ThrowsAsync(failure);
        _groupReservationRepositoryMock
            .Setup(r => r.TryUpdateStatusAsync(groupId, GroupReservationStatus.Submitted, GroupReservationStatus.Open))
            .ThrowsAsync(new TimeoutException("Revert failed"));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.SubmitGroupPreOrderAsync(userId, groupId, reservation.Id));

        Assert.Same(failure, thrown);
    }
}
