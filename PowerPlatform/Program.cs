using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;




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

//Extract solution metadata 
if (results.Entities.Count > 0)
{
    var firstRecord = results.Entities[0];

    // Aliased values from a LinkEntity
    string friendlyName = firstRecord.GetAttributeValue<AliasedValue>("sol.friendlyname")?.Value?.ToString() ?? "Unknown";
    string version = firstRecord.GetAttributeValue<AliasedValue>("sol.version")?.Value?.ToString() ?? "Unknown";
    string description = firstRecord.GetAttributeValue<AliasedValue>("sol.description")?.Value?.ToString() ?? "None";
    bool isManaged = (bool)(firstRecord.GetAttributeValue<AliasedValue>("sol.ismanaged")?.Value ?? false);

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

    var entityQuery = new QueryExpression("entity")
    {

        ColumnSet = new ColumnSet(true)
    };

    if (componentTypeValue == 300)
    {
        try
        {
            Entity canvasAppMeta = client.Retrieve("canvasapp", objectId, new ColumnSet("name", "displayname", "description", "appversion"));

            string appName = canvasAppMeta.GetAttributeValue<string>("name") ?? "None";
            string appDisplayName = canvasAppMeta.GetAttributeValue<string>("displayname") ?? "None";
            string appDescription = canvasAppMeta.GetAttributeValue<string>("description") ?? "None";
            string appVersion = canvasAppMeta.GetAttributeValue<string>("appversion") ?? "None";

            if (string.IsNullOrWhiteSpace(appDescription))
            {
                Reports.Add(new Report
                {
                    Name = appName,
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
            string entityDescription = entityMeta.Description?.UserLocalizedLabel?.Label ?? "None";

            var entityReport = new Report { ComponentValue = componentName, Name = entityDisplayName, Fix = "OK" };
            if (string.IsNullOrWhiteSpace(entityDescription) || string.IsNullOrEmpty(entityDescription))
            {
                entityReport.Fix = "Table needs description.";
                //Reports.Add(report1);
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
            // If the table description was fine, but it has missing child columns, update the fix message
            if (string.IsNullOrWhiteSpace(entityReport.Fix) && entityReport.ColumnReport.Count > 0)
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

            string connDisplayName = connRefMeta.GetAttributeValue<string>("connectionreferencelogicalname") ?? "None";
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

            Console.WriteLine($"Error: {ex}"); ;
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

//foreach (var item in Reports)
//{
//    Console.WriteLine("------------------------------------");
//    Console.WriteLine($"[PARENT] {item.ComponentValue} | {item.Name}");
//    Console.WriteLine($"TODO: {item.Fix}");
//    Console.WriteLine();

//    foreach (var att in item.ColumnReport)
//    {
//        Console.WriteLine("------------------------------------");
//        Console.WriteLine($"    -> [COLUMN] {att.ComponentValue} | {att.Name}");
//        Console.WriteLine($"    -> TODO: {att.Fix}");
//        Console.WriteLine();
//    }
//}
Console.WriteLine("------------------------------------");


public class Report
{
    public string ComponentValue { get; set; }
    public string Name { get; set; }
    public string Fix { get; set; }
    public List<Report> ColumnReport { get; set; } = new List<Report>();
};

