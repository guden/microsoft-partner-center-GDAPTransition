using PartnerLed.Model;

namespace PartnerLed.Providers
{
    public interface IAccessAssignmentProvider
    {
        Task<bool> ExportSecurityGroup(ExportImport type);

        Task<bool> CreateAccessAssignmentRequestAsync(ExportImport type);

        Task<bool> RefreshAccessAssignmentRequest(ExportImport type);

        Task<bool> UpdateAccessAssignmentRequestAsync(ExportImport type);

        Task<bool> DeleteAccessAssignmentRequestAsync(ExportImport type);

        Task<bool> CreateDeleteAccessAssignmentFile(ExportImport type);
    }
}