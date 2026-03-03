using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HaweeDrawing.Services
{
    /// <summary>
    /// Service for loading Revit families from .rfa files
    /// </summary>
    public interface IFamilyLoadService
    {
        /// <summary>
        /// Load all .rfa files from a directory
        /// </summary>
        int LoadFamiliesFromDirectory(Document doc, string directoryPath);

        /// <summary>
        /// Load a specific family file
        /// </summary>
        bool LoadFamily(Document doc, string familyPath);

        /// <summary>
        /// Get or load family symbol
        /// </summary>
        FamilySymbol GetOrLoadFamilySymbol(Document doc, string familyName, string typeName, string searchDirectory);
    }

    public class FamilyLoadService : IFamilyLoadService
    {
        public int LoadFamiliesFromDirectory(Document doc, string directoryPath)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));

            if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
                return 0;

            int loadedCount = 0;
            var rfaFiles = Directory.GetFiles(directoryPath, "*.rfa", SearchOption.AllDirectories);

            foreach (var rfaFile in rfaFiles)
            {
                try
                {
                    if (LoadFamily(doc, rfaFile))
                        loadedCount++;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load family: {rfaFile} - {ex.Message}");
                }
            }

            return loadedCount;
        }

        public bool LoadFamily(Document doc, string familyPath)
        {
            if (doc == null || string.IsNullOrEmpty(familyPath))
                return false;

            if (!File.Exists(familyPath))
                return false;

            try
            {
                // Check if family is already loaded
                string familyName = Path.GetFileNameWithoutExtension(familyPath);
                var existingFamily = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .FirstOrDefault(f => f.Name.Equals(familyName, StringComparison.OrdinalIgnoreCase));

                if (existingFamily != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Family already loaded: {familyName}");
                    return true;
                }

                // Load the family
                Family family;
                bool loaded = doc.LoadFamily(familyPath, out family);

                if (loaded && family != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Successfully loaded family: {familyName}");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading family {familyPath}: {ex.Message}");
                return false;
            }
        }

        public FamilySymbol GetOrLoadFamilySymbol(Document doc, string familyName, string typeName, string searchDirectory)
        {
            if (doc == null || string.IsNullOrEmpty(familyName))
                return null;

            // First, try to find existing symbol
            var existingSymbol = FindFamilySymbol(doc, familyName, typeName);
            if (existingSymbol != null)
                return existingSymbol;

            // If not found and search directory is provided, try to load from .rfa file
            if (!string.IsNullOrEmpty(searchDirectory) && Directory.Exists(searchDirectory))
            {
                var rfaPath = FindFamilyFile(searchDirectory, familyName);
                if (!string.IsNullOrEmpty(rfaPath))
                {
                    if (LoadFamily(doc, rfaPath))
                    {
                        // Try to find again after loading
                        return FindFamilySymbol(doc, familyName, typeName);
                    }
                }
            }

            return null;
        }

        private FamilySymbol FindFamilySymbol(Document doc, string familyName, string typeName)
        {
            var collector = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_PipeFitting)
                .Cast<FamilySymbol>();

            // Try exact match first
            var exactMatch = collector.FirstOrDefault(fs =>
                fs.Family.Name.Equals(familyName, StringComparison.OrdinalIgnoreCase) &&
                fs.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));

            if (exactMatch != null)
                return exactMatch;

            // Try family name only
            var familyMatch = collector.FirstOrDefault(fs =>
                fs.Family.Name.Equals(familyName, StringComparison.OrdinalIgnoreCase));

            return familyMatch;
        }

        private string FindFamilyFile(string searchDirectory, string familyName)
        {
            if (string.IsNullOrEmpty(searchDirectory) || !Directory.Exists(searchDirectory))
                return null;

            // Search for .rfa file with matching name
            var files = Directory.GetFiles(searchDirectory, "*.rfa", SearchOption.AllDirectories);
            
            foreach (var file in files)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                if (fileName.Equals(familyName, StringComparison.OrdinalIgnoreCase))
                    return file;
            }

            return null;
        }
    }
}
