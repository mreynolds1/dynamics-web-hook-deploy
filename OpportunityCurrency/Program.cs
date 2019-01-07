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
using CrmHelper;
using System.Globalization;

namespace OpportunityCurrency
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
                app.httpClient = CrmAPIHelper.CreateConnection(arguments);
                app.OpportunityCurrencyExample();
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

        private void OpportunityCurrencyExample()
        {
            StringContent requestContent;
            HttpResponseMessage response;

            // Issue WhoAmI request to get the organization ID
            response = httpClient.GetAsync("WhoAmI", HttpCompletionOption.ResponseContentRead).Result;
            var organizationId = JsonConvert.DeserializeObject<JObject>(response.Content.ReadAsStringAsync().Result)["OrganizationId"].ToString();

            // Retrieve the base currency ID for the organization
            response = httpClient.GetAsync($"organizations({ organizationId })?$expand=basecurrencyid($select=transactioncurrencyid,currencysymbol,exchangerate,isocurrencycode)").Result;
            var baseCurrency = (JObject)JsonConvert.DeserializeObject<JObject>(response.Content.ReadAsStringAsync().Result)["basecurrencyid"];

            // Retrieve the USD and CAD currencies
            response = httpClient.GetAsync("transactioncurrencies?$select=transactioncurrencyid,currencysymbol,isocurrencycode,exchangerate&$filter=isocurrencycode eq 'CAD' or isocurrencycode eq 'USD'").Result;
            JObject cad = null;
            JObject usd = null;
            if (response.StatusCode == HttpStatusCode.OK) //200  
            {
                JObject body = JsonConvert.DeserializeObject<JObject>
                        (response.Content.ReadAsStringAsync().Result);

                if (!body.ContainsKey("value") || body["value"] == null || !body["value"].Any() || body["value"].Count() != 2)
                {
                    throw new Exception("Could not retrieve currencies");
                }
                else
                {
                    foreach (var currency in body["value"])
                    {
                        if(currency["isocurrencycode"].ToString() == "USD")
                        {
                            usd = (JObject)currency;
                        }
                        else
                        {
                            cad = (JObject)currency;
                        }
                    }
                }

                if(usd == null || cad == null)
                {
                    throw new Exception("Could not retrieve currencies");
                }
            }
            else
            {
                throw new CrmHttpResponseException(response.Content);
            }


            // Create the potential customer for our opportunity if it does not already exist
            var accountName = "Canadian Account";
            string accountUrl = "";
            var account = CrmAPIHelper.RetrieveSingleRecord(httpClient, "accounts", new string[] { "accountid" }, $"name eq '{ accountName }'");
            if (account == null)
            {
                account = new JObject();
                account.Add("accountid", Guid.NewGuid());
                account.Add("name", accountName);
                account.Add("transactioncurrencyid@odata.bind", $"/transactioncurrencies({ cad["transactioncurrencyid"].ToString() })");

                requestContent = new StringContent(account.ToString(), Encoding.UTF8, "application/json");
                response = httpClient.PostAsync("accounts", requestContent).Result;

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"The request failed with a status of '{ response.ReasonPhrase }'");
                }
                else
                {
                    accountUrl = response.Headers.GetValues("OData-EntityId").FirstOrDefault();
                    accountUrl = accountUrl.Substring(accountUrl.LastIndexOf('/'));
                }
            }
            else
            {
                accountUrl = $"/accounts({ account["accountid"].ToString() })";
            }


            // Create an opportunity with an associated competitor
            var opportunityName = $"Canadian Opportunity { Guid.NewGuid().ToString() }";
            var opportunity = new JObject();
            opportunity.Add("name", opportunityName);
            opportunity.Add("budgetamount", 1000);
            opportunity.Add("parentaccountid@odata.bind", accountUrl);
            opportunity.Add("transactioncurrencyid@odata.bind", $"/transactioncurrencies({ cad["transactioncurrencyid"].ToString() })");

            // Create 3 competitors with the opportunity in a deep insert
            JArray competitors = new JArray();
            for (int i = 0; i < 3; i++)
            {
                var competitor = new JObject();
                competitor.Add("name", "Competitor " + Guid.NewGuid().ToString());
                competitors.Add(competitor);
            }
            opportunity.Add("opportunitycompetitors_association", competitors);

            /* example of the json body for this request:
            {
              "name": "Canadian Opportunity 1b28859f-4c9c-451a-b25a-67bf79bb8908",
              "budgetamount": 1000,
              "parentaccountid@odata.bind": "/accounts(585d24d8-9d7d-4553-b2b6-e8c225e24f69)",
              "transactioncurrencyid@odata.bind": "/transactioncurrencies(31aab109-0e05-e911-a95f-000d3a1ccf71)",
              "opportunitycompetitors_association": [
                {
                  "name": "Competitor ae862edf-2683-4bb1-b11c-4745052e4801"
                },
                {
                  "name": "Competitor 1a82e99d-0294-47a9-8c35-606ecdc85151"
                },
                {
                  "name": "Competitor 8699c39e-93ab-4566-9742-890d1ff3827b"
                }
              ]
            } 
            */

            requestContent = new StringContent(opportunity.ToString(), Encoding.UTF8, "application/json");
            response = httpClient.PostAsync("opportunities", requestContent).Result;

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"The request failed with a status of '{ response.ReasonPhrase }'");
            }
            else if (response.Headers.Contains("OData-EntityId"))
            {
                var opportunityUrl = response.Headers.GetValues("OData-EntityId").FirstOrDefault();

                // Retrieve the opportunity, expanding the competitors collection so we can get the identifiers for them. 
                // Let's also treat this like we are exporting an opportunity from Dynamics and we want to convert the 
                // currency to USD, so we'll also expand the transaction currency so we can get the up-to-date exchange rate
                response = httpClient.GetAsync(opportunityUrl + "?$select=name,budgetamount&$expand=transactioncurrencyid($select=exchangerate,isocurrencycode,currencysymbol),opportunitycompetitors_association($select=name,competitorid)").Result;

                opportunity = JsonConvert.DeserializeObject<JObject>(response.Content.ReadAsStringAsync().Result);
                Console.WriteLine($"Created opportunity \"{ opportunity["name"] }\" with ID { opportunity["opportunityid"] }");
                Console.WriteLine();

                competitors = (JArray)opportunity["opportunitycompetitors_association"];
                Console.WriteLine("Created competitors:");
                foreach (var competitorResult in competitors)
                {
                    var competitor = (JObject)competitorResult;
                    Console.WriteLine($"Competitor name: \"{ competitor["name"] }\", ID: { competitor["competitorid"] }");
                }
                Console.WriteLine();

                var opportunityCurrency = (JObject)opportunity["transactioncurrencyid"];
                Console.WriteLine($"The opportunity has a currency of { opportunityCurrency["isocurrencycode"] } and a budget amount of { opportunityCurrency["currencysymbol"] }{ opportunity["budgetamount"] }");
                Console.WriteLine();

                // Convert to the base currency
                var budgetAmount = Convert.ToDecimal(opportunity["budgetamount"]);
                var toBaseExchangeRate = Convert.ToDecimal(opportunityCurrency["exchangerate"]);
                var budgetInBaseCurrency = Math.Round(budgetAmount / toBaseExchangeRate, 2);
                Console.WriteLine($"Budget amount converted to base currency of { baseCurrency["isocurrencycode"] } is: { baseCurrency["currencysymbol"] }{ budgetInBaseCurrency }");

                // Convert to USD
                var toUsdExchangeRate = Convert.ToDecimal(usd["exchangerate"]);
                var budgetInUsd = budgetInBaseCurrency * toUsdExchangeRate;
                Console.WriteLine($"Budget amount converted to USD is: ${ budgetInUsd }");

            }

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
