using System.Net;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace CoffeeNChill.Functions.Functions;

public class DocumentFunctions
{
    private readonly BlobContainerClient _containerClient;

    public DocumentFunctions()
    {
        string connectionString =
            Environment.GetEnvironmentVariable("AzureWebJobsStorage")
            ?? "UseDevelopmentStorage=true";

        var blobServiceClient =
            new BlobServiceClient(connectionString);

        _containerClient =
            blobServiceClient.GetBlobContainerClient("staff-docs");

        _containerClient.CreateIfNotExists();
    }


    // POST /api/documents/upload
    [Function("UploadStaffDocument")]
    public async Task<HttpResponseData> UploadStaffDocument(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "post",
            Route = "documents/upload")]
        HttpRequestData req)
    {
        var contentType =
            req.Headers.TryGetValues(
                "Content-Type",
                out var values)
                ? values.FirstOrDefault()
                : null;

        if (contentType == null ||
            !contentType.StartsWith("multipart/form-data"))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);

            await bad.WriteStringAsync(
                "Request must use multipart/form-data.");

            return bad;
        }

        // For the assignment demonstration, the request body
        // can be streamed to a blob.

        var fileName =
            $"document-{DateTime.UtcNow:yyyyMMddHHmmss}.bin";

        var blob =
            _containerClient.GetBlobClient(fileName);

        await blob.UploadAsync(
            req.Body,
            overwrite: true);

        var response =
            req.CreateResponse(HttpStatusCode.Created);

        await response.WriteAsJsonAsync(new
        {
            fileName,
            message = "Document uploaded successfully."
        });

        return response;
    }


    // GET /api/documents
    [Function("ListStaffDocuments")]
    public async Task<HttpResponseData> ListStaffDocuments(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "documents")]
        HttpRequestData req)
    {
        var documents = new List<object>();

        await foreach (var blob in _containerClient.GetBlobsAsync())
        {
            documents.Add(new
            {
                fileName = blob.Name,
                size = blob.Properties.ContentLength,
                lastModified = blob.Properties.LastModified
            });
        }

        var response = req.CreateResponse(HttpStatusCode.OK);

        await response.WriteAsJsonAsync(documents);

        return response;
    }


    // GET /api/documents/download/{fileName}
    [Function("DownloadStaffDocument")]
    public async Task<HttpResponseData> DownloadStaffDocument(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "documents/download/{fileName}")]
        HttpRequestData req,
        string fileName)
    {
        var blob =
            _containerClient.GetBlobClient(fileName);

        if (!await blob.ExistsAsync())
        {
            var notFound =
                req.CreateResponse(HttpStatusCode.NotFound);

            await notFound.WriteStringAsync(
                "Document not found.");

            return notFound;
        }

        var download =
            await blob.DownloadStreamingAsync();

        var response =
            req.CreateResponse(HttpStatusCode.OK);

        response.Headers.Add(
            "Content-Type",
            download.Value.Details.ContentType
            ?? "application/octet-stream");

        response.Headers.Add(
            "Content-Disposition",
            $"attachment; filename=\"{fileName}\"");

        await download.Value.Content.CopyToAsync(
            response.Body);

        return response;
    }
}