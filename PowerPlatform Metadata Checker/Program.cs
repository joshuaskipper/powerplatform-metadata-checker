using Microsoft.Identity.Client;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;




// old auth
//init connection
// var connectionString =
//     "AuthType=ClientSecret;" +
//     $"Url={Environment.GetEnvironmentVariable("EnvironmentURL")};" +
//     $"ClientId={Environment.GetEnvironmentVariable("ClientId")};" +
//     $"ClientSecret={Environment.GetEnvironmentVariable("ClientSecret")}";

// 
// using var client = new ServiceClient(connectionString);



string clientId = Environment.GetEnvironmentVariable("PublicClientId");
string environmentUrl = Environment.GetEnvironmentVariable("EnvironmentURL");

var msalApp = PublicClientApplicationBuilder
    .Create(clientId)
    .WithAuthority(
        "https://login.microsoftonline.com/organizations")
    .WithDefaultRedirectUri()
    .Build();
async Task<string> GetAccessTokenAsync(string instanceUri)
{

    var scopes = new[]
    {
        $"{environmentUrl}/user_impersonation"
    };

    var accounts = await msalApp.GetAccountsAsync();
    var account = accounts.FirstOrDefault();
    if (account != null)
    {
        try
        {
            Console.WriteLine("Using existing Microsoft login...");

            var silentResult = await msalApp
                .AcquireTokenSilent(scopes, account)
                .ExecuteAsync();

            Console.WriteLine("Authentication successful!");
            Console.WriteLine($"User: {silentResult.Account?.Username}");
            Console.WriteLine($"Token expires: {silentResult.ExpiresOn.LocalDateTime}");
            Console.WriteLine();

            return silentResult.AccessToken;
        }
        catch (MsalUiRequiredException)
        {

        }
    }


    Console.WriteLine();
    Console.WriteLine("Opening Microsoft login...");
    Console.WriteLine();


    var interactiveResult = await msalApp
        .AcquireTokenInteractive(scopes)
        .WithPrompt(Prompt.SelectAccount)
        .ExecuteAsync();

    Console.WriteLine();
    Console.WriteLine("Authentication successful!");
    Console.WriteLine($"User: {interactiveResult.Account?.Username}");
    Console.WriteLine($"Token expires: {interactiveResult.ExpiresOn.LocalDateTime}");
    Console.WriteLine();

    return interactiveResult.AccessToken;
}

// Create the Dataverse ServiceClient using the interactive token provider.
using var client = new ServiceClient(
    new Uri(environmentUrl),
    GetAccessTokenAsync,
    true);

int totalChecked = 0;
int totalIssues = 0;

//check for connection ref in solution
bool hasConnRef = false;

//check if connection is successful
if (!client.IsReady)
    throw new Exception(client.LastError);

Console.Write("Solution Logical Name: ");

string targetSolutionUniqueName = (Console.ReadLine() ?? string.Empty).Trim().ToLower();

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
}

Console.WriteLine($"Found {results.Entities.Count} objects in solution '{targetSolutionUniqueName}':\n");
Console.WriteLine();

foreach (var record in results.Entities)
{
    var optionSetValue = record.GetAttributeValue<OptionSetValue>("componenttype");
    var displayName = record.GetAttributeValue<string>("displayname");

    if (optionSetValue == null)
        continue;

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
        case 80:
        case 90:
            componentName = componentTypeValue == 80 ? "Model-Driven App" : "Plugin Type";
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

    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.WriteLine($"ComponentValue: {componentName} | Object ID: {objectId}");
    Console.ResetColor();

    // web resources
    if (componentTypeValue == 61)
    {
        totalChecked++;
        Entity webResources = client.Retrieve("webresource", objectId, new ColumnSet(true));

        string webRecName = webResources.GetAttributeValue<string>("name") ?? "None";
        string webRecDisplayName = webResources.GetAttributeValue<string>("displayname") ?? "None";
        string? rawWebRecDesc = webResources.GetAttributeValue<string>("description");

        if (string.IsNullOrWhiteSpace(rawWebRecDesc))
        {
            Reports.Add(new Report
            {
                Fix = "Add Web Resource Description",
                LogicalName = webRecName,
                Name = webRecDisplayName,
                ComponentValue = "Web Resource",
                Severity = Severity.Warning
            });

            totalIssues++;
        }
    }

    //canvas apps
    if (componentTypeValue == 300)
    {
        try
        {
            totalChecked++;
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
                    Fix = "Canvas App needs a description.",
                    Severity = Severity.Warning
                });

                totalIssues++;
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
    if (componentTypeValue == 80 || componentTypeValue == 200)
    {
        try
        {
            totalChecked++;
            Entity modelDrivenApp = client.Retrieve("appmodule", objectId, new ColumnSet(true));

            string? rawMdaDescription = modelDrivenApp.GetAttributeValue<string>("description");
            string mdaDescription = string.IsNullOrWhiteSpace(rawMdaDescription) ? "None" : rawMdaDescription;
            string mdaName = modelDrivenApp.GetAttributeValue<string>("name") ?? "None";
            string mdaLogicalName = modelDrivenApp.GetAttributeValue<string>("uniquename") ?? "None";

            Console.WriteLine($"MDA Logical Name: {mdaLogicalName}");
            Console.WriteLine($"Name:             {mdaName}");
            Console.WriteLine($"Description:      {mdaDescription}");

            if (string.IsNullOrWhiteSpace(rawMdaDescription))
            {
                Reports.Add(new Report
                {
                    Name = mdaName,
                    LogicalName = mdaLogicalName,
                    ComponentValue = componentName,
                    Fix = "Model-Driven App needs a description.",
                    Severity = Severity.Warning
                });

                totalIssues++;
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
            totalChecked++;
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

                totalIssues++;
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
                        totalChecked++;
                        string attrLogicalName = attr.LogicalName;
                        string attrDescription = attr.Description?.UserLocalizedLabel?.Label ?? string.Empty;

                        if (string.IsNullOrWhiteSpace(attrDescription))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            var columnReport = new Report { ComponentValue = "Column", Name = attrLogicalName, Fix = "Column needs description." };
                            entityReport.ColumnReport.Add(columnReport);
                            needsReporting = true;
                            totalIssues++;
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


double gradePercent;
if (totalChecked > 0)
{
    gradePercent = ((double)(totalChecked - totalIssues) / totalChecked) * 100.0;
}
else 
{
    gradePercent = 100.0;
}


Console.WriteLine();
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.DarkCyan;
Console.WriteLine("************************************************************");
Console.WriteLine($"Final Report: {totalIssues} out of {totalChecked} objects need corrections");
Console.WriteLine($"Grade: {gradePercent:F1}%");

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

try
{
    Console.WriteLine("Generating PDF...");

    QuestPDF.Fluent.Document.Create(container =>
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);

            page.Margin(0.75f, Unit.Centimetre);
            page.PageColor(Colors.White);
            page.DefaultTextStyle(x => x.FontSize(12));

            page.Content()
            .PaddingTop(0)
                .Column(x =>
                {
                    x.Item().LineHorizontal(3f).LineColor(Colors.BlueGrey.Darken4);
                    x.Item().Text("Engineering Review").FontSize(18).Bold().FontColor(Colors.BlueGrey.Darken4);

                    x.Item().Table(meta =>
                    {
                        meta.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(90);
                            c.RelativeColumn();
                        });

                        meta.Cell().Text("Name:").FontColor(Colors.BlueGrey.Darken4);
                        meta.Cell().Text(friendlyName).FontColor(Colors.BlueGrey.Darken4);

                        meta.Cell().Text("LogicalName:").FontColor(Colors.BlueGrey.Darken4);
                        meta.Cell().Text(targetSolutionUniqueName).FontColor(Colors.BlueGrey.Darken4);

                        meta.Cell().Text("Version:").FontColor(Colors.BlueGrey.Darken4);
                        meta.Cell().Text(version).FontColor(Colors.BlueGrey.Darken4);

                        meta.Cell().Text("Description:").FontColor(Colors.BlueGrey.Darken4);
                        meta.Cell().Text(description).FontColor(Colors.BlueGrey.Darken4);

                        if (hasConnRef)
                        {
                            meta.Cell().Text("Connections:").FontColor(Colors.BlueGrey.Darken4);
                            meta.Cell().Text("Solution Contains Connection references").FontColor(Colors.BlueGrey.Darken4);
                        }
                    });

                    x.Item().LineHorizontal(3f).LineColor(Colors.BlueGrey.Darken4);

                    foreach (var item in Reports)
                    {
                        x.Item().PaddingTop(6).Border(0.55f).BorderColor(Colors.BlueGrey.Lighten2).Padding(8).Column(card =>
                        {
                            card.Item().Row(row =>
                            {
                                row.RelativeItem().Text(t =>
                                {
                                    t.Span(item.Name).Bold().FontSize(11);
                                    t.Span($"  ({item.LogicalName})").FontSize(9).FontColor(Colors.BlueGrey.Darken4);
                                });

                                row.AutoItem().Background(Colors.BlueGrey.Lighten5).PaddingHorizontal(5).PaddingVertical(2)
                                    .Text(item.ComponentValue.ToUpper()).FontSize(8).SemiBold();
                            });

                            card.Item().Text($"Table status: {item.Fix}").FontSize(9).FontColor(Colors.Red.Darken4).SemiBold();
                            card.Item().Text($"Severity: {item.Severity}").FontSize(9).FontColor(Colors.Red.Darken4).SemiBold();

                            if (item.ColumnReport.Count > 0)
                            {
                                card.Item().PaddingTop(2).Table(table =>
                                {
                                    table.ColumnsDefinition(c =>
                                    {
                                        c.RelativeColumn(2);
                                        c.RelativeColumn(3);
                                    });

                                    table.Header(h =>
                                    {
                                        h.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Column").SemiBold().FontSize(8);
                                        h.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Action Required").SemiBold().FontSize(8);
                                    });

                                    foreach (var att in item.ColumnReport)
                                    {
                                        table.Cell().BorderBottom(1).BorderColor(Colors.Black).Padding(4)
                                            .Text(att.Name).FontSize(8.5f);

                                        table.Cell().BorderBottom(1).BorderColor(Colors.Black).Padding(4)
                                            .Text(att.Fix).FontSize(8.5f).FontColor(Colors.Red.Darken4);
                                    }
                                });
                            }
                        });
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

    Console.WriteLine($"PDF saved to: {filepath}");
}
catch (IOException ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"Could not write PDF to '{filepath}' (file may be open or locked): {ex.Message}");
    Console.ResetColor();
}

public class Report
{
    public string ComponentValue { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string LogicalName { get; set; } = string.Empty;
    public string Fix { get; set; } = string.Empty;
    public Severity Severity { get; set; }
    public List<Report> ColumnReport { get; set; } = new List<Report>();
}


public enum Severity
{
    Error,
    Warning,
    Info

}
