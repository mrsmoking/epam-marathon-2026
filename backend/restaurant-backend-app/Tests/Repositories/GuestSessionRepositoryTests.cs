using Amazon.DynamoDBv2.DataModel;
using APIHandler.Entities;
using APIHandler.Repositories.GuestSessions;
using AutoFixture.Xunit2;
using Moq;

namespace APIHandler.Tests.Repositories;

public class GuestSessionRepositoryTests
{
    private readonly Mock<IDynamoDBContext> _mockContext = new();
    private readonly GuestSessionRepository _repository;

    public GuestSessionRepositoryTests()
    {
        Environment.SetEnvironmentVariable("guestSessionTable", "GuestSessions");
        _repository = new GuestSessionRepository(_mockContext.Object);
    }

    [Theory]
    [AutoData]
    public async Task CountByGroupReservationId_OwnerSessionPresent_ExcludesOwnerSession(
        string groupReservationId,
        GuestSession guest1,
        GuestSession guest2,
        GuestSession ownerSession)
    {
        guest1.IsOwnerSession = false;
        guest2.IsOwnerSession = false;
        ownerSession.IsOwnerSession = true;

        var search = new Mock<IAsyncSearch<GuestSession>>();
        search
            .Setup(s => s.GetRemainingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([guest1, ownerSession, guest2]);

        _mockContext
            .Setup(c => c.QueryAsync<GuestSession>(groupReservationId, It.IsAny<QueryConfig>()))
            .Returns(search.Object);

        int count = await _repository.CountByGroupReservationId(groupReservationId);

        Assert.Equal(2, count);
    }
}
