using System;
using System.Collections.Generic;
using System.Text;

namespace LexJobBotLambda.Models
{
    public class JobCriteria
    {
        public string LocationInput { get; set; }
        public string Profession { get; set; }
        public List<string> Skills { get; set; }
        public string PerfectJob { get; set; }

        public List<string> KeyPhrases { get; set; }
        public decimal Longitude { get; set; }
        public decimal Latituede { get; set; }
    }
}
