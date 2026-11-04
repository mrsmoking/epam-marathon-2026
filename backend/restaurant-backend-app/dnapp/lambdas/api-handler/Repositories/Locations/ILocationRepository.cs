using APIHandler.Entities;
using APIHandler.Models.Feedbacks;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace APIHandler.Repositories.Locations;

public interface ILocationRepository
{
    Task<Location?> GetLocationByIdAsync(string locationId);
    Task<Table?> GetTableByIdAsync(string tableId);
    Task<List<Table>> GetTablesByLocationIdAsync(string locationId);
    Task<List<Feedback>> GetFeedbackByLocationIdAsync(string locationId);
    Task<IEnumerable<Location>> GetAllLocationsAsync();
    Task SaveLocationAsync(Location location);
    Task DeleteLocationAsync(Location location);

    /// <summary>Writes only the editable details (address, description, capacity, occupancy, image), leaving the
    /// menu untouched. False if the location does not exist.</summary>
    Task<bool> UpdateLocationDetailsAsync(Location location);

    /// <summary>Adds a dish to the location's menu in one atomic update, so concurrent additions never overwrite
    /// each other. True if the dish is on the menu afterwards; false if the location does not exist.</summary>
    Task<bool> AddSpecialtyDishAsync(string locationId, string dishId);

    /// <summary>Removes a dish from the location's menu atomically. False if the location does not exist or the
    /// dish was not on its menu.</summary>
    Task<bool> RemoveSpecialtyDishAsync(string locationId, string dishId);
}
