using Azure.Storage.Blobs;
using Azure.Identity;

var builder = WebApplication.CreateBuilder(args);

// Configure Azure Blob Storage with Managed Identity
builder.Services.AddSingleton(x =>
{
    var storageAccountName = builder.Configuration["AzureStorage:AccountName"] 
        ?? Environment.GetEnvironmentVariable("AZURE_STORAGE_ACCOUNT_NAME")
        ?? throw new InvalidOperationException("Azure Storage account name not found. Set AzureStorage:AccountName in configuration or AZURE_STORAGE_ACCOUNT_NAME environment variable.");
    
    var blobServiceUri = new Uri($"https://{storageAccountName}.blob.core.windows.net");
    
    // Use DefaultAzureCredential which will try multiple authentication methods:
    // 1. Environment variables (for local development)
    // 2. Managed Identity (when deployed to Azure)
    // 3. Azure CLI (for local development)
    // 4. Visual Studio/VS Code (for local development)
    var credential = new DefaultAzureCredential();
    
    return new BlobServiceClient(blobServiceUri, credential);
});

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { ok = true, message = "Hello from Azure App Service!" }));
app.MapGet("/health", () => Results.Ok("healthy"));

// Image upload endpoint
app.MapPost("/upload-image", async (IFormFile file, BlobServiceClient blobServiceClient) =>
{
    try
    {
        // Validate file
        if (file == null || file.Length == 0)
            return Results.BadRequest(new { error = "No file provided" });

        // Validate file type (only images)
        var allowedTypes = new[] { "image/jpeg", "image/png", "image/gif", "image/webp" };
        if (!allowedTypes.Contains(file.ContentType.ToLower()))
            return Results.BadRequest(new { error = "Only image files are allowed (JPEG, PNG, GIF, WebP)" });

        // Validate file size (max 5MB)
        const int maxSizeBytes = 5 * 1024 * 1024; // 5MB
        if (file.Length > maxSizeBytes)
            return Results.BadRequest(new { error = "File size must be less than 5MB" });

        // Generate unique filename
        var fileExtension = Path.GetExtension(file.FileName);
        var fileName = $"{Guid.NewGuid()}{fileExtension}";

        // Get container client (create container if it doesn't exist)
        var containerClient = blobServiceClient.GetBlobContainerClient("images");
        await containerClient.CreateIfNotExistsAsync(Azure.Storage.Blobs.Models.PublicAccessType.Blob);

        // Upload file to blob storage
        var blobClient = containerClient.GetBlobClient(fileName);
        
        using var stream = file.OpenReadStream();
        await blobClient.UploadAsync(stream, new Azure.Storage.Blobs.Models.BlobUploadOptions
        {
            HttpHeaders = new Azure.Storage.Blobs.Models.BlobHttpHeaders
            {
                ContentType = file.ContentType
            }
        });

        // Return the blob URL
        return Results.Ok(new 
        { 
            success = true, 
            fileName = fileName,
            url = blobClient.Uri.ToString(),
            size = file.Length,
            contentType = file.ContentType
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error uploading file: {ex.Message}");
    }
}).DisableAntiforgery();

app.Run();