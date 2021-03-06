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

        private static readonly string INPUT_IMAGE_STORAGE_ACCOUNT_CONNECTION = Environment.GetEnvironmentVariable("INPUT_IMAGE_STORAGE_ACCOUNT_CONNECTION");

        // Specify the features to return from Computer Vision
        private static readonly IList<VisualFeatureTypes?> features =
            new List<VisualFeatureTypes?>()
        {
            VisualFeatureTypes.Categories, VisualFeatureTypes.Description,
            VisualFeatureTypes.ImageType, VisualFeatureTypes.Tags
        };

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
        [StorageAccount("INPUT_IMAGE_STORAGE_ACCOUNT_CONNECTION")]
        public static async Task Run([EventGridTrigger]EventGridEvent eventGridEvent, 
        [Blob("{data.url}", FileAccess.Read)]Stream fileInput, ExecutionContext context,
        ILogger log)
        {
            log.LogInformation("Function started and received data: " + eventGridEvent);

            if (fileInput != null)
            {
                log.LogInformation("Function received file data from input binding");

                //extract the storage blob specific event data from the incoming request
                object createdEvent = null;
                eventGridEvent.TryGetSystemEventData(out createdEvent);
                var storageEvent = (StorageBlobCreatedEventData)createdEvent;

                // determine if we have an encoder for the image type
                var extension = Path.GetExtension(storageEvent.Url);
                var encoder = GetEncoder(extension);

                MemoryStream localCopy = new MemoryStream();

                fileInput.CopyTo(localCopy);

                localCopy.Position = 0;

                if (encoder != null)
                {
                    ComputerVisionClient computerVision = new ComputerVisionClient(
                        new ApiKeyServiceClientCredentials(Environment.GetEnvironmentVariable("COMPUTER_VISION_KEY")),
                        new System.Net.Http.DelegatingHandler[] { });
            
                    // Add your Computer Vision endpoint to your environment variables.
                    computerVision.Endpoint = Environment.GetEnvironmentVariable("COMPUTER_VISION_ENDPOINT");

                    log.LogInformation("Image being analyzed by Computer Vision...");

                    ImageAnalysis analysis = await computerVision.AnalyzeImageInStreamAsync(localCopy, features);

                    if(analysis.Description != null)
                    {
                       log.LogInformation($"First caption is: {analysis.Description.Captions[0].Text}"); 
                    }

                    var thumbContainerName = Environment.GetEnvironmentVariable("THUMBNAIL_CONTAINER_NAME");
                   
                    // build blob client so we can write out the new image.
                    var blobServiceClient = new BlobServiceClient(INPUT_IMAGE_STORAGE_ACCOUNT_CONNECTION);
                    var blobContainerClient = blobServiceClient.GetBlobContainerClient(thumbContainerName);
                    var blobName = GetBlobNameFromUrl(storageEvent.Url);


                    FontCollection collection = new();
                    FontFamily family = collection.Add($"{context.FunctionAppDirectory}\\Evolventa-zLXL.ttf");
                    Font font = family.CreateFont(30, FontStyle.Regular); 

                    log.LogInformation("Image being written to Azure Storage...");

                    // reset Stream
                    fileInput.Position = 0;

                    using (var output = new MemoryStream())
                    using (var image = Image.Load(fileInput))
                    {   
                        image.Mutate(x => x.Resize(0, image.Height * 3));
                        image.Mutate(x => x.DrawText(analysis.Description.Captions[0].Text, font, Color.White, new PointF(1,1)));
                        image.Save(output, encoder);
                        output.Position = 0;
                        await blobContainerClient.UploadBlobAsync(blobName, output);
                    }
                }
                else
                {
                    log.LogInformation($"No encoder support for: {storageEvent.Url}");
                }
            }     
            else
            {
                log.LogInformation($"No file input found.");
            }       
        }
    }
}