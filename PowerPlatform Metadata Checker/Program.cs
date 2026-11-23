using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Reflection.Metadata;

//init connection
var connectionString =
    "AuthType=ClientSecret;" +
    $"Url={Environment.GetEnvironmentVariable("EnvironmentURL")};" +
    $"ClientId={Environment.GetEnvironmentVariable("ClientId")};" +
    $"ClientSecret={Environment.GetEnvironmentVariable("ClientSecret")}";

using var client = new ServiceClient(connectionString);

int gradeCounter = 0;

//check for connection ref in solution
bool hasConnRef = false;

//check if connection is successful
if (!client.IsReady)
    throw new Exception(client.LastError);

Console.Write("Solution Logical Name: ");

string targetSolutionUniqueName = Console.ReadLine().ToLower();

var query = new QueryExpression("solutioncomponent")
{
    ColumnSet = new ColumnSet("componenttype", "objectid", "solutioncomponentid")
};

// associate the solution table and metadata 
var solutionLink = query.AddLink("solution", "solutionid", "solutionid", JoinOperator.Inner);
solutionLink.EntityAlias = "sol"; // Gives us an alias to read the fields later
solutionLink.Columns = new ColumnSet("friendlyname", "version", "description", "ismanaged");
solutionLink.LinkCriteria.AddCondition("uniquename", ConditionOperator.Equal, targetSolutionUniqueName);

var results = client.RetrieveMultiple(query);

List<Report> Reports = new List<Report>();

string? friendlyName = "";
string? version = "";
string? description = "";
bool? isManaged;

//Extract solution metadata 
if (results.Entities.Count > 0)
{
    var firstRecord = results.Entities[0];

    // Aliased values from a LinkEntity
    friendlyName = firstRecord.GetAttributeValue<AliasedValue>("sol.friendlyname")?.Value?.ToString() ?? "Unknown";
    version = firstRecord.GetAttributeValue<AliasedValue>("sol.version")?.Value?.ToString() ?? "Unknown";
    description = firstRecord.GetAttributeValue<AliasedValue>("sol.description")?.Value?.ToString() ?? "None";
    isManaged = (bool)(firstRecord.GetAttributeValue<AliasedValue>("sol.ismanaged")?.Value ?? false);

    Console.WriteLine("---------------------------------------------------");
    Console.WriteLine($" Engineering Review for: {targetSolutionUniqueName}");
    Console.WriteLine("---------------------------------------------------");
    Console.WriteLine($" Friendly Name : {friendlyName}");
    Console.WriteLine($" Version       : {version}");
    Console.WriteLine($" Is Managed    : {isManaged}");
    Console.WriteLine($" Description   : {description}");
    Console.WriteLine("---------------------------------------------------");
    // count solution descrip
    gradeCounter++;
}

Console.WriteLine($"Found {results.Entities.Count} objects in solution '{targetSolutionUniqueName}':\n");

gradeCounter += results.Entities.Count;

Console.WriteLine();

foreach (var record in results.Entities)
{
    var optionSetValue = record.GetAttributeValue<OptionSetValue>("componenttype");
    var displayName = record.GetAttributeValue<string>("displayname");

    var componentTypeValue = optionSetValue.Value;

    string componentName = "Unknown";

    switch (componentTypeValue)
    {
        case 1:
            componentName = "Entity (Table)";
            break;
        case 2:
            componentName = "Attribute (Column)";
            break;
        case 3:
            componentName = "Relationship";
            break;
        case 10:
            componentName = "Attribute Relationship";
            break;
        case 24:
            componentName = "Workflow / Flow";
            break;
        case 29:
            componentName = "Plugin Assembly";
            break;
        case 31:
            componentName = "SDK Message Processing Step";
            break;
        case 61:
            componentName = "Web Resource";
            break;
        case 62:
            componentName = "Site Map";
            break;
        case 90:
            componentName = "Plugin Type";
            break;
        case 200:
            componentName = "Model-Driven App";
            break;
        case 300:
            componentName = "Canvas App";
            break;
        case 10088:
            componentName = "Connection Reference";
            break;
        default:
            componentName = $"Other / Custom Type ({componentTypeValue})";
            break;
    }

    Guid objectId = record.GetAttributeValue<Guid>("objectid");
    Console.WriteLine("-----------------------------------------");

    //ComponentTypeID: { componentTypeValue} |
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.WriteLine($"ComponentValue: {componentName} | Object ID: {objectId}");
    Console.ResetColor();

    //canvas apps
    if (componentTypeValue == 300)
    {
        try
        {
            Entity canvasAppMeta = client.Retrieve("canvasapp", objectId, new ColumnSet("name", "displayname", "description", "appversion"));

            string appName = canvasAppMeta.GetAttributeValue<string>("name") ?? "None";
            string appDisplayName = canvasAppMeta.GetAttributeValue<string>("displayname") ?? "None";

            string? rawAppDescription = canvasAppMeta.GetAttributeValue<string>("description");

            string appDescription = string.IsNullOrWhiteSpace(rawAppDescription) ? "None" : rawAppDescription;
            string appVersion = canvasAppMeta.GetAttributeValue<string>("appversion") ?? "None";


            if (string.IsNullOrWhiteSpace(rawAppDescription))
            {
                Reports.Add(new Report
                {
                    Name = appDisplayName,
                    LogicalName = appName,
                    ComponentValue = componentName,
                    Fix = "Canvas App needs a description."
                });
            }

            Console.WriteLine($"App Internal Name : {appName}");
            Console.WriteLine($"Display Name      : {appDisplayName}");
            Console.WriteLine($"Version           : {appVersion}");
            Console.WriteLine($"Description       : {appDescription}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving Canvas App record: {ex.Message}");
        }
    }
    //Model Driven App
    if (componentTypeValue == 80)
    {
        try
        {
            Entity modelDrivenApp = client.Retrieve("appmodule", objectId, new ColumnSet(true));


            string mdaDescription = modelDrivenApp.GetAttributeValue<string>("description") ?? "None";
            string mdaName = modelDrivenApp.GetAttributeValue<string>("name") ?? "None";
            string mdaLogicalName = modelDrivenApp.GetAttributeValue<string>("uniquename") ?? "None";

            Console.WriteLine($"MDA Logical Name: {mdaLogicalName}");
            Console.WriteLine($"Name:             {mdaName}");
            Console.WriteLine($"Description:      {mdaDescription}");

            if (string.IsNullOrWhiteSpace(mdaDescription) || mdaDescription == "None")
            {
                Reports.Add(new Report { Name = mdaName, LogicalName = mdaLogicalName, ComponentValue = "Model-Driven App", Fix = "Model-Driven App needs a description." });
            }

        }
        catch (Exception ex)
        {

            Console.WriteLine($"Error: {ex.Message}");
        }
    }
    //Tables/entitiy
    if (componentTypeValue == 1)
    {
        bool needsReporting = false;

        try
        {
            var entityRequest = new RetrieveEntityRequest
            {
                MetadataId = objectId,
                EntityFilters = EntityFilters.Entity | EntityFilters.Attributes
            };

            var entityResponse = (RetrieveEntityResponse)client.Execute(entityRequest);

            var entityMeta = entityResponse.EntityMetadata;

            string entityLogicalName = entityMeta.LogicalName;
            string entityDisplayName = entityMeta.DisplayName?.UserLocalizedLabel?.Label ?? "None";
            string? rawEntityDescription = entityMeta.Description?.UserLocalizedLabel?.Label;
            string entityDescription = string.IsNullOrWhiteSpace(rawEntityDescription) ? "None" : rawEntityDescription;

            var entityReport = new Report { ComponentValue = componentName, Name = entityDisplayName, LogicalName = entityLogicalName, Fix = "OK" };

            if (string.IsNullOrWhiteSpace(rawEntityDescription))
            {
                entityReport.Fix = "Table needs description.";
                needsReporting = true;
            }

            Console.WriteLine($"Entity Logical Name : {entityLogicalName}");
            Console.WriteLine($"Display Name        : {entityDisplayName}");
            Console.WriteLine($"Description         : {entityDescription}");
            Console.WriteLine();
            Console.WriteLine("Custom Columns...");
            if (entityMeta.Attributes != null)
            {
                foreach (var attr in entityMeta.Attributes)
                {
                    if (attr.IsCustomAttribute.GetValueOrDefault(false))
                    {
                        string attrLogicalName = attr.LogicalName;
                        string attrDescription = attr.Description?.UserLocalizedLabel?.Label ?? string.Empty;

                        if (string.IsNullOrEmpty(attrDescription))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            var columnReport = new Report { ComponentValue = "Column", Name = attrLogicalName, Fix = "Column needs description." };
                            entityReport.ColumnReport.Add(columnReport);
                            needsReporting = true;
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                        }

                        Console.WriteLine($"{attrLogicalName} - {attrDescription}");
                        Console.ResetColor();
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(rawEntityDescription) && entityReport.ColumnReport.Count > 0)
            {
                entityReport.Fix = "Table has missing column descriptions.";
            }

            // Add to main reports list only once if anything failed
            if (needsReporting)
            {
                Reports.Add(entityReport);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving entity metadata: {ex.Message}");
        }
    }
    //connection references
    if (componentTypeValue == 10088)
    {
        hasConnRef = true;
        try
        {
            var connQuery = new ColumnSet(
                "connectionreferencedisplayname",
                "connectionid",
                "connectionreferenceid",
                "connectionreferencelogicalname");

            Entity connRefMeta = client.Retrieve("connectionreference", objectId, connQuery);


            string connDisplayName = connRefMeta.GetAttributeValue<string>("connectionreferencedisplayname") ?? "None";
            string connectionid = connRefMeta.GetAttributeValue<string>("connectionid") ?? "None";
            Guid connectionreferenceid = connRefMeta.GetAttributeValue<Guid>("connectionreferenceid");
            string connectionreferencelogicalname = connRefMeta.GetAttributeValue<string>("connectionreferencelogicalname") ?? "None";

            Console.WriteLine($"Display Name                         : {connDisplayName}");
            Console.WriteLine($"Connection ID                        : {connectionid}");
            Console.WriteLine($"connectionreferenceid                : {connectionreferenceid}");
            Console.WriteLine($"connectionreferencelogicalname       : {connectionreferencelogicalname}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
        }
    }
}

Console.WriteLine();
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.DarkCyan;
Console.WriteLine("************************************************************");
Console.WriteLine($"Final Report: {Reports.Count} out of {gradeCounter} objects need corrections");
if (hasConnRef)
{
    Console.WriteLine("This solution contains connection references.. ");
}

Console.WriteLine("************************************************************");
Console.ResetColor();

Console.WriteLine("------------------------------------");

string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
string filepath = Path.Combine(desktop, "PDFTest.pdf");

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

QuestPDF.Fluent.Document.Create(container =>
{
    container.Page(page =>
    {
        page.Size(PageSizes.A4);

        page.Margin(0.75f, Unit.Centimetre);
        page.PageColor(Colors.White);
        page.DefaultTextStyle(x => x.FontSize(12));

        //page.Header()
        //    .Text("Engineering Review")
        //    .SemiBold().FontSize(18).FontColor(Colors.BlueGrey.Medium);

        page.Content()
        .PaddingTop(0)
            //.PaddingVertical(1, Unit.Centimetre)
            .Column(x =>
            {
                x.Spacing(3);
                x.Item().Text("------------------------------------------------------------------------------------------------------").FontColor(Colors.BlueGrey.Medium);
                x.Item().Text($" Engineering Review for: {targetSolutionUniqueName}").FontColor(Colors.BlueGrey.Medium); ;
                x.Item().Text("------------------------------------------------------------------------------------------------------").FontColor(Colors.BlueGrey.Medium);
                x.Item().Text($"Name:         {friendlyName}").FontColor(Colors.BlueGrey.Medium);
                x.Item().Text($"Version:      {version}").FontColor(Colors.BlueGrey.Medium);
                // x.Item().Text($" Is Managed    : {isManaged}");
                x.Item().Text($"Description:  {description}").FontColor(Colors.BlueGrey.Medium);
                if (hasConnRef)
                {
                    x.Item().Text($"Solution Contains Connection references").FontColor(Colors.BlueGrey.Medium);
                }


                x.Item().Text("------------------------------------------------------------------------------------------------------").FontColor(Colors.BlueGrey.Medium);
                foreach (var item in Reports)
                {
                    x.Item().Text($"[{item.ComponentValue.ToUpper()}]").SemiBold().FontSize(9);
                    x.Item().Text($"Logical Name: {item.LogicalName}").FontSize(9);
                    x.Item().Text($"Display Name: {item.Name}").FontSize(9);
                    x.Item().Text($"Fix: {item.Fix}").FontSize(9).FontColor(Colors.Red.Medium);
                    //   x.Item().Text($"[{item.ComponentValue.ToUpper()}] | [LOGICAL]({item.LogicalName}) - [DISPLAY]({item.Name}) — {item.Fix}").SemiBold().FontSize(9);
                    foreach (var att in item.ColumnReport)
                    {
                        x.Item().PaddingLeft(15).Text($"[{att.ComponentValue.ToUpper()}] | {att.Name} — {att.Fix}").FontSize(9).FontColor(Colors.Red.Medium);
                    }
                    x.Item().Text("------------------------------------------------------------------------------------------------------").FontColor(Colors.BlueGrey.Medium);

                }
                if (Reports.Count == 0)
                {
                    x.Item().Text("No issues found.");
                }
            });

        page.Footer()
            .AlignCenter()
            .Text(x =>
            {
                x.Span("Page ").FontSize(8);
                x.CurrentPageNumber().FontSize(8);
            });
    });
})
.GeneratePdf(filepath);

public class Report
{
    public string ComponentValue { get; set; }
    public string Name { get; set; }
    public string LogicalName { get; set; }
    public string Fix { get; set; }
    public List<Report> ColumnReport { get; set; } = new List<Report>();
}