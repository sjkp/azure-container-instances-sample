using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using System;
using System.IO;
using Newtonsoft.Json;
using System.Text;

namespace Azure.Function.ACI.Deployment
{
    public static class DeployAci
    {
        const string ResourceGroupApiVersion = "2015-01-01";
        const string TemplateVersion = "2016-02-01";

        static string ClientId = Environment.GetEnvironmentVariable("ClientId");
        static string ClientSecret = Environment.GetEnvironmentVariable("ClientSecret");
        static string TenantId = Environment.GetEnvironmentVariable("TenantId");
        static string SubscriptionId = Environment.GetEnvironmentVariable("SubscriptionId");


        [FunctionName("Deploy")]
        public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)]HttpRequestMessage req, TraceWriter log)
        {
            log.Info("C# HTTP trigger function processed a request." + ClientId);

            var httpClient = new HttpClient();
            //https://msdn.microsoft.com/en-us/library/azure/dn790546.aspx - Resource Group
            var token = await GetAuthorizationToken(httpClient);

            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer " + token);
            var rgName = "Sjkp.today.test";
            var location = "west europe";

            await CreateResourceGroup(httpClient, rgName, location);

            ////https://msdn.microsoft.com/en-us/library/azure/dn790549.aspx
            //if (ValidateDeployment(httpClient, rgName, location))
            //{

            //}

            var rgRes = await DeployResourceGroupTemplate(httpClient, rgName, location);
     
            var i = 0;
            var ip = "unknown";
            do
            {
                await Task.Delay(3000);
                var res = await CheckDeploymentStatus(httpClient, rgRes.id.ToString());
                if (res.properties.provisioningState == "Succeeded")
                {
                    ip = res.properties.outputs.containerIPv4Address.value;
                    i = 5;
                }
                else
                {
                    log.Info("Waiting for deploy trying again in 3 sec");
                }                
                i++;                
            } while (i < 5);

            return req.CreateResponse(HttpStatusCode.OK, ip);
        }

        private async static Task<dynamic> CheckDeploymentStatus(HttpClient client, string id)
        {
            var res = await client.GetAsync($"https://management.azure.com{id}?api-version={TemplateVersion}");
            return await res.Content.ReadAsAsync<dynamic>();
        }

        private async static Task<dynamic> DeployResourceGroupTemplate(HttpClient client, string rgName, string location)
        {
            var deploymentName = DateTime.UtcNow.Ticks.ToString();
            StringContent body = CreateBody();
            var res = await client.PutAsync($"https://management.azure.com/subscriptions/{SubscriptionId}/resourcegroups/{rgName}/providers/microsoft.resources/deployments/{deploymentName}?api-version={TemplateVersion}", body);
            var result = await res.Content.ReadAsAsync<dynamic>();
            return result;
        }

        private static bool ValidateDeployment(HttpClient client, string rgName, string location)
        {
            var deploymentName = DateTime.UtcNow.Ticks.ToString();
            StringContent body = CreateBody();
            var res = client.PostAsync($"https://management.azure.com/subscriptions/{SubscriptionId}/resourcegroups/{rgName}/providers/microsoft.resources/deployments/{deploymentName}/validate?api-version={TemplateVersion}", body).Result;
            var result = res.Content.ReadAsStringAsync().Result;
            Console.WriteLine(result);
            return res.StatusCode == System.Net.HttpStatusCode.OK;
        }

        private static StringContent CreateBody()
        {
            var template = File.ReadAllText("azuredeploy-aci.json");
            var parameters = JsonConvert.DeserializeObject<dynamic>(File.ReadAllText("azuredeploy.parameters.json")).parameters;
            var requestBody = BuildBody(template, parameters.ToString());
            var body = new StringContent(requestBody, Encoding.UTF8, "application/json");
            return body;
        }

        private static string BuildBody(string template, string parameters)
        {
            return string.Format(@"{{
                ""properties"": {{
                    ""template"": {0},
    ""mode"": ""Incremental"",
    ""parameters"": {1}
                }}
            }}", template, parameters);
        }

        private async static Task<string> CreateResourceGroup(HttpClient client, string name, string location)
        {
            var res = await client.PutAsync($"https://management.azure.com/subscriptions/{SubscriptionId}/resourcegroups/{name}?api-version={ResourceGroupApiVersion}",
                new StringContent(@"{""location"": 
""" + location + @""",  
        }", Encoding.UTF8, "application/json"));

            return await res.Content.ReadAsStringAsync();
        }

        private async static Task<string> GetAuthorizationToken(HttpClient client)
        {    
            var req = new HttpRequestMessage(HttpMethod.Post, $"https://login.microsoftonline.com/{TenantId}/oauth2/token");

            req.Headers.TryAddWithoutValidation("Content-Type", "application/x-www-form-urlencoded");
            req.Content = new StringContent($"grant_type=client_credentials&client_id={ClientId}&client_secret={ClientSecret}&resource=https%3A%2F%2Fmanagement.azure.com%2F", Encoding.UTF8, "application/x-www-form-urlencoded");
            var response = await client.SendAsync(req);
            var result = await response.Content.ReadAsAsync<dynamic>();
            return result.access_token;
        }
    }
}