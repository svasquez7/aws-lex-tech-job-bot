using System;
using System.Collections.Generic;
using System.Text;
using Amazon.DynamoDBv2.DataModel;

namespace LexJobBotLambda.Models
{
    [DynamoDBTable("GeoCodeCache")]
    public class GeoCodedLocation
    {
        public string ID { get; set; }

        [DynamoDBHashKey]
        public string GeocodeTerm { get; set; }

        public string FormattedLocation { get; set; }

        public string City { get; set; }

        public string StateCode { get; set; }

        public string CountryCode { get; set; }

        public decimal Latitude { get; set; }

        public decimal Longitude { get; set; }

        public string PostalCode { get; set; }

        public string Address { get; set; }

        public string Precision { get; set; }

        public DateTime CacheDate { get; set; }

        public string Source { get; set; }
    }

    public enum GeocodePrecision
    {
        Unknown = 0,
        StreetAddress = 1,
        PostalCode = 2,
        State = 3,
        Country = 4,
        City = 5
    }
}
