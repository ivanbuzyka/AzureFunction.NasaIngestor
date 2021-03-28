using System;
using System.IO;
using System.Threading.Tasks;
using System.Net.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Text;
using System.Net;
using Newtonsoft.Json.Linq;
using System.Linq;
using NasaIngestor.Model;

namespace NasaIngestor.Function
{
    public static class IngestNasaMediaToQueue
    {
        [FunctionName("IngestNasaMediaToQueue")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            // The query "Free text search terms to compare to all indexed metadata" https://images.nasa.gov/docs/images.nasa.gov_api_docs.pdf
            string namequery = req.Query["query"];
            
            // Media types to restrict the search to. Available types: [“image”, “audio”]. Separate multiple values with commas.
            string mediaType = req.Query["mediaType"];

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            namequery = namequery ?? data?.query;
            mediaType = mediaType ?? data?.mediaType;

            var result = await NasaSendSearchRequest(namequery, mediaType);

            // string responseMessage = string.IsNullOrEmpty(name)
            //     ? "This HTTP triggered function executed successfully. Pass a name in the query string or in the request body for a personalized response."
            //     : $"Hello, {name}. This HTTP triggered function executed successfully.";



            return new OkObjectResult("Done");
        }

        public static async Task<string> NasaSendSearchRequest(string query, string mediaType)
        {
            var result = string.Empty;
            using (var client = new HttpClient())
            {
                var decodedString = $"q={WebUtility.UrlEncode(query)}&media_type={mediaType}";
                var builder = new UriBuilder("https://images-api.nasa.gov/search");
                builder.Query = decodedString;

                var response = await client.GetAsync(builder.Uri);
                result = await response.Content.ReadAsStringAsync();

                // getting right part of json to deserialize
                JObject nasaSearchResponse = JObject.Parse(result);
                IList<JToken> results = nasaSearchResponse["collection"]["items"].Children().ToList();
                IList<NasaMediaItem> searchResults = new List<NasaMediaItem>();

                // de-serializing objects
                foreach (JToken r in results)
                {
                    // todo: make use of fan-in fan-out durable functions to paralellize this, to perform it faster 
                    NasaMediaItem mediaItem = r.ToObject<NasaMediaItem>();
                    mediaItem = await EnrichWithOriginalMediaHref(mediaItem);
                    searchResults.Add(mediaItem);
                }

                // example of deserializing to the dynamic object
                //dynamic nasaResponse = JsonConvert.DeserializeObject(result);
                //var detailsUri = nasaResponse.collection.items[0].href;
            }

            return result;
        }

        public static async Task<NasaMediaItem> EnrichWithOriginalMediaHref(NasaMediaItem mediaItem)
        {
            using (var client = new HttpClient())
            {
                var response = await client.GetAsync(mediaItem.DetailsHref);
                var result = await response.Content.ReadAsStringAsync();

                dynamic urlsList = JsonConvert.DeserializeObject(result);
                string originalUrl = urlsList[0];
                mediaItem.OriginalUrl = string.IsNullOrEmpty(originalUrl) ? string.Empty : originalUrl;
            }
            
            return mediaItem;
        }
    }
}
