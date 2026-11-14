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

List<Report> reports = new List<Report>();

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

    if (componentTypeValue == 1)
    {
        
        try
        {
            var entityRequest = new RetrieveEntityRequest
            {
                MetadataId = objectId,
                EntityFilters = EntityFilters.Entity
            };

            var entityResponse = (RetrieveEntityResponse)client.Execute(entityRequest);

            var entityMeta = entityResponse.EntityMetadata;


            string entityLogicalName = entityMeta.LogicalName;
            string entityDisplayName = entityMeta.DisplayName?.UserLocalizedLabel?.Label ?? "None";
            string entityDescription = entityMeta.Description?.UserLocalizedLabel?.Label ?? "None";

            if (string.IsNullOrWhiteSpace(entityDescription) || string.IsNullOrEmpty(entityDescription))
            {
                report.Counter ++;
            }

            Console.WriteLine($"Entity Logical Name : {entityLogicalName}");
            Console.WriteLine($"Display Name        : {entityDisplayName}");
            Console.WriteLine($"Description         : {entityDescription}");
        }
        catch (Exception ex)
        {

            Console.WriteLine($"Error retrieving entity metadata: {ex.Message}");
        }
        
    }
    
}

Console.WriteLine($"Counter of null.. {report.Counter}");

public class Report
{
    public int Id { get; set; }
    public string ComponentValue { get; set; }
    public string Name { get; set; }
    public int Counter { get; set; } = 0;

};

