using PartnerLed.Model;

namespace PartnerLed.Providers
{
    public interface IGdapProvider
    {
        Task<bool> GetAllGDAPAsync(ExportImport type);

        Task<bool> CreateGDAPRequestAsync(ExportImport type);

        Task<bool> RefreshGDAPRequestAsync(ExportImport type);

        Task<bool> TerminateGDAPRequestAsync(ExportImport type);

        Task<bool> CreateTerminateRelationshipFile(ExportImport type);
    }
}
