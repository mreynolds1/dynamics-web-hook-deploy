using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Crm.Sdk.Samples.HelperCode;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Xml;
using System.Net;

namespace Deploy
{
    class Program
    {
        private HttpClient httpClient;

        static void Main(string[] args)
        {
            Program app = new Program();
            try
            {
                String[] arguments = Environment.GetCommandLineArgs();
                app.ConnectToCRMAndRegisterWebHook(arguments);
            }
            catch (System.Exception ex)
            {
                DisplayException(ex);
            }
            finally
            {
                if (app.httpClient != null)
                {
                    app.httpClient.Dispose();
                }
            }
        }

        private void ConnectToCRMAndRegisterWebHook(String[] cmdargs)
        {
            Configuration config = null;
            if (cmdargs.Length > 0)
                config = new FileConfiguration("default");

            Authentication auth = new Authentication(config);
            httpClient = new HttpClient(auth.ClientHandler, true);
            httpClient.BaseAddress = new Uri(config.ServiceUrl + "api/data/v9.1/");
            httpClient.Timeout = new TimeSpan(0, 2, 0);
            httpClient.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
            httpClient.DefaultRequestHeaders.Add("OData-Version", "4.0");
            httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            //create a service endpoint
            var webhookUrl = "https://requestbin.fullcontact.com/1ei5sfk1";
            // header values -- not needed for request bin but added here to demonstrate the xml structure
            XmlDocument xmlDocument = new XmlDocument();
            XmlElement xmlElement = xmlDocument.CreateElement("settings");
            XmlElement xmlElement1 = xmlDocument.CreateElement("setting");
            xmlElement1.SetAttribute("name", "Test-Auth-Header");
            xmlElement1.SetAttribute("value", "SECRETKEY");
            xmlElement.AppendChild(xmlElement1);
            xmlDocument.AppendChild(xmlElement);
            var authValue = xmlDocument.InnerXml.ToString();

            // replace with desired name for web hook service endpoint
            var webhookName = Guid.NewGuid().ToString();

            var serviceEndpoint = new JObject();
            serviceEndpoint.Add("authvalue", authValue);
            serviceEndpoint.Add("authtype", 5);
            serviceEndpoint.Add("connectionmode", 1);
            serviceEndpoint.Add("contract", 8);
            serviceEndpoint.Add("name", webhookName);
            serviceEndpoint.Add("url", webhookUrl);

            string jsonContent = serviceEndpoint.ToString(); // for debug

            StringContent requestContent = new StringContent(serviceEndpoint.ToString(), Encoding.UTF8, "application/json");
            var response = httpClient.PostAsync("serviceendpoints", requestContent).Result;

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("The create service endpoint request failed with a status of '{0}'",
                       response.ReasonPhrase);
            }
            else if (response.Headers.Contains("OData-EntityId"))
            {
                var serviceendpointUrl = response.Headers.GetValues("OData-EntityId").FirstOrDefault();
                serviceendpointUrl = serviceendpointUrl.Substring(serviceendpointUrl.LastIndexOf('/'));

                // Retrieve the plugintype, sdkmessage, and sdkmessagefilter data we will need to create the webhook triggers
                JObject webhookPluginType;
                webhookPluginType = RetrieveSingleRecord("plugintypes", new string[] { "plugintypeid" }, "name eq 'Microsoft.Crm.Servicebus.WebHookPlugin'");

                JObject updateMessage;
                updateMessage = RetrieveSingleRecord("sdkmessages", new string[] { "sdkmessageid" }, "name eq 'Update'");

                JObject updateMessageFilter;
                updateMessageFilter = RetrieveSingleRecord(
                    "sdkmessagefilters",
                    new string[] { "sdkmessagefilterid" },
                    $"primaryobjecttypecode eq 'opportunity' and sdkmessageid/sdkmessageid eq { updateMessage["sdkmessageid"] }");

                JObject createMessage;
                createMessage = RetrieveSingleRecord("sdkmessages", new string[] { "sdkmessageid" }, "name eq 'Create'");

                JObject createMessageFilter;
                createMessageFilter = RetrieveSingleRecord(
                    "sdkmessagefilters",
                    new string[] { "sdkmessagefilterid" },
                    $"primaryobjecttypecode eq 'opportunity' and sdkmessageid/sdkmessageid eq { createMessage["sdkmessageid"] }");


                // Processing steps that should trigger the webhook
                // Trigger on update of opportunity fields name (topic) and budgetamount
                var updateStep = new JObject();
                updateStep.Add("asyncautodelete", false); // Change to true for production!!
                updateStep.Add("filteringattributes", "name,budgetamount");
                updateStep.Add("mode", 1);
                updateStep.Add("name", $"{webhookName} opportunity update");
                updateStep.Add("description", $"{webhookName} opportunity update");
                updateStep.Add("rank", 1);
                updateStep.Add("stage", 40);
                updateStep.Add("supporteddeployment", 0);
                updateStep.Add("sdkmessagefilterid@odata.bind", $"/sdkmessagefilters({ updateMessageFilter["sdkmessagefilterid"].ToString() })");
                updateStep.Add("sdkmessageid@odata.bind", $"/sdkmessages({ updateMessage["sdkmessageid"].ToString() })");
                updateStep.Add("eventhandler_serviceendpoint@odata.bind", serviceendpointUrl);

                jsonContent = updateStep.ToString(); // for debug

                requestContent = new StringContent(updateStep.ToString(), Encoding.UTF8, "application/json");
                response = httpClient.PostAsync("sdkmessageprocessingsteps", requestContent).Result;
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine("The update opportunity trigger request failed with a status of '{0}'",
                           response.ReasonPhrase);
                }

                var createStep = new JObject();
                createStep.Add("asyncautodelete", false); // Change to true for production!!
                createStep.Add("mode", 1);
                createStep.Add("name", $"{webhookName} opportunity create");
                createStep.Add("description", $"{webhookName} opportunity create");
                createStep.Add("rank", 1);
                createStep.Add("stage", 40);
                createStep.Add("supporteddeployment", 0);
                createStep.Add("sdkmessagefilterid@odata.bind", $"/sdkmessagefilters({ createMessageFilter["sdkmessagefilterid"].ToString() })");
                createStep.Add("sdkmessageid@odata.bind", $"/sdkmessages({ createMessage["sdkmessageid"].ToString() })");
                createStep.Add("plugintypeid@odata.bind", $"/plugintypes({ webhookPluginType["plugintypeid"].ToString() })");
                createStep.Add("eventhandler_serviceendpoint@odata.bind", serviceendpointUrl);

                jsonContent = createStep.ToString(); // for debug

                requestContent = new StringContent(createStep.ToString(), Encoding.UTF8, "application/json");
                response = httpClient.PostAsync("sdkmessageprocessingsteps", requestContent).Result;
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine("The create opportunity trigger request failed with a status of '{0}'",
                           response.ReasonPhrase);
                }
            }

        }

        private JObject RetrieveSingleRecord(string entityPluralName, string[] properties, string filterCriteria)
        {
            if(properties == null || properties.Length < 1 || string.IsNullOrWhiteSpace(filterCriteria))
            {
                throw new Exception("Must supply filter criteria and at least one property");
            }

            JObject result;

            var queryOptions = "?$select=" + String.Join(",", properties) + "&$filter=" + filterCriteria;
            var queryResponse = httpClient.GetAsync(entityPluralName + queryOptions).Result;
            if (queryResponse.StatusCode == HttpStatusCode.OK) //200  
            {
                JObject body = JsonConvert.DeserializeObject<JObject>
                        (queryResponse.Content.ReadAsStringAsync().Result);
                result = (JObject)body["value"][0];
            }
            else
            {
                throw new CrmHttpResponseException(queryResponse.Content);
            }

            return result;
        }

        private static void DisplayException(Exception ex)
        {
            Console.WriteLine("The application terminated with an error.");
            Console.WriteLine(ex.Message);
            while (ex.InnerException != null)
            {
                Console.WriteLine("\t* {0}", ex.InnerException.Message);
                ex = ex.InnerException;
            }
        }
    }
}
