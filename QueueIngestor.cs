using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using NasaIngestor.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NasaIngestor
{
    public static class QueueIngestor
    {
        [FunctionName("QueueIngestor")]
        public static async Task<List<NasaMediaItem>> RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var queryString = context.GetInput<string>();
            var parallelTasks = new List<Task<NasaMediaItem>>();

            var workBatch = await context.CallActivityAsync<List<NasaMediaItem>>("QueueIngestor_GetInitialMetadata", queryString);

            // create parallel calls
            foreach (var mediaItem in workBatch)
            {
                Task<NasaMediaItem> task = context.CallActivityAsync<NasaMediaItem>("QueueIngestor_EnrichNasaMetadata", mediaItem);
                parallelTasks.Add(task);
            }

            await Task.WhenAll(parallelTasks);

            //aggeragate result
            List<NasaMediaItem> result = parallelTasks.Select(t => t.Result).ToList();

            // write objects to the Service Bus here (or call third function to do that)

            // return object (will be serialized to json and then available by querying the orchestrator status)
            return result;
        }

        [FunctionName("QueueIngestor_EnrichNasaMetadata")]
        public static async Task<NasaMediaItem> EnrichNasaMetadata([ActivityTrigger] NasaMediaItem mediaItem, ILogger log)
        {
            log.LogInformation($"Started worker for {mediaItem.DetailsHref}");
            using (var client = new HttpClient())
            {
                var response = await client.GetAsync(mediaItem.DetailsHref);
                var result = await response.Content.ReadAsStringAsync();

                dynamic urlsList = JsonConvert.DeserializeObject(result);
                string originalUrl = urlsList[0];
                mediaItem.OriginalUrl = string.IsNullOrEmpty(originalUrl) ? string.Empty : originalUrl;
            }

            log.LogInformation($"Finished worker for {mediaItem.DetailsHref}");

            return mediaItem;
        }

        [FunctionName("QueueIngestor_HttpStart")]
        public static async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            string requestBody = await req.Content.ReadAsStringAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            string namequery = data?.query;
            string mediaType = data?.mediaType;
            //todo count (implement count as third parameter to get certain coutn of items)
            
            var decodedString = $"q={WebUtility.UrlEncode(namequery)}&media_type={mediaType}";

            string instanceId = await starter.StartNewAsync<string>("QueueIngestor", decodedString);

            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

            return starter.CreateCheckStatusResponse(req, instanceId);
        }

        [FunctionName("QueueIngestor_GetInitialMetadata")]
        public static async Task<List<NasaMediaItem>> GetInitialMetadata([ActivityTrigger] string query, ILogger log)
        {
            List<NasaMediaItem> searchResults = new List<NasaMediaItem>();
            using (var client = new HttpClient())
            {
                var builder = new UriBuilder("https://images-api.nasa.gov/search");
                builder.Query = query;

                var response = await client.GetAsync(builder.Uri);
                var result = await response.Content.ReadAsStringAsync();

                // getting right part of json to deserialize
                JObject nasaSearchResponse = JObject.Parse(result);
                IList<JToken> results = nasaSearchResponse["collection"]["items"].Children().ToList();

                // de-serializing objects
                foreach (JToken r in results)
                {   
                    NasaMediaItem mediaItem = r.ToObject<NasaMediaItem>();
                    searchResults.Add(mediaItem);
                }

                // example of deserializing to the dynamic object
                //dynamic nasaResponse = JsonConvert.DeserializeObject(result);
                //var detailsUri = nasaResponse.collection.items[0].href;
            }

            return searchResults;

        }
    }
}