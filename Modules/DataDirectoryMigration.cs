using System;
using System.IO;

namespace TOHE.Modules;

internal static class DataDirectoryMigration
{
    private static string rootDirectory;

    internal static string RootDirectory => Path.GetFullPath(rootDirectory ?? GetDefaultRoot());
    internal static string DataDirectory => Path.Combine(RootDirectory, "TOHEE-DATA");
    internal static string LanguageDirectory => Path.Combine(RootDirectory, "Language");

    internal static void Initialize()
    {
        rootDirectory = Path.GetFullPath(GetDefaultRoot());
        string oldDirectory = Path.Combine(rootDirectory, "TOHE-DATA");
        string newDirectory = DataDirectory;
        string oldSaveData = Path.Combine(oldDirectory, "SaveData");
        if (Directory.Exists(oldSaveData) && (File.GetAttributes(oldSaveData) & FileAttributes.Hidden) != 0)
            File.SetAttributes(oldSaveData, File.GetAttributes(oldSaveData) & ~FileAttributes.Hidden);

        if (!Directory.Exists(newDirectory) && Directory.Exists(oldDirectory))
        {
            string temporaryDirectory = newDirectory + ".migration-" + Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(temporaryDirectory);
                foreach (string directory in Directory.EnumerateDirectories(oldDirectory, "*", SearchOption.AllDirectories))
                    Directory.CreateDirectory(Path.Combine(temporaryDirectory, Path.GetRelativePath(oldDirectory, directory)));
                foreach (string file in Directory.EnumerateFiles(oldDirectory, "*", SearchOption.AllDirectories))
                    File.Copy(file, Path.Combine(temporaryDirectory, Path.GetRelativePath(oldDirectory, file)));
                Directory.Move(temporaryDirectory, newDirectory);
            }
            catch
            {
                if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, true);
                throw;
            }
        }

        string saveData = Path.Combine(newDirectory, "SaveData");
        if (Directory.Exists(saveData) && (File.GetAttributes(saveData) & FileAttributes.Hidden) != 0)
            File.SetAttributes(saveData, File.GetAttributes(saveData) & ~FileAttributes.Hidden);
    }

    private static string GetDefaultRoot()
    {
#if ANDROID
        return Environment.GetEnvironmentVariable("STAR_DATA_PATH")
            ?? throw new InvalidOperationException("STAR_DATA_PATH is required for Starlight data storage.");
#else
        return Environment.CurrentDirectory;
#endif
    }
}
