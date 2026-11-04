using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using APIHandler.Entities;
using APIHandler.Repositories.Locations;
using AutoFixture.Xunit2;
using Moq;

namespace APIHandler.Tests.Repositories;

/// <summary>The menu of a location (specialtyDishIds) is changed with single conditional updates, never by writing
/// the whole location back, so concurrent changes to the same menu cannot overwrite each other.</summary>
public class LocationRepositoryTests
{
    private readonly Mock<IDynamoDBContext> _mockContext = new();
    private readonly Mock<IAmazonDynamoDB> _mockClient = new();
    private readonly List<UpdateItemRequest> _updates = [];
    private readonly LocationRepository _repository;

    public LocationRepositoryTests()
    {
        Environment.SetEnvironmentVariable("locationsTable", "Locations");
        Environment.SetEnvironmentVariable("tablesTable", "Tables");
        Environment.SetEnvironmentVariable("FEEDBACK_TABLE_NAME", "Feedback");
        Environment.SetEnvironmentVariable("tableLocationIdIndex", "TablesLocationIdIndex");
        Environment.SetEnvironmentVariable("feedbackGlobalIndex", "FeedbackLocationIdIndex");
        _repository = new LocationRepository(_mockContext.Object, _mockClient.Object);
    }

    // ---- adding a dish ----

    [Theory]
    [AutoData]
    public async Task AddSpecialtyDishAsync_LocationMissing_ReturnsFalseWithoutUpdating(string locationId, string dishId)
    {
        SetUpReads(Missing());

        bool result = await _repository.AddSpecialtyDishAsync(locationId, dishId);

        Assert.False(result);
        Assert.Empty(_updates);
    }

    [Theory]
    [AutoData]
    public async Task AddSpecialtyDishAsync_ListMenu_AppendsWithConditionalUpdate(string locationId, string dishId, string otherDishId)
    {
        SetUpReads(WithList(locationId, otherDishId));
        SetUpUpdates(true);

        bool result = await _repository.AddSpecialtyDishAsync(locationId, dishId);

        Assert.True(result);
        var update = Assert.Single(_updates);
        Assert.Equal("Locations", update.TableName);
        Assert.Equal(locationId, update.Key["id"].S);
        Assert.Equal("SET #dishes = list_append(#dishes, :dish)", update.UpdateExpression);
        Assert.Equal("attribute_exists(#id) AND NOT contains(#dishes, :dishId)", update.ConditionExpression);
        Assert.Equal("specialtyDishIds", update.ExpressionAttributeNames["#dishes"]);
        Assert.Equal(dishId, Assert.Single(update.ExpressionAttributeValues[":dish"].L).S);
    }

    [Theory]
    [AutoData]
    public async Task AddSpecialtyDishAsync_DishAlreadyOnTheMenu_ReturnsTrueWithoutUpdating(string locationId, string dishId)
    {
        SetUpReads(WithList(locationId, dishId));

        bool result = await _repository.AddSpecialtyDishAsync(locationId, dishId);

        Assert.True(result);
        Assert.Empty(_updates);
    }

    [Theory]
    [AutoData]
    public async Task AddSpecialtyDishAsync_NoMenuYet_StartsTheList(string locationId, string dishId)
    {
        SetUpReads(WithoutMenu(locationId));
        SetUpUpdates(true);

        bool result = await _repository.AddSpecialtyDishAsync(locationId, dishId);

        Assert.True(result);
        var update = Assert.Single(_updates);
        Assert.Equal("SET #dishes = :dish", update.UpdateExpression);
        Assert.Equal(
            "attribute_exists(#id) AND (attribute_not_exists(#dishes) OR attribute_type(#dishes, :nullType))",
            update.ConditionExpression);
        Assert.Equal("NULL", update.ExpressionAttributeValues[":nullType"].S);
    }

    [Theory]
    [AutoData]
    public async Task AddSpecialtyDishAsync_NullMenu_StartsTheList(string locationId, string dishId)
    {
        SetUpReads(Item(locationId, new AttributeValue { NULL = true }));
        SetUpUpdates(true);

        bool result = await _repository.AddSpecialtyDishAsync(locationId, dishId);

        Assert.True(result);
        Assert.Equal("SET #dishes = :dish", Assert.Single(_updates).UpdateExpression);
    }

    [Theory]
    [AutoData]
    public async Task AddSpecialtyDishAsync_StringSetMenu_AddsToTheSet(string locationId, string dishId, string otherDishId)
    {
        SetUpReads(Item(locationId, new AttributeValue { SS = [otherDishId] }));
        SetUpUpdates(true);

        bool result = await _repository.AddSpecialtyDishAsync(locationId, dishId);

        Assert.True(result);
        var update = Assert.Single(_updates);
        Assert.Equal("ADD #dishes :dishSet", update.UpdateExpression);
        Assert.Equal(dishId, Assert.Single(update.ExpressionAttributeValues[":dishSet"].SS));
    }

    [Theory]
    [AutoData]
    public async Task AddSpecialtyDishAsync_MenuStartedConcurrently_RetriesAndAppends(string locationId, string dishId, string otherDishId)
    {
        SetUpReads(WithoutMenu(locationId), WithList(locationId, otherDishId));
        SetUpUpdates(false, true);

        bool result = await _repository.AddSpecialtyDishAsync(locationId, dishId);

        Assert.True(result);
        Assert.Equal(2, _updates.Count);
        Assert.Equal("SET #dishes = :dish", _updates[0].UpdateExpression);
        Assert.Equal("SET #dishes = list_append(#dishes, :dish)", _updates[1].UpdateExpression);
    }

    // ---- removing a dish ----

    [Theory]
    [AutoData]
    public async Task RemoveSpecialtyDishAsync_ListMenu_RemovesTheIndexOnlyIfItIsStillTheDish(
        string locationId, string dishId, string before, string after)
    {
        SetUpReads(WithList(locationId, before, dishId, after));
        SetUpUpdates(true);

        bool result = await _repository.RemoveSpecialtyDishAsync(locationId, dishId);

        Assert.True(result);
        var update = Assert.Single(_updates);
        Assert.Equal("REMOVE #dishes[1]", update.UpdateExpression);
        Assert.Equal("#dishes[1] = :dishId", update.ConditionExpression);
        Assert.Equal(dishId, update.ExpressionAttributeValues[":dishId"].S);
    }

    [Theory]
    [AutoData]
    public async Task RemoveSpecialtyDishAsync_ListShiftedConcurrently_RetriesAtTheNewIndex(string locationId, string dishId, string before)
    {
        SetUpReads(WithList(locationId, before, dishId), WithList(locationId, dishId));
        SetUpUpdates(false, true);

        bool result = await _repository.RemoveSpecialtyDishAsync(locationId, dishId);

        Assert.True(result);
        Assert.Equal(["REMOVE #dishes[1]", "REMOVE #dishes[0]"], _updates.Select(u => u.UpdateExpression));
    }

    [Theory]
    [AutoData]
    public async Task RemoveSpecialtyDishAsync_DishNotOnTheMenu_ReturnsFalseWithoutUpdating(string locationId, string dishId, string otherDishId)
    {
        SetUpReads(WithList(locationId, otherDishId));

        bool result = await _repository.RemoveSpecialtyDishAsync(locationId, dishId);

        Assert.False(result);
        Assert.Empty(_updates);
    }

    [Theory]
    [AutoData]
    public async Task RemoveSpecialtyDishAsync_LocationMissing_ReturnsFalse(string locationId, string dishId)
    {
        SetUpReads(Missing());

        Assert.False(await _repository.RemoveSpecialtyDishAsync(locationId, dishId));
        Assert.Empty(_updates);
    }

    [Theory]
    [AutoData]
    public async Task RemoveSpecialtyDishAsync_StringSetMenu_DeletesFromTheSet(string locationId, string dishId, string otherDishId)
    {
        SetUpReads(Item(locationId, new AttributeValue { SS = [otherDishId, dishId] }));
        SetUpUpdates(true);

        bool result = await _repository.RemoveSpecialtyDishAsync(locationId, dishId);

        Assert.True(result);
        var update = Assert.Single(_updates);
        Assert.Equal("DELETE #dishes :dishSet", update.UpdateExpression);
        Assert.Equal(dishId, Assert.Single(update.ExpressionAttributeValues[":dishSet"].SS));
    }

    // ---- editing the details ----

    [Theory]
    [AutoData]
    public async Task UpdateLocationDetailsAsync_WritesTheDetailsButNotTheMenu(Location location)
    {
        SetUpUpdates(true);

        bool result = await _repository.UpdateLocationDetailsAsync(location);

        Assert.True(result);
        var update = Assert.Single(_updates);
        Assert.Equal("attribute_exists(#id)", update.ConditionExpression);
        Assert.Contains("#address = :address", update.UpdateExpression);
        Assert.Contains("#imageUrl = :imageUrl", update.UpdateExpression);
        Assert.DoesNotContain("specialtyDishIds", update.ExpressionAttributeNames.Values);
        Assert.DoesNotContain("rating", update.ExpressionAttributeNames.Values);
    }

    [Theory]
    [AutoData]
    public async Task UpdateLocationDetailsAsync_LocationMissing_ReturnsFalse(Location location)
    {
        SetUpUpdates(false);

        Assert.False(await _repository.UpdateLocationDetailsAsync(location));
    }

    // ---- helpers ----

    private void SetUpReads(params GetItemResponse[] responses)
    {
        var setup = _mockClient.SetupSequence(c => c.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()));
        foreach (var response in responses)
            setup = setup.ReturnsAsync(response);
    }

    private void SetUpUpdates(params bool[] succeed)
    {
        var attempt = 0;
        _mockClient
            .Setup(c => c.UpdateItemAsync(It.IsAny<UpdateItemRequest>(), It.IsAny<CancellationToken>()))
            .Callback<UpdateItemRequest, CancellationToken>((request, _) => _updates.Add(request))
            .Returns(() => succeed[Math.Min(attempt++, succeed.Length - 1)]
                ? Task.FromResult(new UpdateItemResponse())
                : Task.FromException<UpdateItemResponse>(new ConditionalCheckFailedException("The conditional request failed")));
    }

    private static GetItemResponse Missing() => new() { Item = new Dictionary<string, AttributeValue>() };

    private static GetItemResponse WithoutMenu(string locationId) =>
        new() { Item = new Dictionary<string, AttributeValue> { ["id"] = new() { S = locationId } } };

    private static GetItemResponse WithList(string locationId, params string[] dishIds) =>
        Item(locationId, new AttributeValue { L = dishIds.Select(id => new AttributeValue { S = id }).ToList() });

    private static GetItemResponse Item(string locationId, AttributeValue dishes) =>
        new()
        {
            Item = new Dictionary<string, AttributeValue>
            {
                ["id"] = new() { S = locationId },
                ["specialtyDishIds"] = dishes,
            },
        };
}
