using Microsoft.Crm.Sdk.Samples.HelperCode;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace CrmHelper
{
    public static class CrmAPIHelper
    {
        public static HttpClient CreateConnection(String[] cmdargs)
        {
            Configuration config = null;
            if (cmdargs.Length > 0)
                config = new FileConfiguration("default");

            Authentication auth = new Authentication(config);
            var httpClient = new HttpClient(auth.ClientHandler, true);
            httpClient.BaseAddress = new Uri(config.ServiceUrl + "api/data/v9.1/");
            httpClient.Timeout = new TimeSpan(0, 2, 0);
            httpClient.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
            httpClient.DefaultRequestHeaders.Add("OData-Version", "4.0");
            httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            return httpClient;
        }


        public static JObject RetrieveSingleRecord(HttpClient httpClient, string entityPluralName, string[] properties, string filterCriteria)
        {
            if (properties == null || properties.Length < 1)
            {
                throw new Exception("Must at least one select property");
            }

            JObject result;

            var queryOptions = "?$select=" + String.Join(",", properties);
            if (!String.IsNullOrWhiteSpace(filterCriteria))
            {
                queryOptions = queryOptions + "&$filter=" + filterCriteria;
            }
            var queryResponse = httpClient.GetAsync(entityPluralName + queryOptions).Result;
            if (queryResponse.StatusCode == HttpStatusCode.OK) //200  
            {
                JObject body = JsonConvert.DeserializeObject<JObject>
                        (queryResponse.Content.ReadAsStringAsync().Result);

                if(!body.ContainsKey("value") || body["value"] == null || !body["value"].Any())
                {
                    result = null;
                }
                else
                {
                    result = (JObject)body["value"][0];
                }
            }
            else
            {
                throw new CrmHttpResponseException(queryResponse.Content);
            }

            return result;
        }
    }
}
