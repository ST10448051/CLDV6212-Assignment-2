using System.Net;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using CoffeeNChill.Functions.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace CoffeeNChill.Functions.Functions;

public class OrderFunctions
{
    private const string QueueName = "order-processing-queue";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly QueueClient _queue;
    private readonly TableClient _orders;
    private readonly ILogger<OrderFunctions> _logger;

    public OrderFunctions(ILogger<OrderFunctions> logger)
    {
        _logger = logger;
        var connection = Environment.GetEnvironmentVariable("AzureWebJobsStorage")
            ?? throw new InvalidOperationException("AzureWebJobsStorage is required.");
        _queue = new QueueClient(connection, QueueName, new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });
        _orders = new TableServiceClient(connection).GetTableClient("Orders");
    }

    [Function("QueueOrder")]
    public async Task<HttpResponseData> QueueOrder(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "orders/queue")] HttpRequestData req)
    {
        QueuedOrder? order;
        try { order = await JsonSerializer.DeserializeAsync<QueuedOrder>(req.Body, JsonOptions); }
        catch (JsonException) { return await Error(req, HttpStatusCode.BadRequest, "Invalid JSON."); }

        if (order is null || string.IsNullOrWhiteSpace(order.OrderId) ||
            string.IsNullOrWhiteSpace(order.CustomerName) ||
            order.SelectedItemSKUs is null || order.SelectedItemSKUs.Count == 0 ||
            order.SelectedItemSKUs.Any(string.IsNullOrWhiteSpace) || order.TotalPrice <= 0 ||
            order.OrderTimestamp == default || order.OrderId.Contains('/') || order.OrderId.Contains('\\'))
            return await Error(req, HttpStatusCode.BadRequest, "OrderId, CustomerName, SelectedItemSKUs, positive TotalPrice and OrderTimestamp are required.");

        try
        {
            await _queue.CreateIfNotExistsAsync();
            await _queue.SendMessageAsync(JsonSerializer.Serialize(order));
            var response = req.CreateResponse(HttpStatusCode.Accepted);
            await response.WriteAsJsonAsync(new { order.OrderId, Message = "Order queued for processing" });
            return response;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Could not queue order {OrderId}", order.OrderId);
            return await Error(req, HttpStatusCode.ServiceUnavailable, "Queue storage unavailable.");
        }
    }

    [Function("ProcessOrderQueue")]
    public async Task ProcessOrderQueue(
        [QueueTrigger(QueueName, Connection = "AzureWebJobsStorage")] string message)
    {
        var order = JsonSerializer.Deserialize<QueuedOrder>(message, JsonOptions)
            ?? throw new JsonException("Queue message did not contain an order.");
        if (string.IsNullOrWhiteSpace(order.OrderId) || order.OrderTimestamp == default)
            throw new JsonException("Queue message is missing order identity or timestamp.");

        await _orders.CreateIfNotExistsAsync();
        var entity = new TableEntity(order.OrderTimestamp.UtcDateTime.ToString("yyyy-MM-dd"), order.OrderId)
        {
            ["CustomerName"] = order.CustomerName,
            ["SelectedItemSKUs"] = JsonSerializer.Serialize(order.SelectedItemSKUs),
            ["TotalPrice"] = (double)order.TotalPrice,
            ["OrderTimestamp"] = order.OrderTimestamp,
            ["Status"] = "Received"
        };
        await _orders.UpsertEntityAsync(entity, TableUpdateMode.Replace);
        _logger.LogInformation("Order {OrderId}: Received", order.OrderId);
        foreach (var status in new[] { "Preparing", "Ready", "Collected" })
        {
            await Task.Delay(TimeSpan.FromSeconds(3)); // Demo only: replace with real status events in production.
            entity["Status"] = status;
            await _orders.UpsertEntityAsync(entity, TableUpdateMode.Replace);
            _logger.LogInformation("Order {OrderId}: {Status}", order.OrderId, status);
        }
    }

    [Function("GetOrder")]
    public async Task<HttpResponseData> GetOrder(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "orders/{date}/{orderId}")] HttpRequestData req,
        string date, string orderId)
    {
        try
        {
            var entity = await _orders.GetEntityIfExistsAsync<TableEntity>(date, orderId);
            if (!entity.HasValue) return await Error(req, HttpStatusCode.NotFound, "Order not found (or not yet processed).");
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(entity.Value);
            return response;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Could not read order {OrderId}", orderId);
            return await Error(req, HttpStatusCode.ServiceUnavailable, "Table storage unavailable.");
        }
    }

    private static async Task<HttpResponseData> Error(HttpRequestData req, HttpStatusCode code, string message)
    {
        var response = req.CreateResponse(code);
        await response.WriteAsJsonAsync(new { Error = message });
        return response;
    }
}
