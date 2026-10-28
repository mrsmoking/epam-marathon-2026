using System.Text.Json.Serialization;

namespace APIHandler.Models.GroupReservation;

public record GroupPreOrderItemResponse(
    [property: JsonPropertyName("dishId")] string DishId,
    [property: JsonPropertyName("quantity")] int Quantity);
