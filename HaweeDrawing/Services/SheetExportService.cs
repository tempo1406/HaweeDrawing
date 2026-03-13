using Autodesk.Revit.DB;
using HaweeDrawing.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HaweeDrawing.Services
{
    public interface ISheetExportService
    {
        int ExportSheetsByLevelAndSystem(Document doc, IEnumerable<PipeModel> pipes);
    }

    public class SheetExportService : ISheetExportService
    {
        private const double SHEET_MARGIN = 0.08;
        private const double TITLEBLOCK_LEFT_RIGHT_MARGIN_RATIO = 0.02;
        private const double TITLEBLOCK_TOP_MARGIN_RATIO = 0.02;
        private const double TITLEBLOCK_BOTTOM_RESERVED_RATIO = 0.16;

        public int ExportSheetsByLevelAndSystem(Document doc, IEnumerable<PipeModel> pipes)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));

            if (pipes == null)
                throw new ArgumentNullException(nameof(pipes));

            var groupedPipes = pipes
                .GroupBy(p => new
                {
                    Level = string.IsNullOrWhiteSpace(p.LevelName) ? "NoLevel" : p.LevelName,
                    System = string.IsNullOrWhiteSpace(p.SystemTypeName) ? "NoSystem" : p.SystemTypeName
                })
                .ToList();

            if (groupedPipes.Count == 0)
                return 0;

            var titleBlockTypeId = GetDefaultTitleBlockTypeId(doc);
            if (titleBlockTypeId == ElementId.InvalidElementId)
                throw new InvalidOperationException("Title Block not found in project. Please load at least one Title Block before exporting sheets.");

            var existingSheetNumbers = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Select(s => s.SheetNumber),
                StringComparer.OrdinalIgnoreCase);

            int createdCount = 0;
            int sheetIndex = 1;
            var planViewType = GetFloorPlanViewType(doc);
            var draftingViewType = GetDraftingViewType(doc);
            var textNoteTypeId = GetDefaultTextNoteTypeId(doc);

            if (planViewType == null)
                throw new InvalidOperationException("Không t?m th?y lo?i view Floor Plan trong project.");

            if (draftingViewType == null)
                throw new InvalidOperationException("Không t?m th?y lo?i view Drafting trong project.");

            if (textNoteTypeId == ElementId.InvalidElementId)
                throw new InvalidOperationException("Không t?m th?y TextNoteType trong project.");

            using (var transaction = new Transaction(doc, "Create Sheets by Level and System"))
            {
                transaction.Start();

                foreach (var group in groupedPipes)
                {
                    var infoSheet = ViewSheet.Create(doc, titleBlockTypeId);
                    if (infoSheet != null)
                    {
                        infoSheet.Name = SanitizeRevitSheetName($"INFO - {group.Key.Level} - {group.Key.System}");
                        infoSheet.SheetNumber = GetUniqueSheetNumber(existingSheetNumbers, "HW-INF", sheetIndex);

                        var infoView = CreateInfoDraftingView(doc, draftingViewType, group.Key.Level, group.Key.System, group.ToList(), textNoteTypeId);
                        if (infoView != null)
                        {
                            PlaceViewOnSheetFit(doc, infoSheet, infoView, false);
                        }

                        createdCount++;
                    }

                    var technicalSheet = ViewSheet.Create(doc, titleBlockTypeId);
                    if (technicalSheet != null)
                    {
                        technicalSheet.Name = SanitizeRevitSheetName($"TECH - {group.Key.Level} - {group.Key.System}");
                        technicalSheet.SheetNumber = GetUniqueSheetNumber(existingSheetNumbers, "HW-TEC", sheetIndex);

                        var level = GetLevelByName(doc, group.Key.Level);
                        if (level != null)
                        {
                            var groupPipeIds = BuildElementIdsFromPipes(group).ToList();
                            var viewPlan = CreateSystemPlanView(doc, planViewType, level, group.Key.System, groupPipeIds);
                            if (viewPlan != null)
                            {
                                PlaceViewOnSheetFit(doc, technicalSheet, viewPlan, true);
                            }
                        }

                        createdCount++;
                    }

                    sheetIndex++;
                }

                transaction.Commit();
            }

            return createdCount;
        }

        private ViewFamilyType GetDraftingViewType(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(v => v.ViewFamily == ViewFamily.Drafting);
        }

        private ElementId GetDefaultTextNoteTypeId(Document doc)
        {
            var textNoteType = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .FirstOrDefault();

            return textNoteType?.Id ?? ElementId.InvalidElementId;
        }

        private ViewFamilyType GetFloorPlanViewType(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(v => v.ViewFamily == ViewFamily.FloorPlan);
        }

        private Level GetLevelByName(Document doc, string levelName)
        {
            if (string.IsNullOrWhiteSpace(levelName) ||
                levelName.Equals("NoLevel", StringComparison.OrdinalIgnoreCase) ||
                levelName.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .FirstOrDefault(l => l.Name.Equals(levelName, StringComparison.OrdinalIgnoreCase));
        }

        private ViewPlan CreateSystemPlanView(
            Document doc,
            ViewFamilyType planType,
            Level level,
            string systemName,
            List<ElementId> groupPipeIds)
        {
            if (groupPipeIds.Count == 0)
                return null;

            var viewPlan = DuplicateExistingPlanView(doc, level);
            if (viewPlan == null)
            {
                viewPlan = ViewPlan.Create(doc, planType.Id, level.Id);
            }

            if (viewPlan == null)
                return null;

            viewPlan.Name = GetUniqueViewName(doc, $"HW_{level.Name}_{systemName}_2D");
            viewPlan.Scale = 100;

            EnsurePipeCategoriesVisible(viewPlan);

            ApplyCropBoxFromPipes(doc, viewPlan, groupPipeIds);

            return viewPlan;
        }

        private ViewPlan DuplicateExistingPlanView(Document doc, Level level)
        {
            var sourceView = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .FirstOrDefault(v =>
                    !v.IsTemplate &&
                    v.GenLevel != null &&
                    v.GenLevel.Id == level.Id &&
                    v.ViewType == ViewType.FloorPlan);

            if (sourceView == null)
                return null;

            var duplicatedId = sourceView.Duplicate(ViewDuplicateOption.Duplicate);
            return doc.GetElement(duplicatedId) as ViewPlan;
        }

        private void EnsurePipeCategoriesVisible(ViewPlan viewPlan)
        {
            try
            {
                viewPlan.SetCategoryHidden(new ElementId(BuiltInCategory.OST_PipeCurves), false);
                viewPlan.SetCategoryHidden(new ElementId(BuiltInCategory.OST_PipeFitting), false);
                viewPlan.SetCategoryHidden(new ElementId(BuiltInCategory.OST_PipeAccessory), false);
            }
            catch
            {
            }
        }

        private void ApplyCropBoxFromPipes(Document doc, ViewPlan viewPlan, IEnumerable<ElementId> groupPipeIds)
        {
            BoundingBoxXYZ merged = null;

            foreach (var id in groupPipeIds)
            {
                var element = doc.GetElement(id);
                var bb = element?.get_BoundingBox(null);
                if (bb == null)
                    continue;

                if (merged == null)
                {
                    merged = new BoundingBoxXYZ
                    {
                        Min = bb.Min,
                        Max = bb.Max
                    };
                }
                else
                {
                    merged.Min = new XYZ(
                        Math.Min(merged.Min.X, bb.Min.X),
                        Math.Min(merged.Min.Y, bb.Min.Y),
                        Math.Min(merged.Min.Z, bb.Min.Z));
                    merged.Max = new XYZ(
                        Math.Max(merged.Max.X, bb.Max.X),
                        Math.Max(merged.Max.Y, bb.Max.Y),
                        Math.Max(merged.Max.Z, bb.Max.Z));
                }
            }

            if (merged == null)
                return;

            const double marginFeet = 3.0;
            var crop = new BoundingBoxXYZ
            {
                Min = new XYZ(merged.Min.X - marginFeet, merged.Min.Y - marginFeet, merged.Min.Z),
                Max = new XYZ(merged.Max.X + marginFeet, merged.Max.Y + marginFeet, merged.Max.Z)
            };

            viewPlan.CropBoxActive = true;
            viewPlan.CropBoxVisible = true;
            viewPlan.CropBox = crop;
        }

        private void PlaceViewOnSheet(Document doc, ViewSheet sheet, View view)
        {
            if (!Viewport.CanAddViewToSheet(doc, sheet.Id, view.Id))
                return;

            var center = new XYZ(
                (sheet.Outline.Min.U + sheet.Outline.Max.U) / 2.0,
                (sheet.Outline.Min.V + sheet.Outline.Max.V) / 2.0,
                0);

            Viewport.Create(doc, sheet.Id, view.Id, center);
        }

        private ViewDrafting CreateInfoDraftingView(
            Document doc,
            ViewFamilyType draftingViewType,
            string levelName,
            string systemName,
            List<PipeModel> groupPipes,
            ElementId textNoteTypeId)
        {
            var view = ViewDrafting.Create(doc, draftingViewType.Id);
            if (view == null)
                return null;

            view.Name = GetUniqueViewName(doc, $"INFO_{levelName}_{systemName}");
            view.Scale = 100;

            var infoText = BuildInfoText(levelName, systemName, groupPipes);
            var notePoint = new XYZ(0, 0, 0);
            var options = new TextNoteOptions(textNoteTypeId);
            TextNote.Create(doc, view.Id, notePoint, infoText, options);

            return view;
        }

        private string BuildInfoText(string levelName, string systemName, List<PipeModel> pipes)
        {
            var pipeCount = pipes?.Count ?? 0;
            var minDiameter = pipeCount > 0 ? pipes.Min(p => p.Diameter) : 0;
            var maxDiameter = pipeCount > 0 ? pipes.Max(p => p.Diameter) : 0;

            return
                "HAWEE PIPE SYSTEM INFORMATION\n" +
                "------------------------------\n" +
                "Level: " + levelName + "\n" +
                "System: " + systemName + "\n" +
                "Pipe count: " + pipeCount + "\n" +
                "Diameter range (m): " + minDiameter.ToString("0.####") + " - " + maxDiameter.ToString("0.####") + "\n" +
                "Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private void PlaceViewOnSheetFit(Document doc, ViewSheet sheet, View view, bool fitInsideTitleBlock)
        {
            if (!Viewport.CanAddViewToSheet(doc, sheet.Id, view.Id))
                return;

            var area = GetPlacementArea(doc, sheet, fitInsideTitleBlock);

            var center = new XYZ(
                (area.minU + area.maxU) / 2.0,
                (area.minV + area.maxV) / 2.0,
                0);

            var viewport = Viewport.Create(doc, sheet.Id, view.Id, center);
            if (viewport == null)
                return;

            FitViewportToSheet(doc, view, viewport, area.minU, area.maxU, area.minV, area.maxV);
        }

        private void FitViewportToSheet(Document doc, View view, Viewport viewport, double minU, double maxU, double minV, double maxV)
        {
            for (int i = 0; i < 8; i++)
            {
                doc.Regenerate();

                var viewportOutline = viewport.GetBoxOutline();
                var viewportWidth = viewportOutline.MaximumPoint.X - viewportOutline.MinimumPoint.X;
                var viewportHeight = viewportOutline.MaximumPoint.Y - viewportOutline.MinimumPoint.Y;

                var sheetWidth = maxU - minU;
                var sheetHeight = maxV - minV;

                var availableWidth = Math.Max(0.01, sheetWidth - (SHEET_MARGIN * 2));
                var availableHeight = Math.Max(0.01, sheetHeight - (SHEET_MARGIN * 2));

                var ratioW = viewportWidth / availableWidth;
                var ratioH = viewportHeight / availableHeight;
                var fitRatio = Math.Max(ratioW, ratioH);

                if (fitRatio <= 1.0)
                    break;

                int newScale = (int)Math.Ceiling(view.Scale * fitRatio * 1.05);
                if (newScale <= view.Scale)
                    newScale = view.Scale + 1;

                view.Scale = Math.Min(1000, newScale);
            }

            doc.Regenerate();

            var targetCenter = new XYZ(
                (minU + maxU) / 2.0,
                (minV + maxV) / 2.0,
                0);

            viewport.SetBoxCenter(targetCenter);
        }

        private (double minU, double maxU, double minV, double maxV) GetPlacementArea(Document doc, ViewSheet sheet, bool fitInsideTitleBlock)
        {
            double minU = sheet.Outline.Min.U;
            double maxU = sheet.Outline.Max.U;
            double minV = sheet.Outline.Min.V;
            double maxV = sheet.Outline.Max.V;

            if (!fitInsideTitleBlock)
                return (minU, maxU, minV, maxV);

            var titleBlock = new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType()
                .FirstOrDefault();

            var bb = titleBlock?.get_BoundingBox(sheet);
            if (bb == null)
                return (minU, maxU, minV, maxV);

            var width = bb.Max.X - bb.Min.X;
            var height = bb.Max.Y - bb.Min.Y;

            var safeMinU = bb.Min.X + (width * TITLEBLOCK_LEFT_RIGHT_MARGIN_RATIO);
            var safeMaxU = bb.Max.X - (width * TITLEBLOCK_LEFT_RIGHT_MARGIN_RATIO);
            var safeMinV = bb.Min.Y + (height * TITLEBLOCK_BOTTOM_RESERVED_RATIO);
            var safeMaxV = bb.Max.Y - (height * TITLEBLOCK_TOP_MARGIN_RATIO);

            if (safeMinU >= safeMaxU || safeMinV >= safeMaxV)
                return (minU, maxU, minV, maxV);

            return (safeMinU, safeMaxU, safeMinV, safeMaxV);
        }

        private IEnumerable<ElementId> BuildElementIdsFromPipes(IEnumerable<PipeModel> pipes)
        {
            foreach (var pipe in pipes)
            {
                if (pipe == null || string.IsNullOrWhiteSpace(pipe.Id))
                    continue;

                if (long.TryParse(pipe.Id, out long idValue) && idValue > 0)
                {
                    yield return new ElementId(idValue);
                }
            }
        }

        private string GetUniqueViewName(Document doc, string baseName)
        {
            var existingNames = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Select(v => v.Name),
                StringComparer.OrdinalIgnoreCase);

            var name = SanitizeRevitSheetName(baseName);
            if (!existingNames.Contains(name))
                return name;

            int index = 1;
            string candidate;
            do
            {
                candidate = string.Format("{0}_{1}", name, index);
                index++;
            }
            while (existingNames.Contains(candidate));

            return candidate;
        }

        private ElementId GetDefaultTitleBlockTypeId(Document doc)
        {
            var titleBlockType = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType()
                .FirstOrDefault();

            return titleBlockType?.Id ?? ElementId.InvalidElementId;
        }

        private string GetUniqueSheetNumber(HashSet<string> existingSheetNumbers, string prefix, int seed)
        {
            string finalName;
            int index = 1;

            do
            {
                finalName = string.Format("{0}-{1:D3}", prefix, seed + index - 1);
                index++;
            }
            while (existingSheetNumbers.Contains(finalName));

            existingSheetNumbers.Add(finalName);
            return finalName;
        }

        private string SanitizeRevitSheetName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Pipe Export";

            var sanitized = value.Replace("\r", " ").Replace("\n", " ").Trim();
            if (sanitized.Length > 120)
                sanitized = sanitized.Substring(0, 120);
            return sanitized;
        }
    }
}
