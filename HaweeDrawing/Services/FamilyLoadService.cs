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
                catch
                {
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
                    return true;

                Family family;
                bool loaded = doc.LoadFamily(familyPath, out family);

                return loaded && family != null;
            }
            catch
            {
                return false;
            }
        }

        public FamilySymbol GetOrLoadFamilySymbol(Document doc, string familyName, string typeName, string searchDirectory)
        {
            if (doc == null || string.IsNullOrEmpty(familyName))
                return null;

            var existingSymbol = FindFamilySymbol(doc, familyName, typeName);
            if (existingSymbol != null)
            {
                if (!existingSymbol.IsActive)
                {
                    try { existingSymbol.Activate(); doc.Regenerate(); }
                    catch { }
                }
                return existingSymbol;
            }

            if (!string.IsNullOrEmpty(searchDirectory) && Directory.Exists(searchDirectory))
            {
                var rfaPath = FindFamilyFile(searchDirectory, familyName);
                if (!string.IsNullOrEmpty(rfaPath) && LoadFamily(doc, rfaPath))
                {
                    doc.Regenerate();
                    var newSymbol = FindFamilySymbol(doc, familyName, typeName);
                    if (newSymbol != null)
                    {
                        if (!newSymbol.IsActive)
                        {
                            try { newSymbol.Activate(); doc.Regenerate(); }
                            catch { }
                        }
                        return newSymbol;
                    }
                }
            }

            return null;
        }

        private FamilySymbol FindFamilySymbol(Document doc, string familyName, string typeName)
        {
            // List of categories to search for fittings
            var categoriesToSearch = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_PipeFitting,
                BuiltInCategory.OST_PipeAccessory,
                BuiltInCategory.OST_MechanicalEquipment,
                BuiltInCategory.OST_PlumbingFixtures,
                BuiltInCategory.OST_GenericModel
            };

            // Try to find in specific categories first
            foreach (var category in categoriesToSearch)
            {
                try
                {
                    var collector = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilySymbol))
                        .OfCategory(category)
                        .Cast<FamilySymbol>();

                    // Try exact match first (both family name and type name)
                    var exactMatch = collector.FirstOrDefault(fs =>
                        fs.Family.Name.Equals(familyName, StringComparison.OrdinalIgnoreCase) &&
                        fs.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));

                    if (exactMatch != null)
                        return exactMatch;

                    var familyMatch = collector.FirstOrDefault(fs =>
                        fs.Family.Name.Equals(familyName, StringComparison.OrdinalIgnoreCase));

                    if (familyMatch != null)
                        return familyMatch;
                }
                catch
                {
                }
            }

            try
            {
                var allSymbols = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>();

                var exactMatch = allSymbols.FirstOrDefault(fs =>
                    fs.Family.Name.Equals(familyName, StringComparison.OrdinalIgnoreCase) &&
                    fs.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));

                if (exactMatch != null)
                    return exactMatch;

                var familyMatch = allSymbols.FirstOrDefault(fs =>
                    fs.Family.Name.Equals(familyName, StringComparison.OrdinalIgnoreCase));

                if (familyMatch != null)
                    return familyMatch;

                return FindFuzzyMatch(allSymbols.ToList(), familyName, typeName);
            }
            catch
            {
            }

            return null;
        }

        private FamilySymbol FindFuzzyMatch(List<FamilySymbol> symbols, string familyName, string typeName)
        {
            // Define common fitting type keywords and their generic equivalents
            var fittingTypeMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "Elbow", new[] { "elbow", "codo", "coude", "bend", "90", "45" } },
                { "Tee", new[] { "tee", "t-piece", "t-branch", "branch", "trap", "siphon", "si phon" } },
                { "Cross", new[] { "cross", "cruz", "croix", "4-way" } },
                { "Cap", new[] { "cap", "tapa", "bouchon", "end cap", "plug" } },
                { "Coupling", new[] { "coupling", "union", "connector", "socket", "joint", "flexible joint" } },
                { "Transition", new[] { "transition", "reducer", "reduction", "concentric", "eccentric" } },
                { "Flange", new[] { "flange", "brida", "bride" } },
                { "Valve", new[] { "valve", "valvula", "vanne" } }
            };

            // Extract the type from family name (e.g., "HWE_uPVC_Siphon" -> might contain "Siphon", "Trap", etc.)
            string lowerFamilyName = familyName.ToLower();
            string lowerTypeName = (typeName ?? "").ToLower();

            foreach (var mapping in fittingTypeMap)
            {
                string genericType = mapping.Key; // e.g., "Elbow"
                string[] keywords = mapping.Value;

                // Check if any keyword matches
                bool familyMatches = keywords.Any(kw => lowerFamilyName.Contains(kw));
                bool typeMatches = keywords.Any(kw => lowerTypeName.Contains(kw));

                if (familyMatches || typeMatches)
                {
                    var match = symbols.FirstOrDefault(fs =>
                        fs.Family.Name.IndexOf(genericType, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        fs.Family.Name.IndexOf("Generic", StringComparison.OrdinalIgnoreCase) >= 0 && 
                        fs.Family.Name.IndexOf(genericType, StringComparison.OrdinalIgnoreCase) >= 0);

                    if (match != null)
                        return match;
                }
            }

            if (lowerFamilyName.Contains("sprinkler") || lowerTypeName.Contains("sprinkler"))
            {
                var coupling = symbols.FirstOrDefault(fs => 
                    fs.Family.Name.IndexOf("Coupling", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    fs.Family.Name.IndexOf("Tee", StringComparison.OrdinalIgnoreCase) >= 0);
                
                if (coupling != null)
                    return coupling;
            }

            return null;
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
