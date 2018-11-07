using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JobScraperLambda.Models;

using Amazon.Lambda.Core;
using HtmlAgilityPack;
using ScrapySharp.Extensions;
using Amazon.Comprehend;
using Amazon.Comprehend.Model;
using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;
using System.Text.RegularExpressions;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace JobScraperLambda
{
    public class Function
    {
        private const string AWS_KEY = "";
        private const string AWS_SECRET = "+iEfSII";
        private const string AZURE_SEARCH_KEY = "";
        private const string ACURE_SEARCH_SERVICE_NAME = "";
        private const string DICE_BASE_URL = "https://www.dice.com";

        private const float MINIMUM_THRESHOLD_SCORE = 0.9F;

        Regex alpahNumericRegex = new Regex("[^a-zA-Z0-9 -]");

        /// <summary>
        /// A simple function that takes a string and does a ToUpper
        /// </summary>
        /// <param name="input"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public string FunctionHandler(string input, ILambdaContext context)
        {
            var currentPage = 1;
            var totalIndexed = 0;
            var totalError = 0;
            var totalJobs = 0;

            while(currentPage <= 10)
            {
                var url = string.Format("https://www.dice.com/jobs/sort-date-pc-true-l-80013-radius-40-startPage-{0}-jobs", currentPage);
                var webGet = new HtmlWeb();

                if (webGet.Load(url) is HtmlDocument document)
                {
                    var nodes = document.DocumentNode.CssSelect(".complete-serp-result-div").ToList();
                    var jobDocuments = new List<JobDocument>();
                    foreach (var node in nodes)
                    {
                        totalJobs++;
                        try
                        {
                            var link = node.CssSelect(".list-inline a").FirstOrDefault();
                            var href = link.Attributes["href"].Value;
                            var job = BuildJob(href);

                            jobDocuments.Add(job);                            
                        }
                        catch (Exception ex)
                        {
                            totalError++;
                        }

                        totalIndexed++;
                    }

                    PostDocumentBatch(jobDocuments);
                    currentPage++;
                }
            }            

            return string.Format("Finished - Total Jobs: {2}, Total jobs indexe: {0}, Total Errors: {1}", totalIndexed, totalError, totalJobs);
        }

        private JobDocument BuildJob(string relativeUrl)
        {
            JobDocument job = null;
            

            var url = string.Format("{0}{1}", DICE_BASE_URL, relativeUrl);
            var webGet = new HtmlWeb();

            if (webGet.Load(url) is HtmlDocument document)
            {
                var schemaOrgNodes = document.DocumentNode.SelectNodes(".//*[@itemprop]").ToList();

                var urlNode = schemaOrgNodes.Where(x => x.OuterHtml.Contains("itemprop=\"url\"")).FirstOrDefault();

                if(urlNode != null)
                {
                    job = new JobDocument();

                    job.id = alpahNumericRegex.Replace(urlNode.Attributes["data-jobid"].Value, "");
                    job.jobUrl = string.Format("{0}{1}", DICE_BASE_URL, urlNode.Attributes["content"].Value);
                }
                else
                {
                    return null;
                }

                var datePostedNode = schemaOrgNodes.Where(x => x.OuterHtml.Contains("itemprop=\"datePosted\"")).FirstOrDefault();
                if(datePostedNode != null)
                {
                    DateTime dt;
                    if (DateTime.TryParse(datePostedNode.Attributes["content"].Value, out dt))
                        job.postedDate = dt;
                }

                var titleNode = schemaOrgNodes.Where(x => x.OuterHtml.Contains("itemprop=\"title\"")).FirstOrDefault();
                if (titleNode != null)
                {
                    job.jobTitle = titleNode.InnerHtml;
                }

                var descriptionNode = schemaOrgNodes.Where(x => x.OuterHtml.Contains("itemprop=\"description\"") && x.Name.ToLower() == "div").FirstOrDefault();
                if (descriptionNode != null)
                {
                    job.description = RemoveUnwantedTags(descriptionNode.InnerHtml.Replace("\n", string.Empty).Replace("\t", string.Empty).Replace("\r", string.Empty));
                }

                var empNameNode = schemaOrgNodes.Where(x => x.OuterHtml.Contains("itemprop=\"name\"") && x.Name.ToLower() == "span").FirstOrDefault();
                if (empNameNode != null)
                {
                    job.employerName = empNameNode.InnerHtml.Replace("\n", string.Empty).Replace("\t", string.Empty).Replace("\r", string.Empty);
                }

                var city = string.Empty;
                var state = string.Empty;
                var zipCode = string.Empty;
                var cityNode = schemaOrgNodes.Where(x => x.OuterHtml.Contains("itemprop=\"addressLocality\"") && x.Name == "meta").FirstOrDefault();
                if (cityNode != null)
                {
                    city = cityNode.Attributes["content"].Value;
                }

                var stateNode = schemaOrgNodes.Where(x => x.OuterHtml.Contains("itemprop=\"addressRegion\"") && x.Name == "meta").FirstOrDefault();
                if (stateNode != null)
                {
                    state = stateNode.Attributes["content"].Value;
                }

                var postalNode = schemaOrgNodes.Where(x => x.OuterHtml.Contains("itemprop=\"postalCode\"") && x.Name == "meta").FirstOrDefault();
                if (postalNode != null)
                {
                    zipCode = postalNode.Attributes["content"].Value;
                }

                job.locationText = string.Format("{0} {1} {2}", city, state, zipCode);

                var latNode = schemaOrgNodes.Where(x => x.OuterHtml.Contains("itemprop=\"latitude\"") && x.Name == "meta").FirstOrDefault();
                var lonNode = schemaOrgNodes.Where(x => x.OuterHtml.Contains("itemprop=\"longitude\"") && x.Name == "meta").FirstOrDefault();
                if (latNode != null && lonNode != null)
                {
                    job.locationPoint = new Location { type = "Point", coordinates = new List<decimal> { decimal.Parse(lonNode.Attributes["content"].Value), decimal.Parse(latNode.Attributes["content"].Value) } };
                    
                }

                var skillsNode = schemaOrgNodes.Where(x => x.OuterHtml.Contains("itemprop=\"skills\"")).FirstOrDefault();
                if (skillsNode != null)
                {
                    job.skills = skillsNode.InnerHtml.Replace("\n", string.Empty).Replace("\t", string.Empty).Replace("\r", string.Empty).Split(',').ToList();
                }

                job.keyPhrases = DetectKeyPhrases(job.description);

            }

            return job;
        }

        private List<string> DetectKeyPhrases(string text)
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

            if(detectKeyPhrasesResponse.HttpStatusCode == System.Net.HttpStatusCode.OK && detectKeyPhrasesResponse.KeyPhrases != null)
            {
                keyPhrases = (from kp in detectKeyPhrasesResponse.KeyPhrases
                              where kp.Score >= MINIMUM_THRESHOLD_SCORE
                              select kp.Text).ToList();
            }

            return keyPhrases;
        }

        private string RemoveUnwantedTags(string data)
        {
            if (string.IsNullOrEmpty(data)) return string.Empty;

            var document = new HtmlDocument();
            document.LoadHtml(data);

            var nodes = new Queue<HtmlNode>(document.DocumentNode.SelectNodes("./*|./text()"));
            while (nodes.Count > 0)
            {
                var node = nodes.Dequeue();
                var parentNode = node.ParentNode;

                if (node.Name != "#text")
                {
                    var childNodes = node.SelectNodes("./*|./text()");

                    if (childNodes != null)
                    {
                        foreach (var child in childNodes)
                        {
                            nodes.Enqueue(child);
                            parentNode.InsertBefore(child, node);
                        }
                    }

                    parentNode.RemoveChild(node);
                }
            }

            return document.DocumentNode.InnerHtml;
        }

        private static void PostDocumentBatch(List<JobDocument> documents)
        {
            var credential = new SearchCredentials(AZURE_SEARCH_KEY);
            SearchServiceClient serviceClient = new SearchServiceClient(ACURE_SEARCH_SERVICE_NAME, credential);

            var indexClient = serviceClient.Indexes.GetClient("denvertechjobs");

            var batch = IndexBatch.MergeOrUpload(documents);
            indexClient.Documents.Index(batch);
        }
    }

    
}
