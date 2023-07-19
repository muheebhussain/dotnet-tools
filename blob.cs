using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System.Text.Json;

public class Program
{
    public static void Main()
    {
        var connectionString = "your_connection_string";
        var containerName = "your_container_name";
        var blobName = "your_blob_name";

        var data = new YourDataClass
        {
            // Initialize your data here
        };

        UploadData(connectionString, containerName, blobName, data).Wait();
    }

    private static async Task UploadData(string connectionString, string containerName, string blobName, YourDataClass data)
    {
        // Create a BlobServiceClient object which will be used to create a container client
        BlobServiceClient blobServiceClient = new BlobServiceClient(connectionString);

        // Create a unique name for the container
        BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);

        // Create the container and return a container client object
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);

        // Get a reference to a blob
        BlobClient blobClient = containerClient.GetBlobClient(blobName);

        Console.WriteLine("Uploading to Blob storage as blob:\n\t {0}\n", blobClient.Uri);

        // Serialize data to JSON and upload
        var jsonString = JsonSerializer.Serialize(data);
        using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(jsonString));
        await blobClient.UploadAsync(memoryStream, true);
        memoryStream.Close();
    }
}
