
namespace SpyAndScrape.FileSystem;

// Json-File-Handler
public class JFH
{
    private readonly string _baseDir;

    public JFH()
    {
        _baseDir = AppDomain.CurrentDomain.BaseDirectory;
    }

    // creates or overwrites a JSON file frfr
    public void CreateOverwriteJFile(string fName, string content)
    {
        string fPath = GetFilePath(fName);
        File.WriteAllText(fPath, content);
    }

    // appnds content to a JSON file if not existant creates it
    public void AppendToJFile(string fName, string content)
    {
        string fPath = GetFilePath(fName);
        File.AppendAllText(fPath, content);
    }
    
    public bool FileExists(string fName)
    {
        string fPath = GetFilePath(fName);
        return File.Exists(fPath);
    }

    public bool IsFileEmpty(string fName)
    {
        string fPath = GetFilePath(fName);
        return !File.Exists(fPath) || new FileInfo(fPath).Length == 0;
    }
    
    public void DeleteFile(string fName)
    {
        string fPath = GetFilePath(fName);
        if (File.Exists(fPath))
        {
            File.Delete(fPath);
        }
    }

    public (int count, string[] filePaths) CountAndListTypeFiles(string fType = "*.json") //god since when tf do we need * there Im really stupid
    {
        string[] files = Directory.GetFiles(_baseDir, fType);
    
        // returning both is crazy, called a "tulpe"
        return (files.Length, files);
    }

    public string GetJContents(string fName)
    {
        string fPath = GetFilePath(fName);
        if (File.Exists(fPath))
        {
            return File.ReadAllText(fPath);
        }
        else
        {
            // throw instead of return also working WHAT!!!!
            // changed but this worked lol
            return string.Empty;
        }
    }
    
    
    // only a helper method to get full name if required
    private string GetFilePath(string fName)
    {
        if (!fName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            fName += ".json";
        }
        return Path.Combine(_baseDir, fName);
    }
}