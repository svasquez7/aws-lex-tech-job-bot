using System;
using System.Collections.Generic;
using System.Text;

namespace JobScraperLambda.Models
{
    public class JobDocument
    {
        public string id;
        public string jobTitle;
        public string locationText;
        public string description;
        public string employerName;
        public List<string> jobAttributes;
        public string jobUrl;
        public DateTime postedDate;
        public Location locationPoint;
        public List<string> skills;
        public List<string> keyPhrases;
    }

    public class Location
    {
        public string type { get; set; }

        public List<decimal> coordinates { get; set; }
    }
}
