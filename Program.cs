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
app.MapPost("/upload-image", async (IFormFile file, BlobServiceClient blobServiceClient, ILogger<Program> logger) =>
{
    try
    {
        logger.LogInformation("Starting image upload process");
        logger.LogInformation("BlobServiceClient URI: {Uri}", blobServiceClient.Uri);
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
        logger.LogInformation("Getting container client for 'images' container");
        var containerClient = blobServiceClient.GetBlobContainerClient("images");
        
        logger.LogInformation("Attempting to create container if it doesn't exist");
        await containerClient.CreateIfNotExistsAsync(Azure.Storage.Blobs.Models.PublicAccessType.None);
        logger.LogInformation("Container creation/verification completed");

        // Upload file to blob storage
        logger.LogInformation("Creating blob client for file: {FileName}", fileName);
        var blobClient = containerClient.GetBlobClient(fileName);
        
        logger.LogInformation("Starting file upload to blob storage");
        using var stream = file.OpenReadStream();
        await blobClient.UploadAsync(stream, new Azure.Storage.Blobs.Models.BlobUploadOptions
        {
            HttpHeaders = new Azure.Storage.Blobs.Models.BlobHttpHeaders
            {
                ContentType = file.ContentType
            }
        });
        logger.LogInformation("File upload completed successfully");

        // Generate SAS URL for secure access (valid for 7 days)
        logger.LogInformation("Generating SAS URL for blob access");
        var sasUrl = blobClient.GenerateSasUri(Azure.Storage.Sas.BlobSasPermissions.Read, 
            DateTimeOffset.UtcNow.AddDays(7));
        
        logger.LogInformation("Upload process completed successfully. SAS URL generated.");
        
        // Return the SAS URL
        return Results.Ok(new 
        { 
            success = true, 
            fileName = fileName,
            url = sasUrl.ToString(),
            size = file.Length,
            contentType = file.ContentType,
            expiresAt = DateTimeOffset.UtcNow.AddDays(7)
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error occurred during image upload: {ErrorMessage}", ex.Message);
        logger.LogError("Exception type: {ExceptionType}", ex.GetType().Name);
        logger.LogError("Stack trace: {StackTrace}", ex.StackTrace);
        
        if (ex.InnerException != null)
        {
            logger.LogError("Inner exception: {InnerException}", ex.InnerException.Message);
        }
        
        return Results.Problem($"Error uploading file: {ex.Message}");
    }
}).DisableAntiforgery();

app.Run();