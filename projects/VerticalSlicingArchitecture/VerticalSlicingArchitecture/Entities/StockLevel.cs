using System;

public class StockLevel
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
    public DateTime LastUpdated { get; set; }
} 