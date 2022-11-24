using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using PartnerLed.Model;
using PartnerLed.Utility;
using System.Net;

namespace PartnerLed.Providers
{
    internal class AccessAssignmentProvider : IAccessAssignmentProvider
    {
        /// <summary>
        /// The token provider.
        /// </summary>
        private readonly ITokenProvider tokenProvider;
        private readonly ILogger<AccessAssignmentProvider> logger;
        private readonly IExportImportProviderFactory exportImportProviderFactory;

        /// <summary>
        /// AccessAssignment provider constructor.
        /// </summary>
        public AccessAssignmentProvider(ITokenProvider tokenProvider, AppSetting appSetting, IExportImportProviderFactory exportImportProviderFactory, ILogger<AccessAssignmentProvider> logger)
        {
            this.tokenProvider = tokenProvider;
            this.logger = logger;
            this.exportImportProviderFactory = exportImportProviderFactory;
            protectedApiCallHelper = new ProtectedApiCallHelper(appSetting.Client);
            GraphBaseEndpoint = appSetting.MicrosoftGraphBaseEndpoint;
            GdapBaseEndpoint = appSetting.GdapBaseEndPoint;
        }

        protected ProtectedApiCallHelper protectedApiCallHelper;

        /// <summary>
        /// Base endpoint for Traffic Manager
        /// </summary>
        private string GdapBaseEndpoint { get; set; }

        /// <summary>
        /// Base endpoint for Graph
        /// </summary>
        private string GraphBaseEndpoint { get; set; }

        /// <summary>
        /// URLs of the protected Web APIs to call Graph Endpoint
        /// </summary>
        private string graphEndpoint { get { return $"{GraphBaseEndpoint}/v1.0/groups?$filter=securityEnabled+eq+true&$select=id,displayName"; } }

        /// <summary>
        /// URLs of the protected Web APIs to call GDAP (here Traffic Manager endpoints)
        /// </summary>
        private string WebApiUrlAllGdaps { get { return $"{GdapBaseEndpoint}/v1/delegatedAdminRelationships"; } }

        private async Task<string?> getToken(Resource resource)
        {
            var authenticationResult = await tokenProvider.GetTokenAsync(resource);
            return authenticationResult?.AccessToken;
        }

        /// <summary>
        /// Generate the Security Group list from Graph Endpoint.
        /// </summary>
        /// <returns> true </returns>
        public async Task<bool> ExportSecurityGroup(ExportImport type)
        {
            try
            {
                var url = graphEndpoint;
                var nextLink = string.Empty;
                var securityGroup = new List<SecurityGroup?>();

                Console.WriteLine("Getting Security Groups");
                protectedApiCallHelper.setHeader(true);

                do
                {
                    var accessToken = await getToken(Resource.GraphManager);
                    if (!string.IsNullOrEmpty(nextLink))
                    {
                        url = nextLink;
                    }

                    var response = await protectedApiCallHelper.CallWebApiAndProcessResultAsync(url, accessToken);
                    if (response != null && response.IsSuccessStatusCode)
                    {
                        var result = JsonConvert.DeserializeObject(response.Content.ReadAsStringAsync().Result) as JObject;
                        var nextData = result.Properties().Where(p => p.Name.Contains("nextLink"));
                        nextLink = string.Empty;
                        if (nextData != null)
                        {
                            nextLink = (string?)nextData.FirstOrDefault();
                        }

                        foreach (JProperty child in result.Properties().Where(p => !p.Name.StartsWith("@")))
                        {
                            securityGroup.AddRange(child.Value.Select(item => item.ToObject<SecurityGroup>()));
                        }
                    }
                    else
                    {
                        string userResponse = "Failed to get Security Groups.";
                        Console.WriteLine($"{userResponse}");
                    }
                } while (!string.IsNullOrEmpty(nextLink));
                var writer = this.exportImportProviderFactory.Create(type);
                await writer.WriteAsync(securityGroup, $"{Constants.OutputFolderPath}/securityGroup.{Helper.GetExtenstion(type)}");
                Console.WriteLine($"Downloaded Security Groups at {Constants.OutputFolderPath}/securityGroup.{Helper.GetExtenstion(type)}");
            }

            catch (Exception ex)
            {
                logger.LogError(ex.Message);
            }
            return true;

        }

        /// <summary>
        /// Track and update the status of existing Access Assignment objects.
        /// </summary>
        /// <returns>true </returns>
        public async Task<bool> RefreshAccessAssignmentRequest(ExportImport type)
        {
            try
            {
                var exportImportProvider = exportImportProviderFactory.Create(type);
                var accessAssignmentFilepath = $"{Constants.InputFolderPath}/accessAssignment/accessAssignment.{Helper.GetExtenstion(type)}";
                var accessAssignmentList = await exportImportProvider.ReadAsync<DelegatedAdminAccessAssignmentRequest>(accessAssignmentFilepath);
                Console.WriteLine($"Reading files @ {accessAssignmentFilepath}");
                var inputRequest = accessAssignmentList?.Where(x => x.Status.ToLower() != "active");
                var remainingDataList = accessAssignmentList?.Where(x => x.Status.ToLower() == "active");

                if (inputRequest == null)
                {
                    Console.WriteLine(" Error while Processing the input. Incorrect data provided for processing. Please check the input file.");
                    Console.WriteLine($"Check the path {accessAssignmentFilepath}");
                }

                var responseList = new List<DelegatedAdminAccessAssignmentRequest>();
                Console.WriteLine("Updating Access Assignment..");
                protectedApiCallHelper.setHeader(false);

                var options = new ParallelOptions()
                {
                    MaxDegreeOfParallelism = 10
                };
                if (inputRequest.Any())
                {
                    await Parallel.ForEachAsync(inputRequest, options, async (x, cancellationToken) =>
                    {
                        responseList.Add(await GetDelegatedAdminAccessAssignment(x.GdapRelationshipId, x.AccessAssignmentId));
                    });
                }

                if (remainingDataList != null)
                {
                    responseList.AddRange(remainingDataList);
                }

                Console.WriteLine("Downloading Access Assignment(s)...");
                await exportImportProvider.WriteAsync(responseList, $"{Constants.InputFolderPath}/accessAssignment/accessAssignment.{Helper.GetExtenstion(type)}");
                Console.WriteLine($"Downloaded Access Assignment(s) at {Constants.InputFolderPath}/accessAssignment/accessAssignment.{Helper.GetExtenstion(type)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error occurred while save the Access Assignment");
                logger.LogError(ex.Message);
            }

            return true;
        }


        /// <summary>
        /// Create bulk Delegated Admin relationship access assignment.
        /// </summary>
        /// <returns>true </returns>
        public async Task<bool> CreateAccessAssignmentRequestAsync(ExportImport type)
        {
            try
            {
                var exportImportProvider = exportImportProviderFactory.Create(type);
                var gDapFilepath = $"{Constants.InputFolderPath}/gdapRelationship/gdapRelationship.{Helper.GetExtenstion(type)}";
                var gDapRelationshipList = await exportImportProvider.ReadAsync<DelegatedAdminRelationship>(gDapFilepath);
                Console.WriteLine($"Reading files @ {gDapFilepath}");

                var azureRoleFilePath = $"{Constants.InputFolderPath}/ADRoles.{Helper.GetExtenstion(type)}";
                Console.WriteLine($"Reading files @ {azureRoleFilePath}");
                var inputAdRole = await exportImportProvider.ReadAsync<ADRole>(azureRoleFilePath);

                var securityRolepath = $"{Constants.InputFolderPath}/securityGroup.{Helper.GetExtenstion(type)}";
                Console.WriteLine($"Reading files @ {securityRolepath}");
                var securityGroupList = await exportImportProvider.ReadAsync<SecurityGroup>(securityRolepath);

                if (securityGroupList == null)
                {
                    throw new Exception($"No security groups found in gdapbulkmigration/securitygroup.{Helper.GetExtenstion(type)}");
                }

                if (securityGroupList.Any(item => string.IsNullOrEmpty(item.CommaSeperatedRoles)))
                {
                    throw new Exception($"One or more security groups do not have roles mapped in gdapbulkmigration/securitygroup.{Helper.GetExtenstion(type)}");
                }

                var option = Helper.UserConfirmation($"Waring: There are {securityGroupList.Count} Security Groups configured for Access Assignment, are you sure you want to continue with this?");
                if (!option)
                {
                    return true;
                }
                var list = new List<DelegatedAdminAccessAssignment>();
                var responseList = new List<DelegatedAdminAccessAssignmentRequest>();
                // get the unique AD roles 
                list.AddRange(from SecurityGroup? item in securityGroupList select GetAdminAccessAssignmentObject(item.Id, item.Roles, inputAdRole));

                var inputList = gDapRelationshipList.Where(g => g.Status == DelegatedAdminRelationshipStatus.Active).ToList();

                try
                {
                    protectedApiCallHelper.setHeader(false);
                    foreach (var gdapRelationship in inputList)
                    {
                        var tasks = list?.Select(item => PostGranularAdminAccessAssignment(gdapRelationship.Id, item));
                        DelegatedAdminAccessAssignmentRequest?[] collection = await Task.WhenAll(tasks);
                        responseList.AddRange(collection);
                    }

                    if (responseList != null)
                    {
                        await exportImportProvider.WriteAsync(responseList, $"{Constants.InputFolderPath}/accessAssignment/accessAssignment.{Helper.GetExtenstion(type)}");
                        Console.WriteLine($"Downloaded Access Assignment(s) at {Constants.InputFolderPath}/accessAssignment/accessAssignment.{Helper.GetExtenstion(type)}");
                    }
                }
                catch
                {
                    Console.WriteLine($"Error occurred while save the Access Assignment");
                    throw;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message);
            }


            return true;
        }

        /// <summary>
        /// Create a Delegated Admin relationship access assignment.
        /// </summary>
        /// <param name="gdapRelationshipId"></param>
        /// <param name="token"></param>
        /// <param name="data"></param>
        /// <returns>DelegatedAdminAccessAssignmentRequest</returns>
        private async Task<DelegatedAdminAccessAssignmentRequest?> PostGranularAdminAccessAssignment(string gdapRelationshipId, DelegatedAdminAccessAssignment data)
        {
            try
            {
                var url = $"{WebApiUrlAllGdaps}/{gdapRelationshipId}/accessAssignments";

                logger.LogInformation($"Assignment Request:\n{gdapRelationshipId}\n{JsonConvert.SerializeObject(data.AccessDetails)}");
                var token = await getToken(Resource.TrafficManager);
                HttpResponseMessage response = await protectedApiCallHelper.CallWebApiPostAndProcessResultAsync(url, token, JsonConvert.SerializeObject(data,
                    new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Ignore,
                        ContractResolver = new CamelCasePropertyNamesContractResolver()
                    }).ToString());

                string userResponse = GetUserResponse(response.StatusCode);
                Console.WriteLine($"{gdapRelationshipId}-{userResponse}");

                var accessAssignmentObject = response.IsSuccessStatusCode
                    ? JsonConvert.DeserializeObject<DelegatedAdminAccessAssignment>(response.Content.ReadAsStringAsync().Result)
                    : new DelegatedAdminAccessAssignment() { Status = "Failed", Id = string.Empty };

                logger.LogInformation($"Assignment Response:\n {gdapRelationshipId}-{response.StatusCode} \n {response.Content.ReadAsStringAsync().Result} \n");

                return new DelegatedAdminAccessAssignmentRequest()
                {
                    GdapRelationshipId = gdapRelationshipId,
                    AccessAssignmentId = accessAssignmentObject.Id,
                    Status = accessAssignmentObject.Status
                };
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message);
            }
            return new DelegatedAdminAccessAssignmentRequest();
        }

        private string GetUserResponse(HttpStatusCode statusCode)
        {
            switch (statusCode)
            {
                case HttpStatusCode.OK:
                case HttpStatusCode.Created:
                    return "Created.";
                case HttpStatusCode.Conflict: return "Access assignment already exits.";
                case HttpStatusCode.Forbidden: return "Please check if DAP relationship exists with the Customer.";
                case HttpStatusCode.Unauthorized: return "Unauthorized. Please make sure your Sign-in credentials are correct and MFA enabled.";
                case HttpStatusCode.BadRequest: return "Please check input setup for gdaprelationships and securitygroup configuration.";
                default: return "Failed to create. Please try again.";
            }

        }

        /// <summary>
        /// Gets the Delegated Admin relationship access assignment for a given Delegated Admin relationship ID.
        /// </summary>
        /// <param name="gdapRelationshipId"></param>
        /// <param name="accessAssignmentId"></param>
        /// <returns>DelegatedAdminAccessAssignmentRequest</returns>
        private async Task<DelegatedAdminAccessAssignmentRequest?> GetDelegatedAdminAccessAssignment(string gdapRelationshipId, string accessAssignmentId)
        {
            try
            {
                var url = $"{WebApiUrlAllGdaps}/{gdapRelationshipId}/accessAssignments/{accessAssignmentId}";
                var token = await getToken(Resource.TrafficManager);
                var response = await protectedApiCallHelper.CallWebApiAndProcessResultAsync(url, token);
                if (response != null && response.IsSuccessStatusCode)
                {
                    var accessAssignmentObject = JsonConvert.DeserializeObject<DelegatedAdminAccessAssignment>(response.Content.ReadAsStringAsync().Result);

                    return new DelegatedAdminAccessAssignmentRequest()
                    {
                        GdapRelationshipId = gdapRelationshipId,
                        AccessAssignmentId = accessAssignmentId,
                        Status = accessAssignmentObject.Status
                    };
                }
                else
                {
                    return new DelegatedAdminAccessAssignmentRequest()
                    {
                        GdapRelationshipId = gdapRelationshipId,
                        AccessAssignmentId = accessAssignmentId,
                        Status = "Failed"
                    };
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message);
                Console.WriteLine($"{gdapRelationshipId} - Unexpected error.");
                return new DelegatedAdminAccessAssignmentRequest()
                {
                    GdapRelationshipId = gdapRelationshipId,
                    AccessAssignmentId = accessAssignmentId,
                    Status = "Errored"
                };
            }
        }

        /// <summary>
        /// Create Delegated Access Assignment Object.
        /// </summary>
        /// <param name="SecurityGrpId"></param>
        /// <param name="roleList"></param>
        /// <returns>DelegatedAdminAccessAssignment</returns>
        private DelegatedAdminAccessAssignment GetAdminAccessAssignmentObject(string SecurityGrpId, IEnumerable<UnifiedRole> roleList, List<ADRole> ADRolesList)
        {
            // TODO: validating the role using create of GDAP relation are the same using 
            var validatedRoles = ValidateRole(roleList, ADRolesList);
            return new DelegatedAdminAccessAssignment() { AccessContainer = new DelegatedAdminAccessContainer() { AccessContainerId = SecurityGrpId, AccessContainerType = DelegatedAdminAccessContainerType.SecurityGroup }, AccessDetails = new DelegatedAdminAccessDetails() { UnifiedRoles = validatedRoles } };
        }

        /// <summary>
        /// Validate the roles.
        /// </summary>
        /// <param name="roles"></param>
        /// <param name="ADRoles"></param>
        /// <returns></returns>
        private IEnumerable<UnifiedRole?> ValidateRole(IEnumerable<UnifiedRole> roles, List<ADRole> ADRoles)
        {
            var unifiedRoleList = roles.ToList();
            var validateRoles = new List<UnifiedRole>();
            foreach (var role in unifiedRoleList)
            {
                var adRole = ADRoles.Where(item => item.Id == role.RoleDefinitionId || item.Name.ToLower() == role.RoleDefinitionId.ToLower().Trim()).FirstOrDefault();
                if (adRole != null)
                {
                    validateRoles.Add(new UnifiedRole() { RoleDefinitionId = adRole.Id });
                }
            }

            return validateRoles;
        }
    }
}