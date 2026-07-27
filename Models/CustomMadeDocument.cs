namespace CameywareOrder.Models;

public enum CustomMadeDocumentCategory
{
    HandwritingReceipt = 1,
    Fabric = 2,
    Photo = 3,
    Other = 4
}

// A single uploaded image attached to a custom-made record. The image bytes live
// on disk in the document store; only this reference is persisted in the record JSON.
public class CustomMadeDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public CustomMadeDocumentCategory Category { get; set; } = CustomMadeDocumentCategory.Other;
    // Original file name chosen by the user (for display / download suggestions).
    public string FileName { get; set; } = string.Empty;
    // Name of the file inside the document store directory.
    public string StoredFileName { get; set; } = string.Empty;
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
}
