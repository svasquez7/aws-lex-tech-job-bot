using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon;
using Amazon.Comprehend;
using Amazon.Comprehend.Model;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.Lambda.Core;
using Amazon.Lambda.LexEvents;
using LexJobBotLambda.Models;
using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;
using RestSharp;

namespace LexJobBotLambda
{
    public class FindJobIntentProcessor : AbstractIntentProcessor
    {
        private const string BASE_URL = "https://atlas.microsoft.com";
        private const string AZURE_MAP_KEY = "";
        private const string AWS_KEY = "";
        private const string AZURE_SEARCH_KEY = "";
        private const string ACURE_SEARCH_SERVICE_NAME = "";
        private const string AWS_SECRET = "";
        public const string LOCATION_SLOT = "Location";
        public const string PROFESSION_SLOT = "​Profession";
        public const string SKILLS_SLOT = "Skills";
        public const string PERFECT_JOB_SLOT = "PefectJob";
        private const float MINIMUM_THRESHOLD_SCORE = 0.9F;




        public override LexResponse Process(LexEvent lexEvent, ILambdaContext context)
        {
            var slots = lexEvent.CurrentIntent.Slots;
            var sessionAttributes = lexEvent.SessionAttributes ?? new Dictionary<string, string>();

            JobCriteria jobCriteria = new JobCriteria
            {
                LocationInput = slots.ContainsKey(LOCATION_SLOT) ? slots[LOCATION_SLOT] : null,
                Profession = slots.ContainsKey(PROFESSION_SLOT) ? slots[PROFESSION_SLOT] : null,
                Skills = slots.ContainsKey(SKILLS_SLOT) ? slots[SKILLS_SLOT].Split(',').ToList() : null,
                PerfectJob = slots.ContainsKey(PERFECT_JOB_SLOT) ? slots[PERFECT_JOB_SLOT] : null,
                KeyPhrases = slots.ContainsKey(PERFECT_JOB_SLOT) ? GetKeyPhrases(slots[PERFECT_JOB_SLOT]) : null
            };         
            
            if(slots.ContainsKey(LOCATION_SLOT))
            {
                var geoLocation = FuzzyGeoCode(slots[LOCATION_SLOT]).Result;
                if(geoLocation != null && geoLocation.Longitude != 0 && geoLocation.Latitude != 0)
                {
                    jobCriteria.Longitude = geoLocation.Longitude;
                    jobCriteria.Latituede = geoLocation.Latitude;
                    jobCriteria.LocationInput = geoLocation.FormattedLocation;
                }
            }
            else
            {
                var geoLocation = FuzzyGeoCode("aurora, co").Result;
                if (geoLocation != null && geoLocation.Longitude != 0 && geoLocation.Latitude != 0)
                {
                    jobCriteria.Longitude = geoLocation.Longitude;
                    jobCriteria.Latituede = geoLocation.Latitude;
                    jobCriteria.LocationInput = geoLocation.FormattedLocation;
                }
            }

            sessionAttributes[CURRENT_JOB_CRITERIA_SESSION_ATTRIBUTE] = SerializeJobCriteria(jobCriteria);

            var jobs = SearchJobs(jobCriteria);
            var content = "DERP! I didn't find any jobs that are a fit for you right now.";

            if(jobs.ResultItems.Any())
            {
                var job = jobs.ResultItems.FirstOrDefault();
                var desciption = job.description.Substring(0, 500);
                var cutOffIndex = desciption.LastIndexOf(' ');
                content = string.Format("I found your next job. {0} located in {1}. {2}... To apply visit {3}",
                job.jobTitle, job.locationText, desciption.Remove(cutOffIndex).Replace("&nbsp;", string.Empty), job.jobUrl);
            }

            return Close(
                        sessionAttributes,
                        "Fulfilled",
                        new LexResponse.LexMessage
                        {
                            ContentType = MESSAGE_CONTENT_TYPE,
                            Content = content
                        }
                    );
        }

        private List<string> GetKeyPhrases(string text)
        {
            List<string> keyPhrases = new List<string>();
            if (text.Length > 2500)
                text = text.Substring(0, 2499);

            AmazonComprehendClient comprehendClient = new AmazonComprehendClient(AWS_KEY, AWS_SECRET, Amazon.RegionEndpoint.USWest2);

            // Call DetectKeyPhrases API
            Console.WriteLine("Calling DetectKeyPhrases");
            DetectKeyPhrasesRequest detectKeyPhrasesRequest = new DetectKeyPhrasesRequest()
            {
                Text = text,
                LanguageCode = "en"
            };

            DetectKeyPhrasesResponse detectKeyPhrasesResponse = comprehendClient.DetectKeyPhrasesAsync(detectKeyPhrasesRequest).Result;

            if (detectKeyPhrasesResponse.HttpStatusCode == System.Net.HttpStatusCode.OK && detectKeyPhrasesResponse.KeyPhrases != null)
            {
                keyPhrases = (from kp in detectKeyPhrasesResponse.KeyPhrases
                              where kp.Score >= MINIMUM_THRESHOLD_SCORE
                              select kp.Text).ToList();
            }

            return keyPhrases;
        }

        public async Task<GeoCodedLocation> FuzzyGeoCode(string text)
        {
            
            GeoCodedLocation geoCodedLocation = null;
            try
            {
                text = text.ToLower().Trim();
                IAmazonDynamoDB clientDynamoDb = new AmazonDynamoDBClient(AWS_KEY, AWS_SECRET, RegionEndpoint.USWest2);
                DynamoDBContext context = new DynamoDBContext(clientDynamoDb);

                geoCodedLocation = await context.LoadAsync<GeoCodedLocation>(text);
                if (geoCodedLocation != null)
                {
                    geoCodedLocation.Source = "Cache";
                    return geoCodedLocation;
                }

                var client = new RestClient(BASE_URL);

                var request = new RestRequest(string.Format("/search/fuzzy/json?query={0}, USA&api-version=1.0&subscription-key={1}", text, AZURE_MAP_KEY),
                    Method.GET);
                request.AddHeader("Accept", "application/json, text/json, text/x-json");

                var typedRestData = client.Execute<GeoCodeResults>(request);

                if (typedRestData.Data != null && typedRestData.StatusCode == System.Net.HttpStatusCode.OK && typedRestData.Data.results.Any())
                {
                    geoCodedLocation = BuildLocation(typedRestData.Data, text);
                    geoCodedLocation.CacheDate = DateTime.Today;
                    await context.SaveAsync(geoCodedLocation);
                    geoCodedLocation.Source = "Geocoder";

                    return geoCodedLocation;
                }
            }
            catch (Exception ex)
            {
               
            }
            return null;
        }

        private JobSearchResults SearchJobs(JobCriteria searchParms)
        {
            var credential = new SearchCredentials(AZURE_SEARCH_KEY);
            SearchServiceClient serviceClient = new SearchServiceClient(ACURE_SEARCH_SERVICE_NAME, credential);
            var indexClient = serviceClient.Indexes.GetClient("denvertechjobs");

            var sp = new SearchParameters
            {
                ScoringProfile = (searchParms.Latituede != 0 && searchParms.Latituede != 0) ? "geo" : "newAndHighlyRated",
                IncludeTotalResultCount = true,
                SearchMode = SearchMode.All,
                //Filter = "trioType eq 'Food'"
            };

            if (searchParms.Latituede != 0 && searchParms.Latituede != 0)
            {
                var scoreParam = new ScoringParameter("currentLocation", Microsoft.Spatial.GeographyPoint.Create((double)searchParms.Latituede, (double)searchParms.Longitude));
                sp.ScoringParameters = new List<ScoringParameter> { scoreParam };
            }

            var searchText = string.Join('|', searchParms.KeyPhrases) + "+" + string.Join('|', searchParms.Skills) + "+" + searchParms.Profession;

            DocumentSearchResult<JobDocument> response = indexClient.Documents.Search<JobDocument>(searchText, sp);

            var results = new JobSearchResults { TotalResults = response.Count };
            if (response.Results.Any())
            {
                var docs = (from d in response.Results select d.Document).ToList();
                results.ResultItems.AddRange(docs);
            }

            return results;

        }

        private GeoCodedLocation BuildLocation(GeoCodeResults results, string geocodeText)
        {
            GeoCodedLocation location = null;
            var result = results.results.FirstOrDefault();

            location = new GeoCodedLocation
            {
                ID = result.id,
                CountryCode = result.address.countryCode,
                FormattedLocation = result.address.freeformAddress,
                Latitude = (decimal)result.position.lat,
                Longitude = (decimal)result.position.lon,
                GeocodeTerm = geocodeText,
                PostalCode = result.address.postalCode,
                StateCode = result.address.countrySubdivision,
                City = result.address.municipality
            };



            if (!string.IsNullOrEmpty(result.address.streetNumber) && !string.IsNullOrEmpty(result.address.streetName))
            {
                location.Address = string.Format("{0} {1}", result.address.streetNumber, result.address.streetName);
            }

            location.Precision = MapPrecision(result).ToString();
            return location;
        }

        private GeocodePrecision MapPrecision(Result result)
        {
            if (result.entityType == "Country")
                return GeocodePrecision.Country;

            if (result.entityType == "PostalCodeArea")
                return GeocodePrecision.PostalCode;

            if (result.entityType == "Municipality")
                return GeocodePrecision.City;

            if (result.entityType == "CountrySubdivision")
                return GeocodePrecision.State;

            if (result.type == "Point Address")
                return GeocodePrecision.StreetAddress;

            return GeocodePrecision.Unknown;
        }
    }
}
