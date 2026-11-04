using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Models.Dishes;
using APIHandler.Repositories.Dishes;
using APIHandler.Repositories.Locations;
using APIHandler.Services.Dishes;
using AutoFixture.Xunit2;
using Moq;

namespace APIHandler.Tests.Services;

/// <summary>Changing which menus a dish is on goes through the atomic menu updates of the location repository,
/// never through saving whole locations.</summary>
public class DishServiceTests
{
    private readonly Mock<IDishRepository> _mockDishRepository = new();
    private readonly Mock<ILocationRepository> _mockLocationRepository = new();
    private readonly DishService _dishService;

    public DishServiceTests()
    {
        _dishService = new DishService(_mockDishRepository.Object, _mockLocationRepository.Object);
    }

    [Theory]
    [AutoData]
    public async Task UpdateDishAsync_AddsTheDishToEachRequestedMenuOnce(
        string dishId, Dish dish, CreateDishRequest request, string firstLocationId, string secondLocationId)
    {
        request.LocationIds = [firstLocationId, secondLocationId, firstLocationId];
        _mockDishRepository.Setup(r => r.GetDishByIdAsync(dishId)).ReturnsAsync(dish);

        await _dishService.UpdateDishAsync(dishId, request, null);

        _mockLocationRepository.Verify(r => r.AddSpecialtyDishAsync(firstLocationId, dishId), Times.Once);
        _mockLocationRepository.Verify(r => r.AddSpecialtyDishAsync(secondLocationId, dishId), Times.Once);
        _mockLocationRepository.Verify(r => r.SaveLocationAsync(It.IsAny<Location>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task UpdateDishAsync_NoLocations_LeavesEveryMenuAlone(string dishId, Dish dish, CreateDishRequest request)
    {
        request.LocationIds = [];
        _mockDishRepository.Setup(r => r.GetDishByIdAsync(dishId)).ReturnsAsync(dish);

        await _dishService.UpdateDishAsync(dishId, request, null);

        _mockLocationRepository.Verify(r => r.AddSpecialtyDishAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task DeleteDishAsync_Force_RemovesTheDishFromEveryMenuItIsOn(
        string dishId, Dish dish, Location onTheMenu, Location alsoOnTheMenu, Location notOnTheMenu)
    {
        onTheMenu.SpecialtyDishIds = [dishId];
        alsoOnTheMenu.SpecialtyDishIds = ["other", dishId];
        notOnTheMenu.SpecialtyDishIds = ["other"];
        _mockDishRepository.Setup(r => r.GetDishByIdAsync(dishId)).ReturnsAsync(dish);
        _mockLocationRepository.Setup(r => r.GetAllLocationsAsync()).ReturnsAsync(new List<Location> { onTheMenu, alsoOnTheMenu, notOnTheMenu });

        await _dishService.DeleteDishAsync(dishId, force: true);

        _mockLocationRepository.Verify(r => r.RemoveSpecialtyDishAsync(onTheMenu.Id, dishId), Times.Once);
        _mockLocationRepository.Verify(r => r.RemoveSpecialtyDishAsync(alsoOnTheMenu.Id, dishId), Times.Once);
        _mockLocationRepository.Verify(r => r.RemoveSpecialtyDishAsync(notOnTheMenu.Id, It.IsAny<string>()), Times.Never);
        _mockLocationRepository.Verify(r => r.SaveLocationAsync(It.IsAny<Location>()), Times.Never);
        _mockDishRepository.Verify(r => r.DeleteDishAsync(dish), Times.Once);
    }

    [Theory]
    [AutoData]
    public async Task DeleteDishAsync_OnAMenuWithoutForce_ThrowsAndChangesNothing(string dishId, Dish dish, Location onTheMenu)
    {
        onTheMenu.SpecialtyDishIds = [dishId];
        _mockDishRepository.Setup(r => r.GetDishByIdAsync(dishId)).ReturnsAsync(dish);
        _mockLocationRepository.Setup(r => r.GetAllLocationsAsync()).ReturnsAsync(new List<Location> { onTheMenu });

        await Assert.ThrowsAsync<ValidationException>(() => _dishService.DeleteDishAsync(dishId));

        _mockLocationRepository.Verify(r => r.RemoveSpecialtyDishAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _mockDishRepository.Verify(r => r.DeleteDishAsync(It.IsAny<Dish>()), Times.Never);
    }
}
