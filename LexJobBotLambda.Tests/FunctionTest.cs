using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Xunit;
using Amazon.Lambda.Core;
using Amazon.Lambda.TestUtilities;
using Amazon.Lambda.LexEvents;

using Newtonsoft.Json;

using LexJobBotLambda;

namespace LexJobBotLambda.Tests
{
    public class FunctionTest
    {
        [Fact]
        public void FindJobTest()
        {
            var json = File.ReadAllText("find-job.json");

            var lexEvent = JsonConvert.DeserializeObject<LexEvent>(json);

            var function = new Function();
            var context = new TestLambdaContext();
            var response = function.FunctionHandler(lexEvent, context);
        }        
    }
}
