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

        JObject newJ = JObject.Parse(newJContent);

        // if true = new file
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
            // treat as if old content was an empty obj
            JObject changes = GetChanges(new JObject(), newJ);
            return (changes.Count > 0, changes);
        }

        JObject existingJ = JObject.Parse(existingJContent);

        if (JToken.DeepEquals(existingJ, newJ))
        {
            return (false, new JObject());
        }

        JObject changesRes = GetChanges(existingJ, newJ);

        return (changesRes.Count > 0, changesRes);
    }
    
    // oh god noones want to know how much I raged on this
    // this very calming to watch lol, https://youtu.be/NCdDwa3VH-Y?si=XJEhj7hzOg6e_IhX
    private JObject GetChanges(JToken oldJ, JToken newJ)
    {
        JObject res = new JObject();

        // If tokens are same, no changes at this lvl nor deeper
        if (JToken.DeepEquals(oldJ, newJ))
        {
            return res;
        }

        if (oldJ.Type == JTokenType.Object && newJ.Type == JTokenType.Object)
        {
            var oldObj = (JObject)oldJ;
            var newObj = (JObject)newJ;

            foreach (var property in oldObj.Properties())
            {
                JToken? newPropertyValue = newObj[property.Name];

                if (newPropertyValue == null) // = deleted
                {
                    res[property.Name] = new JObject
                    {
                        ["action"] = "deleted",
                        ["oldValue"] = property.Value
                    };
                }
                else // exists in both
                {
                    JObject subChanges = GetChanges(property.Value, newPropertyValue);
                    if (subChanges.Count > 0)
                    {
                        res[property.Name] = subChanges;
                    }
                }
            }

            foreach (var property in newObj.Properties())
            {
                if (oldObj[property.Name] == null) // = added
                {
                    res[property.Name] = new JObject
                    {
                        ["action"] = "added",
                        ["newValue"] = property.Value
                    };
                }
            }
        }
        else if (oldJ.Type == JTokenType.Array && newJ.Type == JTokenType.Array)
        {
            // should return an empty JObj if no itemlevel changes, calling context will after then decide, whether to include this arr field based on its emptiness
            return CompareArrays((JArray)oldJ, (JArray)newJ);
        }
        else
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

        foreach (var itemToken in oldArray)
        {
            if (itemToken is JObject item)
            {
                string key = item["id"]?.ToString() ?? Guid.NewGuid().ToString();
                oldItemsDict[key] = item;
            }
        }
        

        var newItemsDict = new Dictionary<string, JObject>();

        foreach (var itemToken in newArray)
        {
            if (itemToken is JObject item)
            {
                string key = item["id"]?.ToString() ?? Guid.NewGuid().ToString();
                newItemsDict[key] = item;
            }
        }
        

        foreach (var oldEntry in oldItemsDict)
        {
            if (!newItemsDict.ContainsKey(oldEntry.Key))
            {
                deletedItems.Add(oldEntry.Value);
            }
            else if (!JToken.DeepEquals(oldEntry.Value, newItemsDict[oldEntry.Key]))
            {
                editedItems.Add(new JObject
                {
                    ["id"] = oldEntry.Key,
                    ["oldValue"] = oldEntry.Value,
                    ["newValue"] = newItemsDict[oldEntry.Key]
                });
            }
        }

        foreach (var newEntry in newItemsDict)
        {
            if (!oldItemsDict.ContainsKey(newEntry.Key))
            {
                addedItems.Add(newEntry.Value);
            }
        }

        bool hasChanges = false;
        if (addedItems.Count > 0)
        {
            result["addedItems"] = addedItems;
            hasChanges = true;
        }
        if (deletedItems.Count > 0)
        {
            result["deletedItems"] = deletedItems;
            hasChanges = true;
        }
        if (editedItems.Count > 0)
        {
            result["editedItems"] = editedItems;
            hasChanges = true;
        }

        if (hasChanges)
        {
            if (addedItems.Count > 0 && deletedItems.Count == 0 && editedItems.Count == 0)
            {
                result["action"] = "added";
            }
            else if (deletedItems.Count > 0 && addedItems.Count == 0 && editedItems.Count == 0)
            {
                result["action"] = "deleted";
            }
            else
            {
                result["action"] = "edited";
            }
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