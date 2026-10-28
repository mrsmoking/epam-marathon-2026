using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.Model;
using APIHandler.Entities;
using APIHandler.Repositories.GuestSessions;
using AutoFixture.Xunit2;
using Moq;

namespace APIHandler.Tests.Repositories;

public class GuestSessionRepositoryTests
{
    private readonly Mock<IDynamoDBContext> _mockContext = new();
    private readonly Mock<IAmazonDynamoDB> _mockClient = new();
    private readonly GuestSessionRepository _repository;

    public GuestSessionRepositoryTests()
    {
        Environment.SetEnvironmentVariable("guestSessionTable", "GuestSessions");
        _repository = new GuestSessionRepository(_mockContext.Object, _mockClient.Object);
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

    [Theory]
    [AutoData]
    public async Task TryUpdateExistingAsync_SessionExists_SendsConditionalPutAndReturnsTrue(GuestSession session)
    {
        var document = new Document
        {
            ["guestSessionId"] = session.Id,
            ["guestName"] = session.GuestName,
        };
        _mockContext
            .Setup(c => c.ToDocument(session, It.IsAny<ToDocumentConfig>()))
            .Returns(document);

        PutItemRequest? sent = null;
        _mockClient
            .Setup(c => c.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutItemRequest, CancellationToken>((r, _) => sent = r)
            .ReturnsAsync(new PutItemResponse());

        bool result = await _repository.TryUpdateExistingAsync(session);

        Assert.True(result);
        Assert.NotNull(sent);
        Assert.Equal("GuestSessions", sent.TableName);
        Assert.Equal("attribute_exists(#id)", sent.ConditionExpression);
        Assert.Equal("guestSessionId", sent.ExpressionAttributeNames["#id"]);
        Assert.Equal(session.Id, sent.Item["guestSessionId"].S);
        Assert.Equal(session.GuestName, sent.Item["guestName"].S);
        _mockContext.Verify(c => c.SaveAsync(It.IsAny<GuestSession>(), It.IsAny<SaveConfig>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task TryUpdateExistingAsync_SessionDeleted_ReturnsFalse(GuestSession session)
    {
        _mockContext
            .Setup(c => c.ToDocument(session, It.IsAny<ToDocumentConfig>()))
            .Returns(new Document { ["guestSessionId"] = session.Id });
        _mockClient
            .Setup(c => c.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConditionalCheckFailedException("The conditional request failed"));

        bool result = await _repository.TryUpdateExistingAsync(session);

        Assert.False(result);
    }
}
