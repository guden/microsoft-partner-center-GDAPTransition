using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using GBM.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PartnerLed;
using PartnerLed.Logger;
using PartnerLed.Model;
using PartnerLed.Providers;
using PartnerLed.Utility;




var AppSetting = new AppSetting();

using IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((services) =>
    {
        services.AddSingleton(AppSetting);
        services.AddSingleton<IExportImportProviderFactory, ExportImportProviderFactory>();
        services.AddSingleton<ITokenProvider, TokenProvider>();
        services.AddSingleton<IDapProvider, DapProvider>();
        services.AddSingleton<IAzureRoleProvider, AzureRoleProvider>();
        services.AddSingleton<IGdapProvider, GdapProvider>();
        services.AddSingleton<IAccessAssignmentProvider, AccessAssignmentProvider>();
    }).ConfigureLogging(logging =>
    {
        logging.ClearProviders().AddCustomLogger();
    }).Build();


await RunAsync(host.Services, AppSetting.customProperties.Version);
await host.RunAsync();

static async Task RunAsync(IServiceProvider serviceProvider, string version)
{
    Console.Clear();
    Console.WriteLine("Checking pre-requisites...");

    if (!checkPrerequisites(serviceProvider))
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("Please request your Global Admin to launch the tool and\n" +
            "a. Enable this tool to automatically execute pre-requisite step to grant access to GDAP API.\nb. Provide consent for this tool to access the GDAP API and read your Tenant Security Groups.");
        Console.ResetColor();
        return;
    }

    setupDirectory();
    var type = setupFormat(version);
    SetupFile(type, serviceProvider);
    DisplayOptions();

SelectOption:
    Console.Write('>');
    var option = Console.ReadLine();
    if (!short.TryParse(option, out short input) || !(input >= 1 && input <= 13))
    {
        Console.WriteLine("Invalid input, Please try again.");
        DisplayOptions();

        goto SelectOption;
    }

    Stopwatch stopwatch = Stopwatch.StartNew();

    var result = input switch
    {
        1 => await serviceProvider.GetRequiredService<IDapProvider>().ExportCustomerDetails(type),
        2 => await serviceProvider.GetRequiredService<IDapProvider>().ExportCustomerBulk(),
        3 => await serviceProvider.GetRequiredService<IAzureRoleProvider>().ExportAzureDirectoryRoles(type),
        4 => await serviceProvider.GetRequiredService<IAccessAssignmentProvider>().ExportSecurityGroup(type),
        5 => await serviceProvider.GetRequiredService<IGdapProvider>().GetAllGDAPAsync(type),
        6 => await serviceProvider.GetRequiredService<IDapProvider>().GenerateDAPRelatioshipwithAccessAssignment(type),
        7 => await serviceProvider.GetRequiredService<IGdapProvider>().CreateGDAPRequestAsync(type),
        8 => await serviceProvider.GetRequiredService<IGdapProvider>().RefreshGDAPRequestAsync(type),
        13 => await serviceProvider.GetRequiredService<IGdapProvider>().TerminateGDAPRequestAsync(type),
        9 => await serviceProvider.GetRequiredService<IAccessAssignmentProvider>().CreateAccessAssignmentRequestAsync(type),
        10 => await serviceProvider.GetRequiredService<IAccessAssignmentProvider>().RefreshAccessAssignmentRequest(type),
        11 => await serviceProvider.GetRequiredService<IAccessAssignmentProvider>().UpdateAccessAssignmentRequestAsync(type),
        12 => await serviceProvider.GetRequiredService<IAccessAssignmentProvider>().DeleteAccessAssignmentRequestAsync(type),
        _ => throw new InvalidOperationException("Invalid input")
    };

    stopwatch.Stop();
    Console.WriteLine($"[Completed the operation in {stopwatch.Elapsed}]\n");
    goto SelectOption;
}

static void DisplayOptions()
{
    Console.WriteLine("\nDownload operations: ");
    Console.WriteLine("\t 1. Download eligible customers list");
    Console.WriteLine("\t 2. Download eligible customers for very large list (compressed format)");
    Console.WriteLine("\t 3. Download Example Azure AD Roles");
    Console.WriteLine("\t 4. Download Partner Tenant's Security Group(s)");
    Console.WriteLine("\t 5. Download existing GDAP relationship(s)\n");
    Console.WriteLine("GDAP Relationship operations: ");
    Console.WriteLine("\t 6. One flow generation");
    Console.WriteLine("\t 7. Create GDAP Relationship(s)");
    Console.WriteLine("\t 8. Refresh GDAP Relationship status\n");
    
    Console.WriteLine("Provision Partner Security Group access operations: ");
    Console.WriteLine("\t 9. Create Security Group-Role Assignment(s)");
    Console.WriteLine("\t 10. Refresh Security Group-Role Assignment status\n");

    Console.WriteLine("Update and delete operations: ");
    Console.WriteLine("\t 11. Update Security Group-Role Assignment(s)");
    Console.WriteLine("\t 12. Delete Security Group-Role Assignment(s)");
    Console.WriteLine("\t 13. Terminate GDAP Relationship(s)\n");
}

static async void SetupFile(ExportImport type, IServiceProvider serviceProvider)
{
    await serviceProvider.GetRequiredService<IGdapProvider>().CreateTerminateRelationshipFile(type);
    await serviceProvider.GetRequiredService<IAccessAssignmentProvider>().CreateDeleteAccessAssignmentFile(type);
}

static bool checkPrerequisites(IServiceProvider serviceProvider)
{
    return serviceProvider.GetRequiredService<ITokenProvider>().CheckPrerequisite().Result;
}

static void setupDirectory()
{
    Environment.SetEnvironmentVariable(Constants.BasepathVariable, Directory.GetParent(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location)).Parent.Parent.FullName);
    Directory.CreateDirectory($"{Constants.InputFolderPath}/gdapRelationship");
    Directory.CreateDirectory($"{Constants.InputFolderPath}/accessAssignment");
    Directory.CreateDirectory(Constants.OutputFolderPath);
    Directory.CreateDirectory(Constants.LogFolderPath);
}

static ExportImport setupFormat(string version)
{
    Console.WriteLine("\n\nPlease choose an file type you like to work with for this tool");
    Console.WriteLine("1. CSV");
    Console.WriteLine("2. JSON");
    Console.Write('>');

SelectOption:
    var option = Console.ReadLine();
    if (!short.TryParse(option, out short input) || !(input >= 1 && input <= 2))
    {
        Console.WriteLine("Invalid input, Please try again, possible values are {1, 2}");
        goto SelectOption;
    }
    Console.Clear();
    Console.WriteLine($"GDAP Bulk Migration Tool {version}.");
    
    return (ExportImport)Convert.ToInt32(input);
}
