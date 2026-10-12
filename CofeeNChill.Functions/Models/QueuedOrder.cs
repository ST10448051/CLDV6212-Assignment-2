namespace CoffeeNChill.Functions.Models;

public class QueuedOrder
{
    public string OrderId { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public List<string> SelectedItemSKUs { get; set; } = new();
    public decimal TotalPrice { get; set; }
    public DateTimeOffset OrderTimestamp { get; set; }
}
