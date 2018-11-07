using System;
using System.Collections.Generic;
using System.Text;

namespace LexJobBotLambda.Models
{
    public class JobSearchResults
    {
        public List<JobDocument> ResultItems { get; set; }

        public long? TotalResults { get; set; }

        public JobSearchResults()
        {
            ResultItems = new List<JobDocument>();
        }
    }
}
