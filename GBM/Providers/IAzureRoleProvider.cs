using GBM.Model;
using PartnerLed.Model;

namespace PartnerLed.Providers
{
    internal interface IAzureRoleProvider
    {
        Task<bool> ExportAzureDirectoryRoles(ExportImport type);
    }
}
