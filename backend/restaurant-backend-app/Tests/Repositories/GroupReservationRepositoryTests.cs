using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using APIHandler.Entities;
using APIHandler.Repositories.GroupReservations;
using AutoFixture.Xunit2;
using Moq;

namespace APIHandler.Tests.Repositories;

public class GroupReservationRepositoryTests
{
    private readonly Mock<IDynamoDBContext> _mockContext = new();
    private readonly Mock<IAmazonDynamoDB> _mockClient = new();
    private readonly GroupReservationRepository _repository;

    public GroupReservationRepositoryTests()
    {
        Environment.SetEnvironmentVariable("groupReservationsTable", "GroupReservations");
        _repository = new GroupReservationRepository(_mockContext.Object, _mockClient.Object);
    }

    [Theory]
    [AutoData]
    public async Task TryUpdateStatusAsync_ConditionMet_SendsConditionalNumericUpdateAndReturnsTrue(string groupReservationId)
    {
        UpdateItemRequest? sent = null;
        _mockClient
            .Setup(c => c.UpdateItemAsync(It.IsAny<UpdateItemRequest>(), It.IsAny<CancellationToken>()))
            .Callback<UpdateItemRequest, CancellationToken>((r, _) => sent = r)
            .ReturnsAsync(new UpdateItemResponse());

        bool result = await _repository.TryUpdateStatusAsync(
            groupReservationId, GroupReservationStatus.Open, GroupReservationStatus.Submitted);

        Assert.True(result);
        Assert.NotNull(sent);
        Assert.Equal("GroupReservations", sent.TableName);
        Assert.Equal(groupReservationId, sent.Key["groupReservationId"].S);
        Assert.Equal("SET #status = :newStatus", sent.UpdateExpression);
        Assert.Equal("#status = :expectedStatus", sent.ConditionExpression);
        Assert.Equal("status", sent.ExpressionAttributeNames["#status"]);
        // The enum has no converter, so DynamoDBContext stores it as a number (N).
        Assert.Equal("1", sent.ExpressionAttributeValues[":newStatus"].N);
        Assert.Equal("0", sent.ExpressionAttributeValues[":expectedStatus"].N);
    }

    [Theory]
    [AutoData]
    public async Task TryUpdateStatusAsync_ConditionalCheckFailed_ReturnsFalse(string groupReservationId)
    {
        _mockClient
            .Setup(c => c.UpdateItemAsync(It.IsAny<UpdateItemRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConditionalCheckFailedException("The conditional request failed"));

        bool result = await _repository.TryUpdateStatusAsync(
            groupReservationId, GroupReservationStatus.Open, GroupReservationStatus.Submitted);

        Assert.False(result);
    }
}
