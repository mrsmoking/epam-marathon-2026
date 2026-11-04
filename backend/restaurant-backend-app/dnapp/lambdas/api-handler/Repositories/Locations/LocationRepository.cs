using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using APIHandler.Constants;
using APIHandler.Entities;
using APIHandler.Models.Feedbacks;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace APIHandler.Repositories.Locations;

public class LocationRepository : ILocationRepository
{
    private static readonly string LocationsTableName = ResourceNames.LocationsTableName;
    private static readonly string TablesTableName = ResourceNames.TablesTableName;
    private static readonly string FeedbackTableName = ResourceNames.FeedbackTableName;
    private static readonly string TablesLocationIdIndex = ResourceNames.TablesLocationIdIndex;
    private static readonly string FeedbackLocationIdIndex = ResourceNames.FeedbackGlobalIndex;

    private const string IdAttribute = "id";
    private const string SpecialtyDishIdsAttribute = "specialtyDishIds";

    // Concurrent menu changes can make a conditional list update fail (the list was started, or an element shifted,
    // between the read and the write); the update is then retried against a fresh read.
    private const int MaxMenuUpdateAttempts = 5;

    private readonly IDynamoDBContext _dynamoDbContext;
    private readonly IAmazonDynamoDB _dynamoDbClient;

    public LocationRepository(IDynamoDBContext dynamoDbContext, IAmazonDynamoDB dynamoDbClient)
    {
        _dynamoDbContext = dynamoDbContext ?? throw new ArgumentNullException(nameof(dynamoDbContext));
        _dynamoDbClient = dynamoDbClient ?? throw new ArgumentNullException(nameof(dynamoDbClient));
    }

    public async Task<Location?> GetLocationByIdAsync(string locationId)
    {
        return await _dynamoDbContext.LoadAsync<Location>(locationId, DynamoConfig.LoadConfig(LocationsTableName));
    }

    public async Task<Table?> GetTableByIdAsync(string tableId)
    {
        return await _dynamoDbContext.LoadAsync<Table>(tableId, DynamoConfig.LoadConfig(TablesTableName));
    }

    public async Task<IEnumerable<Location>> GetAllLocationsAsync()
    {
        return await _dynamoDbContext.ScanAsync<Location>(new List<ScanCondition>()).GetRemainingAsync();
    }

    public async Task<List<Table>> GetTablesByLocationIdAsync(string locationId)
    {
        var queryConfig = DynamoConfig.QueryConfig(TablesTableName);
        queryConfig.IndexName = TablesLocationIdIndex;
        queryConfig.ConsistentRead = false;

        return await _dynamoDbContext.QueryAsync<Table>(locationId, queryConfig).GetRemainingAsync();
    }

    public async Task<List<Feedback>> GetFeedbackByLocationIdAsync(string locationId)
    {
        var queryConfig = DynamoConfig.QueryConfig(FeedbackTableName);
        queryConfig.IndexName = FeedbackLocationIdIndex;
        queryConfig.ConsistentRead = false;

        return await _dynamoDbContext.QueryAsync<Feedback>(locationId, queryConfig).GetRemainingAsync();
    }

    public async Task SaveLocationAsync(Location location)
    {
        await _dynamoDbContext.SaveAsync(location, DynamoConfig.SaveConfig(ResourceNames.LocationsTableName));
    }

    public async Task DeleteLocationAsync(Location location)
    {
        await _dynamoDbContext.DeleteAsync(location, DynamoConfig.DeleteConfig(ResourceNames.LocationsTableName));
    }

    public async Task<bool> UpdateLocationDetailsAsync(Location location)
    {
        // Only the editable details are written: a full SaveAsync would also write back the menu
        // (specialtyDishIds) as it was when the location was read, dropping a dish added in the meantime.
        var names = new Dictionary<string, string>
        {
            ["#id"] = IdAttribute,
            ["#address"] = "address",
            ["#description"] = "description",
            ["#totalCapacity"] = "totalCapacity",
            ["#averageOccupancy"] = "averageOccupancy",
        };
        var values = new Dictionary<string, AttributeValue>
        {
            [":address"] = new() { S = location.Address },
            [":description"] = new() { S = location.Description },
            [":totalCapacity"] = new() { N = location.TotalCapacity.ToString(CultureInfo.InvariantCulture) },
            [":averageOccupancy"] = new() { S = location.AverageOccupancy },
        };
        var update = "SET #address = :address, #description = :description, #totalCapacity = :totalCapacity, #averageOccupancy = :averageOccupancy";

        if (!string.IsNullOrWhiteSpace(location.ImageUrl))
        {
            names["#imageUrl"] = "imageUrl";
            values[":imageUrl"] = new AttributeValue { S = location.ImageUrl };
            update += ", #imageUrl = :imageUrl";
        }

        return await TryUpdateAsync(new UpdateItemRequest
        {
            TableName = LocationsTableName,
            Key = KeyOf(location.Id),
            UpdateExpression = update,
            ConditionExpression = "attribute_exists(#id)",
            ExpressionAttributeNames = names,
            ExpressionAttributeValues = values,
        });
    }

    public async Task<bool> AddSpecialtyDishAsync(string locationId, string dishId)
    {
        for (int attempt = 1; attempt <= MaxMenuUpdateAttempts; attempt++)
        {
            var (exists, dishes) = await ReadSpecialtyDishIdsAsync(locationId);
            if (!exists)
                return false;

            if (dishes?.SS is not null)
            {
                // String set: ADD is atomic and idempotent.
                await _dynamoDbClient.UpdateItemAsync(new UpdateItemRequest
                {
                    TableName = LocationsTableName,
                    Key = KeyOf(locationId),
                    UpdateExpression = "ADD #dishes :dishSet",
                    ExpressionAttributeNames = new Dictionary<string, string> { ["#dishes"] = SpecialtyDishIdsAttribute },
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":dishSet"] = new() { SS = [dishId] },
                    },
                });
                return true;
            }

            if (dishes?.L is not null)
            {
                if (dishes.L.Any(item => item.S == dishId))
                    return true;

                // List: append in place, unless the dish got there concurrently.
                if (await TryUpdateAsync(new UpdateItemRequest
                {
                    TableName = LocationsTableName,
                    Key = KeyOf(locationId),
                    UpdateExpression = "SET #dishes = list_append(#dishes, :dish)",
                    ConditionExpression = "attribute_exists(#id) AND NOT contains(#dishes, :dishId)",
                    ExpressionAttributeNames = new Dictionary<string, string>
                    {
                        ["#id"] = IdAttribute,
                        ["#dishes"] = SpecialtyDishIdsAttribute,
                    },
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":dish"] = new() { L = [new AttributeValue { S = dishId }] },
                        [":dishId"] = new() { S = dishId },
                    },
                }))
                    return true;

                continue;
            }

            // No menu yet — the attribute is missing, or DynamoDBContext stored a null list as NULL. Start the list,
            // unless another request started it in the meantime (then the next attempt appends to it).
            if (await TryUpdateAsync(new UpdateItemRequest
            {
                TableName = LocationsTableName,
                Key = KeyOf(locationId),
                UpdateExpression = "SET #dishes = :dish",
                ConditionExpression = "attribute_exists(#id) AND (attribute_not_exists(#dishes) OR attribute_type(#dishes, :nullType))",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#id"] = IdAttribute,
                    ["#dishes"] = SpecialtyDishIdsAttribute,
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":dish"] = new() { L = [new AttributeValue { S = dishId }] },
                    [":nullType"] = new() { S = "NULL" },
                },
            }))
                return true;
        }

        throw new InvalidOperationException(
            $"Could not add dish '{dishId}' to location '{locationId}' after {MaxMenuUpdateAttempts} attempts: the menu kept changing.");
    }

    public async Task<bool> RemoveSpecialtyDishAsync(string locationId, string dishId)
    {
        for (int attempt = 1; attempt <= MaxMenuUpdateAttempts; attempt++)
        {
            var (exists, dishes) = await ReadSpecialtyDishIdsAsync(locationId);
            if (!exists)
                return false;

            if (dishes?.SS is not null)
            {
                if (!dishes.SS.Contains(dishId))
                    return false;

                // String set: DELETE is atomic.
                await _dynamoDbClient.UpdateItemAsync(new UpdateItemRequest
                {
                    TableName = LocationsTableName,
                    Key = KeyOf(locationId),
                    UpdateExpression = "DELETE #dishes :dishSet",
                    ExpressionAttributeNames = new Dictionary<string, string> { ["#dishes"] = SpecialtyDishIdsAttribute },
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":dishSet"] = new() { SS = [dishId] },
                    },
                });
                return true;
            }

            int index = dishes?.L?.FindIndex(item => item.S == dishId) ?? -1;
            if (index < 0)
                return false;

            // List: remove the element at the index just read, but only if it is still this dish.
            bool removed = await TryUpdateAsync(new UpdateItemRequest
            {
                TableName = LocationsTableName,
                Key = KeyOf(locationId),
                UpdateExpression = $"REMOVE #dishes[{index.ToString(CultureInfo.InvariantCulture)}]",
                ConditionExpression = $"#dishes[{index.ToString(CultureInfo.InvariantCulture)}] = :dishId",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#dishes"] = SpecialtyDishIdsAttribute },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":dishId"] = new() { S = dishId },
                },
            });

            if (removed)
                return true;
        }

        throw new InvalidOperationException(
            $"Could not remove dish '{dishId}' from location '{locationId}' after {MaxMenuUpdateAttempts} attempts: the menu kept changing.");
    }

    /// <summary>Reads only the menu attribute, strongly consistent. <c>Exists</c> is false when the location is gone.</summary>
    private async Task<(bool Exists, AttributeValue? Dishes)> ReadSpecialtyDishIdsAsync(string locationId)
    {
        var response = await _dynamoDbClient.GetItemAsync(new GetItemRequest
        {
            TableName = LocationsTableName,
            Key = KeyOf(locationId),
            ConsistentRead = true,
            ProjectionExpression = "#id, #dishes",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#id"] = IdAttribute,
                ["#dishes"] = SpecialtyDishIdsAttribute,
            },
        });

        if (response.Item is null || response.Item.Count == 0)
            return (false, null);

        return (true, response.Item.GetValueOrDefault(SpecialtyDishIdsAttribute));
    }

    private async Task<bool> TryUpdateAsync(UpdateItemRequest request)
    {
        try
        {
            await _dynamoDbClient.UpdateItemAsync(request);
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    private static Dictionary<string, AttributeValue> KeyOf(string locationId) =>
        new() { [IdAttribute] = new AttributeValue { S = locationId } };
}
