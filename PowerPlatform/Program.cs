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
}

Console.WriteLine($"Found {results.Entities.Count} objects in solution '{targetSolutionUniqueName}':\n");

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
        default:
            componentName = $"Other / Custom Type ({componentTypeValue})";
            break;
    }

    Guid objectId = record.GetAttributeValue<Guid>("objectid");
    Console.WriteLine("-----------------------------------------");
    Console.WriteLine($"ComponentTypeID: {componentTypeValue} | ComponentValue: {componentName} | Object ID: {objectId}");

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

            if (string.IsNullOrWhiteSpace(entityDescription) || string.IsNullOrEmpty(entityDescription))
            {
                var report = new Report { Name = entityLogicalName, ComponentValue = componentName, Fix = "Needs description." };
                Reports.Add(report);
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
                            Console.ForegroundColor = ConsoleColor.DarkRed;
                            Console.WriteLine($"{attrLogicalName} - {attrDescription}");
                            Console.ResetColor();
                        
                        }

                    }
                  
                    
                }
            }
        }
        catch (Exception ex)
        {

            Console.WriteLine($"Error retrieving entity metadata: {ex.Message}");
        }
        
    }
    
}


Console.WriteLine();
Console.WriteLine();
Console.WriteLine("************************************************************");
Console.WriteLine("Final Report");
Console.WriteLine("************************************************************");

foreach (var item in Reports)
{
    Console.WriteLine("------------------------------------");
    Console.WriteLine($"{item.ComponentValue} | {item.Name}");
    Console.WriteLine($"TODO: {item.Fix}");
    Console.WriteLine();
    
}
Console.WriteLine("------------------------------------");
Console.WriteLine($"Count of null descriptions: {Reports.Count()}");

public class Report
{
    public string ComponentValue { get; set; }
    public string Name { get; set; }
    public string Fix { get; set; }
};

