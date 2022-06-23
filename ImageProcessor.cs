// Default URL for triggering event grid function in the local environment.
// http://localhost:7071/runtime/webhooks/EventGrid?functionName={functionname}
using Azure.Storage.Blobs;
using Azure.Messaging.EventGrid.Models;
using Azure.Messaging.EventGrid;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Azure.WebJobs.Extensions.Storage;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Extensions;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.Fonts;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;
using System.Collections.Generic;
using Azure.Messaging.EventGrid.SystemEvents;
using Azure.Storage;

namespace SiliconValve.DemoFunctions
{
    public static class ImageProcessor
    {

        private static readonly string IMAGE_STORAGE_ACCOUNT_NAME = Environment.GetEnvironmentVariable("IMAGE_STORAGE_ACCOUNT");
        private static readonly string IMAGE_STORAGE_ACCOUNT_KEY = Environment.GetEnvironmentVariable("IMAGE_STORAGE_ACCOUNT_KEY");

        private static string GetBlobNameFromUrl(string bloblUrl)
        {
            var uri = new Uri(bloblUrl);
            var blobClient = new BlobClient(uri);
            return blobClient.Name;
        }

        private static IImageEncoder GetEncoder(string extension)
        {
            IImageEncoder encoder = null;

            extension = extension.Replace(".", "");

            var isSupported = Regex.IsMatch(extension, "gif|png|jpe?g", RegexOptions.IgnoreCase);

            if (isSupported)
            {
                switch (extension.ToLower())
                {
                    case "png":
                        encoder = new PngEncoder();
                        break;
                    case "jpg":
                        encoder = new JpegEncoder();
                        break;
                    case "jpeg":
                        encoder = new JpegEncoder();
                        break;
                    case "gif":
                        encoder = new GifEncoder();
                        break;
                    default:
                        break;
                }
            }

            return encoder;
        }


        [FunctionName("ImageProcessor")]
        public static async Task Run([EventGridTrigger]EventGridEvent eventGridEvent, 
        [Blob("{data.url}", FileAccess.Read)] Stream fileInput, 
        ILogger log)
        {
            log.LogInformation(eventGridEvent.Data.ToString());

             if (fileInput != null)
            {
                var createdEvent = ((JObject)eventGridEvent.Data.ToString()).ToObject<StorageBlobCreatedEventData>();
                var extension = Path.GetExtension(createdEvent.Url);
                var encoder = GetEncoder(extension);

                if (encoder != null)
                {
                    
                    var thumbnailWidth = Convert.ToInt32(Environment.GetEnvironmentVariable("THUMBNAIL_WIDTH"));
                    var thumbContainerName = Environment.GetEnvironmentVariable("THUMBNAIL_CONTAINER_NAME");

                    var blobServiceClient = new BlobServiceClient(new Uri($"{IMAGE_STORAGE_ACCOUNT_NAME}blob.core.windows.net"),new StorageSharedKeyCredential(IMAGE_STORAGE_ACCOUNT_NAME, IMAGE_STORAGE_ACCOUNT_KEY));
                    var blobContainerClient = blobServiceClient.GetBlobContainerClient(thumbContainerName);
                    var blobName = GetBlobNameFromUrl(createdEvent.Url);

                    FontCollection collection = new();
                    FontFamily family = collection.Add("./FivoSans-Black.otf");
                    Font font = family.CreateFont(12, FontStyle.Regular);


                    ComputerVisionClient computerVision = new ComputerVisionClient(
                        new ApiKeyServiceClientCredentials(Environment.GetEnvironmentVariable("COMPUTER_VISION_KEY")),
                        new System.Net.Http.DelegatingHandler[] { });
            
                    // Add your Computer Vision endpoint to your environment variables.
                    computerVision.Endpoint = Environment.GetEnvironmentVariable("COMPUTER_VISION_ENDPOINT");

                    log.LogInformation("Image being analyzed by Computer Vision...");

                    ImageAnalysis analysis = await computerVision.AnalyzeImageInStreamAsync(fileInput);

                    // Image image = ...; // Create any way you like.

                    // The options are optional
                    TextOptions options = new(font)
                    {
                        Origin = new PointF(100, 100), // Set the rendering origin.
                        TabWidth = 8, // A tab renders as 8 spaces wide
                        WrappingLength = 100, // Greater than zero so we will word wrap at 100 pixels wide
                        HorizontalAlignment = HorizontalAlignment.Right // Right align
                    };

                    log.LogInformation("Image being written to Azure Storage...");

                    // reset Stream
                    fileInput.Position = 0;

                    using (var output = new MemoryStream())
                    using (var image = Image.Load(fileInput))
                    {
                        var divisor = image.Width / thumbnailWidth;
                        var height = Convert.ToInt32(Math.Round((decimal)(image.Height / divisor)));

                        image.Mutate(x => x.Resize(0, image.Height * 3));
                        image.Mutate(x => x.DrawText(analysis.Description.Captions[0].Text, font, Color.Red, new PointF(1,1)));
                        image.Save(output, encoder);
                        output.Position = 0;
                        await blobContainerClient.UploadBlobAsync(blobName, output);
                    }
                }
                else
                {
                    log.LogInformation($"No encoder support for: {createdEvent.Url}");
                }
            }            
        }
    }
}