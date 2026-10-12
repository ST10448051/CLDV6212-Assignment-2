using System.Net;
using System.Text.Json;
using Azure.Data.Tables;
using CoffeeNChill.Functions.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace CoffeeNChill.Functions.Functions;

public class MenuFunctions
{
    private readonly TableClient _tableClient;

    public MenuFunctions()
    {
        string connectionString =
            Environment.GetEnvironmentVariable("AzureWebJobsStorage")
            ?? "UseDevelopmentStorage=true";

        var serviceClient = new TableServiceClient(connectionString);

        _tableClient = serviceClient.GetTableClient("MenuItems");

        _tableClient.CreateIfNotExists();
    }

    // POST /api/menu
    [Function("CreateMenuItem")]
    public async Task<HttpResponseData> CreateMenuItem(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "post",
            Route = "menu")] HttpRequestData req)
    {
        try
        {
            var menuItem =
                await JsonSerializer.DeserializeAsync<MenuItem>(
                    req.Body,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

            if (menuItem == null)
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteStringAsync("Invalid menu item.");
                return bad;
            }

            if (string.IsNullOrWhiteSpace(menuItem.PartitionKey) ||
                string.IsNullOrWhiteSpace(menuItem.RowKey) ||
                string.IsNullOrWhiteSpace(menuItem.Name))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteStringAsync(
                    "PartitionKey, RowKey and Name are required.");
                return bad;
            }

            await _tableClient.AddEntityAsync(menuItem);

            var response = req.CreateResponse(HttpStatusCode.Created);

            await response.WriteAsJsonAsync(menuItem);

            return response;
        }
        catch (Exception ex)
        {
            var response = req.CreateResponse(
                HttpStatusCode.InternalServerError);

            await response.WriteStringAsync(ex.Message);

            return response;
        }
    }


    // GET /api/menu
    [Function("GetAllMenuItems")]
    public async Task<HttpResponseData> GetAllMenuItems(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "menu")] HttpRequestData req)
    {
        var items = new List<MenuItem>();

        await foreach (var item in _tableClient.QueryAsync<MenuItem>())
        {
            items.Add(item);
        }

        var response = req.CreateResponse(HttpStatusCode.OK);

        await response.WriteAsJsonAsync(items);

        return response;
    }


    // GET /api/menu/category/{category}
    [Function("GetMenuItemsByCategory")]
    public async Task<HttpResponseData> GetMenuItemsByCategory(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "menu/category/{category}")]
        HttpRequestData req,
        string category)
    {
        var items = new List<MenuItem>();

        await foreach (var item in
            _tableClient.QueryAsync<MenuItem>(
                x => x.PartitionKey == category))
        {
            items.Add(item);
        }

        var response = req.CreateResponse(HttpStatusCode.OK);

        await response.WriteAsJsonAsync(items);

        return response;
    }


    // PUT /api/menu/{category}/{id}
    [Function("UpdateMenuItem")]
    public async Task<HttpResponseData> UpdateMenuItem(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "put",
            Route = "menu/{category}/{id}")]
        HttpRequestData req,
        string category,
        string id)
    {
        var existing =
            await _tableClient.GetEntityIfExistsAsync<MenuItem>(
                category,
                id);

        if (!existing.HasValue)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteStringAsync("Menu item not found.");
            return notFound;
        }

        var update =
            await JsonSerializer.DeserializeAsync<MenuItem>(
                req.Body,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

        if (update == null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("Invalid update.");
            return bad;
        }

        var item = existing.Value;

        item.Name = update.Name;
        item.Description = update.Description;
        item.Price = update.Price;
        item.IsAvailable = update.IsAvailable;

        await _tableClient.UpdateEntityAsync(
            item,
            item.ETag);

        var response = req.CreateResponse(HttpStatusCode.OK);

        await response.WriteAsJsonAsync(item);

        return response;
    }


    // DELETE /api/menu/{category}/{id}
    [Function("DeleteMenuItem")]
    public async Task<HttpResponseData> DeleteMenuItem(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "delete",
            Route = "menu/{category}/{id}")]
        HttpRequestData req,
        string category,
        string id)
    {
        await _tableClient.DeleteEntityAsync(category, id);

        var response = req.CreateResponse(HttpStatusCode.NoContent);

        return response;
    }
}