namespace InventoryApp.Models;

/// <summary>Carries no exception detail, so a stack trace can never reach the browser.</summary>
public class ErrorViewModel
{
    public string? RequestId { get; set; }

    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

    public int? StatusCode { get; set; }
}
