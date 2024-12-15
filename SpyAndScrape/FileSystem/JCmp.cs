using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SpyAndScrape.FileSystem;

public class JCmp
{
    private readonly string _baseDir;

    public JCmp()
    {
        _baseDir = AppDomain.CurrentDomain.BaseDirectory;
    }

    public (bool, JObject) CompareJson(string fName, string newJContent)
    {
        string fPth = GetFilePath(fName);

        if (!File.Exists(fPth))
        {
            JObject newResult = JObject.Parse(newJContent);
            return (true, new JObject
            {
                ["action"] = "new",
                ["content"] = newResult
            });
        }

        string existingJContent = File.ReadAllText(fPth);

        if (string.IsNullOrWhiteSpace(existingJContent))
        {
            JObject newRes = JObject.Parse(newJContent);
            return (true, new JObject
            {
                ["action"] = "new",
                ["content"] = newRes
            });
        }

        JObject existingJ = JObject.Parse(existingJContent);
        JObject newJ = JObject.Parse(newJContent);

        if (JToken.DeepEquals(existingJ, newJ))
        {
            return (false, new JObject());
        }

        JObject changes = GetChanges(existingJ, newJ);

        return (true, changes);
    }
    
    // oh god noones want to know how much I raged on this
    // this very calming to watch lol, https://youtu.be/NCdDwa3VH-Y?si=XJEhj7hzOg6e_IhX
    private JObject GetChanges(JToken oldJ, JToken newJ)
    {
        JObject res = new JObject();

        if (oldJ.Type == JTokenType.Object && newJ.Type == JTokenType.Object)
        {
            var oldObj = (JObject)oldJ;
            var newObj = (JObject)newJ;

            foreach (var property in oldObj.Properties())
            {
                if (!newObj.ContainsKey(property.Name))
                {
                    res[property.Name] = new JObject
                    {
                        ["action"] = "deleted",
                        ["value"] = property.Value
                    };
                }
                else if (!JToken.DeepEquals(property.Value, newObj[property.Name]))
                {
                    res[property.Name] = GetChanges(property.Value, newObj[property.Name]);
                }
            }

            foreach (var property in newObj.Properties())
            {
                if (!oldObj.ContainsKey(property.Name))
                {
                    res[property.Name] = new JObject
                    {
                        ["action"] = "added",
                        ["value"] = property.Value
                    };
                }
            }
        }
        else if (oldJ.Type == JTokenType.Array && newJ.Type == JTokenType.Array)
        {
            res = CompareArrays((JArray)oldJ, (JArray)newJ);
        }
        else if (!JToken.DeepEquals(oldJ, newJ))
        {
            res["action"] = "edited";
            res["oldValue"] = oldJ;
            res["newValue"] = newJ;
        }

        return res;
    }

    //GAMBLING!
    private JObject CompareArrays(JArray oldArray, JArray newArray)
    {
        JObject result = new JObject();
        JArray addedItems = new JArray();
        JArray deletedItems = new JArray();
        JArray editedItems = new JArray();
        var oldItemsDict = new Dictionary<string, JObject>();
        var newItemsDict = new Dictionary<string, JObject>();

        foreach (var item in oldArray.Cast<JObject>())
        {
            string key = item["id"]?.ToString() ?? Guid.NewGuid().ToString();
            oldItemsDict[key] = item;
        }

        foreach (var item in newArray.Cast<JObject>())
        {
            string key = item["id"]?.ToString() ?? Guid.NewGuid().ToString();
            newItemsDict[key] = item;
        }

        foreach (var oldItem in oldItemsDict)
        {
            if (!newItemsDict.ContainsKey(oldItem.Key))
            {
                deletedItems.Add(oldItem.Value);
            }
            else if (!JToken.DeepEquals(oldItem.Value, newItemsDict[oldItem.Key]))
            {
                editedItems.Add(new JObject
                {
                    ["id"] = oldItem.Key,
                    ["oldValue"] = oldItem.Value,
                    ["newValue"] = newItemsDict[oldItem.Key]
                    
                    //
                    
                });
            }
        }

        foreach (var newItem in newItemsDict)
        {
            if (!oldItemsDict.ContainsKey(newItem.Key))
            {
                addedItems.Add(newItem.Value);
            }
        }

        if (addedItems.Count > 0)
        {
            result["addedItems"] = addedItems;
        }
        if (deletedItems.Count > 0)
        {
            result["deletedItems"] = deletedItems;
        }
        if (editedItems.Count > 0)
        {
            result["editedItems"] = editedItems;
        }

        
        
        if (addedItems.Count > 0 && deletedItems.Count == 0 && editedItems.Count == 0)
        {
            result["action"] = "added";
        }
        else if (deletedItems.Count > 0 && addedItems.Count == 0 && editedItems.Count == 0)
        {
            result["action"] = "deleted";
        }
        else if (editedItems.Count > 0 || addedItems.Count > 0 || deletedItems.Count > 0)
        {
            result["action"] = "edited";
        }
        

        return result;
    }



    private string GetFilePath(string fileName)
    {
        if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            fileName += ".json";
        }
        return Path.Combine(_baseDir, fileName);
    }
}