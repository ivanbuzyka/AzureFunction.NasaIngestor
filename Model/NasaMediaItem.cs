using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace NasaIngestor.Model
{
    public class NasaMediaItem
    {
        [JsonProperty("href")]
        public string DetailsHref { get; set; }
        public IEnumerable<NasaMetaDataItem> Data { get; set; }
        public string OriginalUrl { get; set; }
    }

    public class NasaMetaDataItem
    {
        [JsonProperty("nasa_id")]
        public string NasaId { get; set; }
        public string Description { get; set; }
        public string Title { get; set; }
        [JsonProperty("media_type")]
        public string MediaType { get; set; }
    }
}
