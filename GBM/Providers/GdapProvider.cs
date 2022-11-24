// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PartnerLed.Model;
using PartnerLed.Utility;
using System.Net;

namespace PartnerLed.Providers
{
    internal class GdapProvider : IGdapProvider
    {
        /// <summary>
        /// Provider Instances
        /// </summary>
        private readonly ITokenProvider tokenProvider;
        private readonly ILogger<GdapProvider> logger;
        private readonly IExportImportProviderFactory exportImportProviderFactory;

        /// <summary>
        /// GDAP provider constructor.
        /// </summary>
        public GdapProvider(ITokenProvider tokenProvider, AppSetting appSetting, IExportImportProviderFactory exportImportProviderFactory, ILogger<GdapProvider> logger)
        {
            this.tokenProvider = tokenProvider;
            this.logger = logger;
            this.exportImportProviderFactory = exportImportProviderFactory;
            protectedApiCallHelper = new ProtectedApiCallHelper(appSetting.Client);
            GdapBaseEndpoint = appSetting.GdapBaseEndPoint;
        }

        /// <summary>
        /// Base endpoint for Traffic Manager
        /// </summary>
        private string GdapBaseEndpoint { get; set; }

        protected ProtectedApiCallHelper protectedApiCallHelper;

        /// <summary>
        /// URLs of the protected Web APIs to call GDAP (here Traffic Manager endpoints)
        /// </summary>
        private string WebApiUrlAllGdaps { get { return $"{GdapBaseEndpoint}/v1/delegatedAdminRelationships"; } }

        private async Task<string?> getToken(Resource resource)
        {
            var authenticationResult = await tokenProvider.GetTokenAsync(resource);
            return authenticationResult?.AccessToken;
        }

        private async Task<DelegatedAdminRelationship?> PostGdapRelationship(string url, DelegatedAdminRelationship delegatedAdminRelationship)
        {
            try
            {
                logger
                    .LogInformation($"GDAP Request:\n{delegatedAdminRelationship.DisplayName}-{delegatedAdminRelationship.Customer.TenantId}\n{JsonConvert.SerializeObject(delegatedAdminRelationship.AccessDetails.UnifiedRoles)}\n");
                var accessToken = await getToken(Resource.TrafficManager);
                var response = await protectedApiCallHelper.CallWebApiPostAndProcessResultAsync(url, accessToken,
                    JsonConvert.SerializeObject(delegatedAdminRelationship, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }).ToString());

                string userResponse = GetUserResponse(response.StatusCode);
                Console.WriteLine($"{delegatedAdminRelationship.DisplayName} - {userResponse}");

                logger
                  .LogInformation($"GDAP Response:\n{delegatedAdminRelationship.DisplayName} {delegatedAdminRelationship.Customer.TenantId}\n {response.Content.ReadAsStringAsync().Result} \n");

                var relationshipObject = response.IsSuccessStatusCode
                       ? JsonConvert.DeserializeObject<DelegatedAdminRelationship>(response.Content.ReadAsStringAsync().Result)
                       : new DelegatedAdminRelationship()
                       {
                           DisplayName = delegatedAdminRelationship.DisplayName,
                           Id = string.Empty,
                           Duration = delegatedAdminRelationship.Duration,
                           Customer = new DelegatedAdminRelationshipCustomerParticipant() { DisplayName = delegatedAdminRelationship.Customer.DisplayName, TenantId = delegatedAdminRelationship.Customer.TenantId },
                           Partner = new DelegatedAdminRelationshipParticipant() { TenantId = delegatedAdminRelationship.Partner.TenantId }
                       };
                return relationshipObject;
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message);
                Console.WriteLine($"{delegatedAdminRelationship.DisplayName} - Unexpected error.");
                return new DelegatedAdminRelationship()
                {
                    DisplayName = delegatedAdminRelationship.DisplayName,
                    Id = string.Empty,
                    Duration = delegatedAdminRelationship.Duration,
                    Customer = new DelegatedAdminRelationshipCustomerParticipant() { DisplayName = delegatedAdminRelationship.Customer.DisplayName, TenantId = delegatedAdminRelationship.Customer.TenantId },
                    Partner = new DelegatedAdminRelationshipParticipant() { TenantId = delegatedAdminRelationship.Partner.TenantId }
                };
            }


        }

        private string GetUserResponse(HttpStatusCode statusCode)
        {
            switch (statusCode)
            {
                case HttpStatusCode.OK:
                case HttpStatusCode.Created:
                    return "Created.";
                case HttpStatusCode.Conflict: return "GDAP Relationship name already exits.";
                case HttpStatusCode.NotFound: return "GDAP relationship is already created but User does not have permissions to approve a relationship.";
                case HttpStatusCode.Forbidden: return "Please check if DAP relationship exists with the Customer or \nif GDAP relationship alredy exists then User has permissions to approve a relationship.";
                case HttpStatusCode.Unauthorized: return "Unauthorized. Please make sure your Sign-in credentials are correct and MFA enabled.";
                case HttpStatusCode.BadRequest: return "Please check input setup for Customers and ADRoles.";
                default: return "Failed to create. The customer does not exist, or DAP relationship is missing.";
            }

        }

        /// <summary>
        /// Fetch of details of Gdap relationship.
        /// </summary>
        /// <param name="granularRelationshipId">Gdap relationshipId</param>
        /// <param name="accessToken">bearer token.</param>
        /// <param name="nextLink">For fetching paginated query.</param>
        /// <returns>GDAP relationship object</returns>
        private async Task<JObject?> GetGdapRelationships(string? nextLink = null)
        {
            var url = WebApiUrlAllGdaps;
            if (string.IsNullOrEmpty(nextLink))
            {
                url = $"{url}?$count=true";
            }
            else { url = nextLink; }
            var accessToken = await getToken(Resource.TrafficManager);
            var response = await protectedApiCallHelper.CallWebApiAndProcessResultAsync(url, accessToken);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    throw new Exception("Unauthorized. Please make sure your Sign-in credentials are correct and MFA enabled.");
                }
            }
            return JsonConvert.DeserializeObject<JObject>(response.Content.ReadAsStringAsync().Result);
        }

        /// <summary>
        /// Fetch of details of Gdap relationship.
        /// </summary>
        /// <param name="granularRelationshipId">Gdap relationshipId</param>
        /// <param name="accessToken">bearer token.</param>
        /// <param name="nextLink">For fetching paginated query.</param>
        /// <returns>GDAP relationship object</returns>
        private async Task<DelegatedAdminRelationship?> GetGdapRelationship(string granularRelationshipId)
        {
            try
            {
                var url = WebApiUrlAllGdaps;
                if (!string.IsNullOrEmpty(granularRelationshipId))
                {
                    url = $"{url}/{granularRelationshipId}";
                }
                else
                {
                    throw new Exception("GDAP relationship id missing.");
                }

                var accessToken = await getToken(Resource.TrafficManager);
                var response = await protectedApiCallHelper.CallWebApiAndProcessResultAsync(url, accessToken);
                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        throw new Exception("Unauthorized. Please make sure your Sign-in credentials are correct and MFA enabled.");
                    }
                }
                return JsonConvert.DeserializeObject<DelegatedAdminRelationship>(response.Content.ReadAsStringAsync().Result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message);
                Console.WriteLine($"{granularRelationshipId} - Unexpected error.");
                return new DelegatedAdminRelationship()
                {
                    Id = granularRelationshipId,
                    Status = DelegatedAdminRelationshipStatus.Approved
                };
            }
        }

        /// <summary>
        ///  Track and update the status of existing GDAP relationship status.
        /// </summary>
        /// <param name="type">Export type "Json" or "Csv" based on user selection.</param>
        /// <returns></returns>
        public async Task<bool> RefreshGDAPRequestAsync(ExportImport type)
        {
            try
            {
                var exportImportProvider = exportImportProviderFactory.Create(type);
                var path = $"{Constants.InputFolderPath}/gdapRelationship/gdapRelationship.{Helper.GetExtenstion(type)}";
                var gDapRelationshipList = await exportImportProvider.ReadAsync<DelegatedAdminRelationship>(path);
                Console.WriteLine($"Reading files @ {path}");

                if (gDapRelationshipList == null)
                {
                    Console.WriteLine(" Error while Processing the input. No input provided. Please check the input file.");
                    Console.WriteLine($"Check the path {path}");
                }
                var inputRequest = gDapRelationshipList?.Where(x => x.Status == DelegatedAdminRelationshipStatus.Approved);
                var remainingDataList = gDapRelationshipList?.Where(x => x.Status != DelegatedAdminRelationshipStatus.Approved);

                if (inputRequest == null)
                {
                    Console.WriteLine(" Error while Processing the input. Incorrect data provided for processing. Please check the input file.");
                    Console.WriteLine($"Check the path {path}");
                }

                IEnumerable<string>? gdapIdList = inputRequest?.Select(p => p.Id.ToString());
                var responseList = new List<DelegatedAdminRelationship>();
                Console.WriteLine("Refreshing relationship(s) status...");
                var options = new ParallelOptions()
                {
                    MaxDegreeOfParallelism = 10
                };
                protectedApiCallHelper.setHeader(false);
                if (gdapIdList.Any())
                {
                    await Parallel.ForEachAsync(gdapIdList, options, async (gdapId, cancellationToken) =>
                    {
                        responseList.Add(await GetGdapRelationship(gdapId));
                    });
                }

                if (remainingDataList != null)
                {
                    responseList.AddRange(remainingDataList);
                }

                Console.WriteLine($"Downloaded latest statuses of GDAP Relationship(s) at {Constants.InputFolderPath}//gdapRelationship/gdapRelationship.{Helper.GetExtenstion(type)}");
                await exportImportProvider.WriteAsync(responseList, $"{Constants.InputFolderPath}/gdapRelationship/gdapRelationship.{Helper.GetExtenstion(type)}");
            }
            catch (IOException ex)
            {
                logger.LogError(ex.Message);
                Console.WriteLine("Make sure the file is closed before running the operation.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message);
            }

            return true;
        }

        /// <summary>
        ///  Create GDAP relatioship Object.
        /// </summary>
        /// <param name="type">Export type "Json" or "Csv" based on user selection.</param>
        /// <returns></returns>
        public async Task<bool> CreateGDAPRequestAsync(ExportImport type)
        {
            try
            {
                var exportImportProvider = exportImportProviderFactory.Create(type);
                var inputCustomer = await exportImportProvider.ReadAsync<DelegatedAdminRelationshipRequest>($"{Constants.InputFolderPath}/customers.{Helper.GetExtenstion(type)}");
                var inputAdRole = await exportImportProvider.ReadAsync<ADRole>($"{Constants.InputFolderPath}/ADRoles.{Helper.GetExtenstion(type)}");

                IEnumerable<UnifiedRole> roleList = inputAdRole.Select(x => new UnifiedRole() { RoleDefinitionId = x.Id.ToString() });

                var option = Helper.UserConfirmation($"Warning: There are {inputAdRole.Count} roles configured for creating GDAP relationship(s), are you sure you want to continue?");
                if (!option)
                {
                    return true;
                }
                var gdapList = new List<DelegatedAdminRelationship>();
                foreach (var item in inputCustomer)
                {
                    var gdapRelationship = new DelegatedAdminRelationship()
                    {
                        Customer = new DelegatedAdminRelationshipCustomerParticipant() { TenantId = item.CustomerTenantId, DisplayName = item.OrganizationDisplayName },
                        Partner = new DelegatedAdminRelationshipParticipant() { TenantId = item.PartnerTenantId.ToString() },
                        DisplayName = item.Name.ToString(),
                        Duration = $"P{item.Duration}D",
                        AccessDetails = new DelegatedAdminAccessDetails() { UnifiedRoles = roleList },
                    };
                    gdapList.Add(gdapRelationship);

                }

                Console.WriteLine("Creating new relationship(s)...");
                var url = $"{WebApiUrlAllGdaps}/migrate"; ;
                var allgdapRelationList = new List<DelegatedAdminRelationship>();

                var options = new ParallelOptions()
                {
                    MaxDegreeOfParallelism = 5
                };
                protectedApiCallHelper.setHeader(false);
                await Parallel.ForEachAsync(gdapList, options, async (g, cancellationToken) =>
                {
                    allgdapRelationList.Add(await PostGdapRelationship(url, g));
                });


                Console.WriteLine("Downloading GDAP Relationship(s)...");
                await exportImportProvider.WriteAsync(allgdapRelationList, $"{Constants.InputFolderPath}/gdapRelationship/gdapRelationship.{Helper.GetExtenstion(type)}");
                Console.WriteLine($"Downloaded new GDAP Relationship(s) at {Constants.InputFolderPath}/gdapRelationship/gdapRelationship.{Helper.GetExtenstion(type)}");
            }
            catch (IOException ex)
            {
                logger.LogError(ex.Message);
                Console.WriteLine("Make sure all input file(s) are closed before running the operation.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error occurred while save the Granular Relationship");
                logger.LogError(ex.Message);
            }
            return true;
        }

        /// <summary>
        /// Export all GDAP relationship for partner tenant.
        /// </summary>
        /// <param name="type">Export type "Json" or "Csv" based on user selection.</param>
        /// <returns></returns>
        public async Task<bool> GetAllGDAPAsync(ExportImport type)
        {
            try
            {
                var exportImportProvider = exportImportProviderFactory.Create(type);
                var gdapList = new List<DelegatedAdminRelationship>();
                var nextLink = string.Empty;
                Console.WriteLine("Downloading relationship(s)...");
                protectedApiCallHelper.setHeader(false);
                do
                {
                    var response = await GetGdapRelationships(nextLink);
                    var nextData = response.Properties().Where(p => p.Name.Contains("nextLink"));
                    nextLink = string.Empty;
                    if (nextData != null)
                    {
                        nextLink = (string?)nextData.FirstOrDefault();
                        if (!string.IsNullOrEmpty(nextLink))
                        {
                            Uri nextUri = new Uri(nextLink);
                            nextLink = WebApiUrlAllGdaps + nextUri.Query;
                        }
                    }
                    foreach (JProperty? child in response.Properties().Where(p => !p.Name.StartsWith("@")))
                    {
                        gdapList.AddRange(child.Value.Select(item => item.ToObject<DelegatedAdminRelationship>()));
                    }

                } while (!string.IsNullOrEmpty(nextLink));

                await exportImportProvider.WriteAsync(gdapList, $"{Constants.OutputFolderPath}/ExistingGdapRelationship.{Helper.GetExtenstion(type)}");
                Console.WriteLine($"Downloaded existing GDAP relationship(s) at {Constants.OutputFolderPath}/ExistingGdapRelationship.{Helper.GetExtenstion(type)}");
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message);
            }
            return true;
        }
    }
}
