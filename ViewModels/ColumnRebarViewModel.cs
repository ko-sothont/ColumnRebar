using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ColumnRebar.Models;

namespace ColumnRebar.ViewModels
{
    public partial class ColumnRebarViewModel : ObservableObject
    {
        private Document _doc;
        public List<FamilyInstance> SelectedColumns { get; set; }
        [ObservableProperty] private ObservableCollection<RebarTypeOption> _availableRebarTypes;
        [ObservableProperty] private ObservableCollection<StirrupLayoutOption> _stirrupLayouts;
        [ObservableProperty] private string _spacingDense = "100";
        [ObservableProperty] private string _spacingSparse = "200";
        [ObservableProperty] private int _cx;
        [ObservableProperty] private int _cy;
        [ObservableProperty] private RebarTypeOption _selectedMainRebar;
        [ObservableProperty] private RebarTypeOption _selectedStirrupRebar;
        [ObservableProperty] private RebarTypeOption _selectedTieRebar;
        [ObservableProperty] private StirrupLayoutOption _selectedStirrupLayout;
        [ObservableProperty] private bool _isHookTie; // Radio: Đai móc
        [ObservableProperty] private bool _isClosedTie; // Radio: Đai lồng kín
        [ObservableProperty] private double _columnWidth = 400; // Giả sử mm
        [ObservableProperty] private double _columnHeight = 400; // Giả sử mm
        [ObservableProperty] private string _beamDepth = "450";
        [ObservableProperty] private string _beamCategory = "Structural Framing";
        [ObservableProperty] private string _topCover = "25";
        [ObservableProperty] private string _otherCover = "25";
        [ObservableProperty] private string _totalRebarText;
        [ObservableProperty] private string _rebarAreaText;
        [ObservableProperty] private string _rebarRatioText;
        [ObservableProperty] private ObservableCollection<RebarDot> _rebarDots = new();
        [ObservableProperty] private ObservableCollection<RebarLine> _internalTies = new();
        [ObservableProperty] private double _mainStirrupThickness = 1.5;
        [ObservableProperty] private ObservableCollection<ColumnPreviewItem> _columnPreviews = new();
        [ObservableProperty] private double _stirrupOffset = 50; // Cách mép rải đai
        [ObservableProperty] private bool _useMinL1 = true;
        [ObservableProperty] private double _minL1Value = 500;
        [ObservableProperty] private bool _useClearHeightDivide = true;
        [ObservableProperty] private double _clearHeightDivideValue = 4;
        [ObservableProperty] private bool _useColumnSectionHeight = true;
        [ObservableProperty] private ObservableCollection<RebarHookOption> _availableHooks = new();
        [ObservableProperty] private RebarHookOption _selectedMainTieHook;
        [ObservableProperty] private bool _isStirrupToBeamBottom = true;
        [ObservableProperty] private bool _isStirrupToSlabTop = false;
        [ObservableProperty] private bool _isSeismicBySpacing = true;
        [ObservableProperty] private bool _isSeismicByQuantity = false;
        [ObservableProperty] private double _seismicSpacing = 100;
        [ObservableProperty] private int _seismicQuantity = 3;
        [ObservableProperty] private bool _isAdditionalStirrupSameAsMain = true;
        [ObservableProperty] private bool _isAdditionalStirrupCustom = false;
        [ObservableProperty] private double _additionalStirrupSpacing = 200;
        [ObservableProperty] private RebarHookOption _selectedAdditionalTieHook;

        // === THÊM MỚI 1: Biến lưu trữ Preview đang được chọn ===
        [ObservableProperty] private ColumnPreviewItem _selectedColumnPreview;

        partial void OnCxChanged(int value) => RecalculateRebarInfo();
        partial void OnCyChanged(int value) => RecalculateRebarInfo();
        partial void OnSelectedMainRebarChanged(RebarTypeOption value) => RecalculateRebarInfo();
        partial void OnSelectedStirrupRebarChanged(RebarTypeOption value) => RecalculateRebarInfo();
        partial void OnSelectedTieRebarChanged(RebarTypeOption value) => RecalculateRebarInfo();
        private RebarDot _firstClickedDot = null;
        public List<Tuple<RebarDot, RebarDot>> _customClosedTies = new();

        partial void OnSelectedStirrupLayoutChanged(StirrupLayoutOption value)
        {
            UpdateRebarDiagram();
            GenerateColumnPreview();
        }

        // === THÊM MỚI 2: Xử lý sự kiện khi click chọn tầng ===
        partial void OnSelectedColumnPreviewChanged(ColumnPreviewItem value)
        {
            if (value != null)
            {
                ExtractExistingRebarFromRevit(value);
            }
        }

        public ColumnRebarViewModel(Document doc, List<FamilyInstance> columns)
        {
            _doc = doc;
            SelectedColumns = columns;

            LoadRebarTypesFromProject();
            LoadHooksFromProject();
            InitializeDefaultValues();
            ExtractColumnDataFromRevit();
            RecalculateRebarInfo();
        }

        private void LoadHooksFromProject()
        {
            var hookCollector = new FilteredElementCollector(_doc)
                .OfClass(typeof(RebarHookType))
                .Cast<RebarHookType>()
                .ToList();

            AvailableHooks.Clear();
            foreach (var hook in hookCollector)
            {
                AvailableHooks.Add(new RebarHookOption { Name = hook.Name, Id = hook.Id });
            }
        }

        private void LoadRebarTypesFromProject()
        {
            var collector = new FilteredElementCollector(_doc)
                .OfClass(typeof(RebarBarType))
                .Cast<RebarBarType>()
                .OrderBy(x => x.BarModelDiameter)
                .ToList();

            var options = new List<RebarTypeOption>();
            foreach (var type in collector)
            {
                double diameterMm = type.BarModelDiameter * 304.8;
                options.Add(new RebarTypeOption { Name = type.Name, DiameterMm = diameterMm, Id = type.Id });
            }

            AvailableRebarTypes = new ObservableCollection<RebarTypeOption>(options);
        }

        [RelayCommand]
        private void ClearAllTies()
        {
            _firstClickedDot = null;
            _customClosedTies.Clear();
            foreach (var dot in RebarDots)
            {
                dot.TieType = 0;
                dot.IsSelected = false;
            }
            RefreshInternalTies();
        }

        private void GenerateColumnPreview()
        {
            if (SelectedColumns == null || SelectedColumns.Count == 0) return;

            var previews = new List<ColumnPreviewItem>();
            string layoutName = SelectedStirrupLayout?.Name ?? "";
            var sortedColumns = SelectedColumns
                .OrderByDescending(c => c.get_BoundingBox(null).Min.Z)
                .ToList();
            double scaleFactor = 0.04;
            double actualBeamDepth = 450;
            if (!string.IsNullOrEmpty(BeamDepth)) double.TryParse(BeamDepth, out actualBeamDepth);

            foreach (var col in sortedColumns)
            {
                BoundingBoxXYZ bb = col.get_BoundingBox(null);
                double actualHeightMm = (bb.Max.Z - bb.Min.Z) * 304.8;

                ElementId baseLevelId = col.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM).AsElementId();
                Level baseLevel = _doc.GetElement(baseLevelId) as Level;
                string levelName = baseLevel != null ? $"▼ {baseLevel.Name} ({(baseLevel.Elevation * 304.8):F0})" : "Unknown Level";

                double uiHeight = actualHeightMm * scaleFactor;
                double uiBeamDepth = actualBeamDepth * scaleFactor;

                double uiWidth = ColumnWidth * scaleFactor;
                if (uiWidth < 30) uiWidth = 30; if (uiWidth > 80) uiWidth = 80;

                double clearUIHeight = uiHeight - uiBeamDepth;
                double lOver4 = clearUIHeight / 4;
                var stirrups = new List<double>();

                for (double y = 2; y <= uiBeamDepth; y += 4) stirrups.Add(y);

                if (layoutName.Contains("L1, L2, L1"))
                {
                    for (double y = uiBeamDepth; y <= uiBeamDepth + lOver4; y += 4) stirrups.Add(y);
                    for (double y = uiBeamDepth + lOver4 + 10; y <= uiHeight - lOver4; y += 10) stirrups.Add(y);
                    for (double y = uiHeight - lOver4 + 4; y <= uiHeight - 2; y += 4) stirrups.Add(y);
                }
                else if (layoutName == "L1")
                {
                    for (double y = uiBeamDepth; y <= uiHeight - 2; y += 6) stirrups.Add(y);
                }
                else if (layoutName.Contains("L1, L2"))
                {
                    for (double y = uiBeamDepth; y <= uiHeight - lOver4; y += 10) stirrups.Add(y);
                    for (double y = uiHeight - lOver4 + 4; y <= uiHeight - 2; y += 4) stirrups.Add(y);
                }

                previews.Add(new ColumnPreviewItem
                {
                    // === THÊM MỚI 3: Lưu Id của cột lại để sau này tìm thép ===
                    ColumnId = col.Id,

                    LevelName = levelName,
                    DimensionText = $"Height = {Math.Round(actualHeightMm)} (mm)\nBxH = {ColumnWidth}x{ColumnHeight}\nMark: {col.LookupParameter("Mark")?.AsString() ?? "Unknown"}",
                    RebarInfoText = $"Thép chủ: {TotalRebarText}\nKiểu phân bố: {layoutName}",
                    UIColumnHeight = uiHeight,
                    UIColumnWidth = uiWidth,
                    UIBeamDepth = uiBeamDepth,
                    StirrupPositions = new ObservableCollection<double>(stirrups)
                });
            }

            ColumnPreviews = new ObservableCollection<ColumnPreviewItem>(previews);
        }

        private void InitializeDefaultValues()
        {
            string packUri = "pack://application:,,,/ColumnRebar;component/Resources/Icons/";

            StirrupLayouts = new ObservableCollection<StirrupLayoutOption>
            {
                             new StirrupLayoutOption { Name = "L1, L2, L1", IconPath = packUri + "L1_L2_L1.png" },
                             new StirrupLayoutOption { Name = "L1", IconPath = packUri + "L1.png" },
                             new StirrupLayoutOption { Name = "L1, L2", IconPath = packUri + "L1_L2.png" }
            };

            Cx = 2;
            Cy = 2;
            IsHookTie = true;
            SelectedStirrupLayout = StirrupLayouts[0];

            if (AvailableRebarTypes != null && AvailableRebarTypes.Count > 0)
            {
                SelectedMainRebar = AvailableRebarTypes.Count > 3 ? AvailableRebarTypes[AvailableRebarTypes.Count - 3] : AvailableRebarTypes.Last();
                SelectedStirrupRebar = AvailableRebarTypes.First();
                SelectedTieRebar = AvailableRebarTypes.First();
            }

            if (AvailableHooks.Count > 0)
            {
                var defaultHook = AvailableHooks.FirstOrDefault(h => h.Name.Contains("135")) ?? AvailableHooks[0];
                SelectedMainTieHook = defaultHook;
                SelectedAdditionalTieHook = defaultHook;
            }
        }

        private void ExtractColumnDataFromRevit()
        {
            if (SelectedColumns == null || SelectedColumns.Count == 0) return;
            var firstCol = SelectedColumns[0];

            Parameter bParam = firstCol.Symbol.LookupParameter("b") ?? firstCol.Symbol.LookupParameter("Width");
            Parameter hParam = firstCol.Symbol.LookupParameter("h") ?? firstCol.Symbol.LookupParameter("Depth");
            if (bParam != null) ColumnWidth = bParam.AsDouble() * 304.8;
            if (hParam != null) ColumnHeight = hParam.AsDouble() * 304.8;

            Parameter topCoverParam = firstCol.get_Parameter(BuiltInParameter.CLEAR_COVER_TOP);
            if (topCoverParam != null)
            {
                var coverType = _doc.GetElement(topCoverParam.AsElementId()) as RebarCoverType;
                if (coverType != null) TopCover = Math.Round(coverType.CoverDistance * 304.8).ToString();
            }

            Parameter otherCoverParam = firstCol.get_Parameter(BuiltInParameter.CLEAR_COVER_OTHER);
            if (otherCoverParam != null)
            {
                var coverType = _doc.GetElement(otherCoverParam.AsElementId()) as RebarCoverType;
                if (coverType != null) OtherCover = Math.Round(coverType.CoverDistance * 304.8).ToString();
            }
        }

        // === THÊM MỚI 4: Hàm trích xuất dữ liệu thép đang có trên mô hình khi click tầng ===
        private void ExtractExistingRebarFromRevit(ColumnPreviewItem item)
        {
            var col = SelectedColumns.FirstOrDefault(c => c.Id == item.ColumnId);
            if (col == null) return;

            RebarHostData rebarHostData = RebarHostData.GetRebarHostData(col);
            if (rebarHostData == null) return;

            IList<Rebar> rebars = rebarHostData.GetRebarsInHost().Cast<Rebar>().ToList();
            if (!rebars.Any()) return;

            // --- SỬA LỖI 1: Phân loại thép chủ và thép đai an toàn ---
            List<Rebar> mainRebars = new List<Rebar>();
            List<Rebar> stirrups = new List<Rebar>();

            foreach (var r in rebars)
            {
                // Lấy hình dáng thép để kiểm tra
                RebarShape shape = _doc.GetElement(r.GetShapeId()) as RebarShape;
                if (shape != null && shape.RebarStyle == RebarStyle.StirrupTie)
                {
                    stirrups.Add(r);
                }
                else
                {
                    mainRebars.Add(r);
                }
            }

            // Lấy thông số thép chủ
            if (mainRebars.Any())
            {
                var mainBar = mainRebars.First();
                var matchedType = AvailableRebarTypes.FirstOrDefault(t => t.Id == mainBar.GetTypeId());
                if (matchedType != null) SelectedMainRebar = matchedType;
            }

            // Lấy thông số thép đai
            if (stirrups.Any())
            {
                var stirrup = stirrups.First();
                var matchedStirrupType = AvailableRebarTypes.FirstOrDefault(t => t.Id == stirrup.GetTypeId());
                if (matchedStirrupType != null) SelectedStirrupRebar = matchedStirrupType;

                // --- CÁCH MỚI: Đọc khoảng cách đai trực tiếp từ thuộc tính MaxSpacing ---
                // MaxSpacing luôn trả về đơn vị hệ nội bộ của Revit là feet
                double spacingFeet = stirrup.MaxSpacing;

                // Chuyển đổi feet sang milimet
                double spacingMm = Math.Round(spacingFeet * 304.8);

                if (spacingMm > 0)
                {
                    // Gán giá trị vào thuộc tính của ViewModel (ví dụ ô Đai dày)
                    SpacingDense = spacingMm.ToString();
                }
            }
        }

        private void UpdateRebarDiagram()
        {
            if (SelectedMainRebar == null || SelectedStirrupRebar == null) return;

            var dots = new List<RebarDot>();

            int cx = Cx < 2 ? 2 : Cx;
            int cy = Cy < 2 ? 2 : Cy;

            double dotSize = SelectedMainRebar.DiameterMm * 0.6;
            if (dotSize < 6) dotSize = 6;
            if (dotSize > 20) dotSize = 20;

            MainStirrupThickness = SelectedStirrupRebar.DiameterMm * 0.2;

            double offset = dotSize / 2;
            double canvasWidth = 240;
            double canvasHeight = 100;
            double spanX = canvasWidth - 2 * offset;
            double spanY = canvasHeight - 2 * offset;

            for (int i = 0; i < cx; i++)
            {
                double x = offset + ((double)i / (cx - 1)) * spanX;
                bool isCorner = (i == 0 || i == cx - 1);

                dots.Add(new RebarDot { X = x, Y = offset, Size = dotSize, IsCorner = isCorner, OppositeX = x, OppositeY = offset + spanY });
                dots.Add(new RebarDot { X = x, Y = offset + spanY, Size = dotSize, IsCorner = isCorner, OppositeX = x, OppositeY = offset });
            }

            for (int i = 1; i < cy - 1; i++)
            {
                double y = offset + ((double)i / (cy - 1)) * spanY;
                dots.Add(new RebarDot { X = offset, Y = y, Size = dotSize, IsCorner = false, OppositeX = offset + spanX, OppositeY = y });
                dots.Add(new RebarDot { X = offset + spanX, Y = y, Size = dotSize, IsCorner = false, OppositeX = offset, OppositeY = y });
            }

            RebarDots = new ObservableCollection<RebarDot>(dots);
            InternalTies.Clear();
        }

        [RelayCommand]
        private void ToggleHook(RebarDot clickedDot)
        {
            if (clickedDot == null || clickedDot.IsCorner) return;

            if (IsClosedTie)
            {
                if (_firstClickedDot == null)
                {
                    _firstClickedDot = clickedDot;
                    _firstClickedDot.IsSelected = true;
                }
                else
                {
                    if (_firstClickedDot != clickedDot)
                    {
                        _customClosedTies.Add(Tuple.Create(_firstClickedDot, clickedDot));
                    }

                    _firstClickedDot.IsSelected = false;
                    _firstClickedDot = null;
                    RefreshInternalTies();
                }
            }
            else
            {
                if (_firstClickedDot != null) _firstClickedDot.IsSelected = false;
                _firstClickedDot = null;

                clickedDot.TieType = clickedDot.TieType == 1 ? 0 : 1;

                foreach (var dot in RebarDots)
                {
                    if (Math.Abs(dot.X - clickedDot.OppositeX) < 0.1 && Math.Abs(dot.Y - clickedDot.OppositeY) < 0.1)
                    {
                        dot.TieType = clickedDot.TieType;
                        break;
                    }
                }
                RefreshInternalTies();
            }
        }

        private void RefreshInternalTies()
        {
            if (SelectedTieRebar == null) return;
            var ties = new List<RebarLine>();
            double tieThickness = SelectedTieRebar.DiameterMm * 0.2;
            var drawn = new HashSet<string>();

            foreach (var dot in RebarDots)
            {
                if (dot.TieType == 1)
                {
                    string key = $"HOOK_{Math.Min(dot.X, dot.OppositeX):F1}_{Math.Min(dot.Y, dot.OppositeY):F1}";
                    if (!drawn.Contains(key))
                    {
                        double r = (dot.Size / 2) + (tieThickness / 2) + 0.5;
                        string path = "";
                        if (Math.Abs(dot.X - dot.OppositeX) < 1)
                        {
                            double x = dot.X; double y1 = Math.Min(dot.Y, dot.OppositeY); double y2 = Math.Max(dot.Y, dot.OppositeY);
                            path = $"M {x + r - 4},{y1 + 4} L {x + r},{y1} A {r},{r} 0 0 0 {x - r},{y1} L {x - r},{y2} A {r},{r} 0 0 0 {x + r},{y2} L {x + r - 4},{y2 - 4}";
                        }
                        else
                        {
                            double y = dot.Y; double x1 = Math.Min(dot.X, dot.OppositeX); double x2 = Math.Max(dot.X, dot.OppositeX);
                            path = $"M {x1 + 4},{y + r - 4} L {x1},{y + r} A {r},{r} 0 0 1 {x1},{y - r} L {x2},{y - r} A {r},{r} 0 0 1 {x2},{y + r} L {x2 - 4},{y + r - 4}";
                        }
                        ties.Add(new RebarLine { PathData = path, Thickness = tieThickness });
                        drawn.Add(key);
                    }
                }
            }

            foreach (var tie in _customClosedTies)
            {
                var d1 = tie.Item1;
                var d2 = tie.Item2;

                double minX = Math.Min(Math.Min(d1.X, d1.OppositeX), Math.Min(d2.X, d2.OppositeX));
                double maxX = Math.Max(Math.Max(d1.X, d1.OppositeX), Math.Max(d2.X, d2.OppositeX));
                double minY = Math.Min(Math.Min(d1.Y, d1.OppositeY), Math.Min(d2.Y, d2.OppositeY));
                double maxY = Math.Max(Math.Max(d1.Y, d1.OppositeY), Math.Max(d2.Y, d2.OppositeY));

                double r = (d1.Size / 2) + (tieThickness / 2) + 0.5;

                string path = $"M {minX + r},{minY} L {maxX - r},{minY} A {r},{r} 0 0 1 {maxX},{minY + r} L {maxX},{maxY - r} A {r},{r} 0 0 1 {maxX - r},{maxY} L {minX + r},{maxY} A {r},{r} 0 0 1 {minX},{maxY - r} L {minX},{minY + r} A {r},{r} 0 0 1 {minX + r},{minY} Z";

                ties.Add(new RebarLine { PathData = path, Thickness = tieThickness });
            }

            InternalTies = new ObservableCollection<RebarLine>(ties);
        }

        private void RecalculateRebarInfo()
        {
            if (SelectedMainRebar == null) return;

            int totalRebars = (Cx * 2) + (Cy * 2) - 4;
            if (totalRebars < 4) totalRebars = 4;

            TotalRebarText = $"{totalRebars} {SelectedMainRebar.Name}";

            double radiusCm = SelectedMainRebar.DiameterMm / 20.0;
            double areaCm2 = totalRebars * Math.PI * Math.Pow(radiusCm, 2);

            RebarAreaText = $"{Math.Round(areaCm2, 3)} (cm²)";

            double colAreaCm2 = (ColumnWidth / 10.0) * (ColumnHeight / 10.0);
            if (colAreaCm2 > 0)
            {
                double ratio = (areaCm2 / colAreaCm2) * 100;
                RebarRatioText = $"{Math.Round(ratio, 2)} %";
            }

            UpdateRebarDiagram();
            RefreshInternalTies();
            GenerateColumnPreview();
        }

        [RelayCommand]
        private void ApplyToOtherFloors()
        {
            // Logic lưu cấu hình của tầng hiện tại copy cho các tầng trên
        }

        [RelayCommand]
        private void Run(System.Windows.Window window)
        {
            using (Transaction t = new Transaction(_doc, "Vẽ thép cột toàn diện"))
            {
                t.Start();
                try
                {
                    // ===============================================
                    // 1. TÌM KIẾM VÀ TỰ ĐỘNG TẠO MÓC (NẾU THIẾU)
                    // ===============================================
                    string mainHookName = SelectedMainTieHook != null ? SelectedMainTieHook.Name : "135";
                    string addHookName = SelectedAdditionalTieHook != null ? SelectedAdditionalTieHook.Name : mainHookName;

                    RebarHookType mainStirrupHook = new FilteredElementCollector(_doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>()
                        .FirstOrDefault(h => h.Style == RebarStyle.StirrupTie && h.Name == mainHookName)
                        ?? new FilteredElementCollector(_doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>().FirstOrDefault(h => h.Style == RebarStyle.StirrupTie && (h.Name.Contains("135") || h.Name.Contains("Seismic")))
                        ?? new FilteredElementCollector(_doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>().FirstOrDefault(h => h.Style == RebarStyle.StirrupTie);

                    RebarHookType addStirrupHook = new FilteredElementCollector(_doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>()
                        .FirstOrDefault(h => h.Style == RebarStyle.StirrupTie && h.Name == addHookName) ?? mainStirrupHook;

                    double targetAngleRad = addHookName.Contains("135") ? (135.0 * Math.PI / 180.0) :
                                             addHookName.Contains("180") ? (180.0 * Math.PI / 180.0) : (90.0 * Math.PI / 180.0);

                    RebarHookType addStandardHook = new FilteredElementCollector(_doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>()
                        .FirstOrDefault(h => h.Style == RebarStyle.Standard && Math.Abs(h.HookAngle - targetAngleRad) < 0.01);

                    if (addStandardHook == null)
                    {
                        RebarHookType baseStandardHook = new FilteredElementCollector(_doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>()
                            .FirstOrDefault(h => h.Style == RebarStyle.Standard);

                        if (baseStandardHook != null)
                        {
                            try
                            {
                                string newHookName = $"Standard - {(int)(targetAngleRad * 180.0 / Math.PI)} deg (Auto)";
                                addStandardHook = baseStandardHook.Duplicate(newHookName) as RebarHookType;
                                Parameter angleParam = addStandardHook.get_Parameter(BuiltInParameter.REBAR_HOOK_ANGLE);
                                if (angleParam != null && !angleParam.IsReadOnly) angleParam.Set(targetAngleRad);
                            }
                            catch { addStandardHook = baseStandardHook; }
                        }
                    }

                    // === THÊM MỚI 5: Quyết định vẽ cột nào thay vì fix cứng SelectedColumns ===
                    List<FamilyInstance> columnsToRun = new List<FamilyInstance>();
                    if (SelectedColumnPreview != null)
                    {
                        // Vẽ TẦNG ĐƯỢC CHỌN
                        var selectedCol = SelectedColumns.FirstOrDefault(c => c.Id == SelectedColumnPreview.ColumnId);
                        if (selectedCol != null) columnsToRun.Add(selectedCol);
                    }
                    else
                    {
                        // Vẽ TẤT CẢ nếu không click cột nào
                        columnsToRun = SelectedColumns;
                    }

                    // Thay đổi foreach để sử dụng danh sách mới ở trên
                    foreach (var col in columnsToRun)
                    {
                        // CHUẨN BỊ DỮ LIỆU
                        RebarBarType mainBarType = _doc.GetElement(SelectedMainRebar.Id) as RebarBarType;
                        RebarBarType stirrupBarType = _doc.GetElement(SelectedStirrupRebar.Id) as RebarBarType;
                        RebarBarType tieBarType = _doc.GetElement(SelectedTieRebar.Id) as RebarBarType;

                        double.TryParse(TopCover, out double topCoverMm);
                        double.TryParse(OtherCover, out double otherCoverMm);
                        double.TryParse(BeamDepth, out double beamDepthMm);

                        double topCoverFeet = topCoverMm / 304.8;
                        double otherCoverFeet = otherCoverMm / 304.8;
                        double beamDepthFeet = beamDepthMm / 304.8;

                        BoundingBoxXYZ bb = col.get_BoundingBox(null);
                        double minZ = bb.Min.Z + (StirrupOffset / 304.8);
                        double maxZ_BeamBottom = bb.Max.Z - beamDepthFeet;
                        double maxZ_MainRebar = bb.Max.Z - topCoverFeet;

                        double mainBarRadius = mainBarType.BarModelDiameter / 2.0;
                        double stirrupDiameter = stirrupBarType.BarModelDiameter;

                        double offsetRev = otherCoverFeet + stirrupDiameter + mainBarRadius;
                        double spanX_Rev = (bb.Max.X - bb.Min.X) - 2 * offsetRev;
                        double spanY_Rev = (bb.Max.Y - bb.Min.Y) - 2 * offsetRev;

                        // ===============================================
                        // 2. THUẬT TOÁN ÁNH XẠ UI -> REVIT 
                        // ===============================================
                        double dotSizeUI = SelectedMainRebar.DiameterMm * 0.6;
                        if (dotSizeUI < 6) dotSizeUI = 6; if (dotSizeUI > 20) dotSizeUI = 20;
                        double offsetUI = dotSizeUI / 2;
                        double spanX_UI = 240 - 2 * offsetUI;
                        double spanY_UI = 100 - 2 * offsetUI;

                        bool isSwapped = spanY_Rev > spanX_Rev;

                        Func<double, double, XYZ> GetRevitPt = (uiX, uiY) => {
                            double ratioX = (uiX - offsetUI) / spanX_UI;
                            double ratioY = (uiY - offsetUI) / spanY_UI;

                            double px, py;
                            if (!isSwapped)
                            {
                                px = bb.Min.X + offsetRev + ratioX * spanX_Rev;
                                py = bb.Min.Y + offsetRev + ratioY * spanY_Rev;
                            }
                            else
                            {
                                px = bb.Min.X + offsetRev + ratioY * spanX_Rev;
                                py = bb.Min.Y + offsetRev + ratioX * spanY_Rev;
                            }
                            return new XYZ(px, py, 0);
                        };

                        // ===============================================
                        // 3. TẠO THÉP CHỦ
                        // ===============================================
                        var drawnMainBars = new HashSet<string>();
                        foreach (var dot in RebarDots)
                        {
                            string key = $"{dot.X:F1}_{dot.Y:F1}";
                            if (!drawnMainBars.Contains(key))
                            {
                                XYZ pt = GetRevitPt(dot.X, dot.Y);
                                Line mainBarLine = Line.CreateBound(new XYZ(pt.X, pt.Y, minZ), new XYZ(pt.X, pt.Y, maxZ_MainRebar));
                                Rebar.CreateFromCurves(_doc, RebarStyle.Standard, mainBarType, null, null, col, XYZ.BasisX, new List<Curve> { mainBarLine }, RebarHookOrientation.Right, RebarHookOrientation.Right, true, true);
                                drawnMainBars.Add(key);
                            }
                        }

                        // ===============================================
                        // 4. HÀM TẠO MẢNG THÉP ĐAI LÕI (TRẢ VỀ NGUYÊN BẢN SẠCH SẼ)
                        // ===============================================
                        double sDense = 100.0 / 304.8;
                        if (double.TryParse(SpacingDense, out double sd) && sd > 0) sDense = sd / 304.8;
                        double sSparse = 200.0 / 304.8;
                        if (double.TryParse(SpacingSparse, out double ss) && ss > 0) sSparse = ss / 304.8;

                        double clearHeight = maxZ_BeamBottom - minZ;
                        double lOver4 = clearHeight / 4.0;

                        Action<List<Curve>, double, double, double, RebarBarType, RebarStyle, RebarHookType, RebarHookOrientation, RebarHookOrientation> CreateStirrupArray =
                            (profile, startZ, endZ, spacing, barType, style, hook, startOrient, endOrient) =>
                            {
                                if (endZ - startZ < 0.1 || profile == null || profile.Count == 0) return;
                                List<Curve> shiftedProfile = profile.Select(c => c.CreateTransformed(Transform.CreateTranslation(new XYZ(0, 0, startZ)))).ToList();

                                try
                                {
                                    Rebar stirrup = Rebar.CreateFromCurves(_doc, style, barType, hook, hook, col, XYZ.BasisZ, shiftedProfile, startOrient, endOrient, true, true);
                                    if (stirrup != null)
                                    {
                                        stirrup.GetShapeDrivenAccessor().SetLayoutAsMaximumSpacing(spacing, endZ - startZ, true, true, true);
                                    }
                                }
                                catch { }
                            };

                        Action<List<Curve>, RebarBarType, RebarStyle, RebarHookType, RebarHookOrientation, RebarHookOrientation, bool> ProcessProfileZones =
                            (profile, barType, style, hook, startOrient, endOrient, isAdditionalTie) =>
                            {
                                if (isAdditionalTie && IsAdditionalStirrupCustom)
                                {
                                    double customSpc = AdditionalStirrupSpacing / 304.8;
                                    if (customSpc <= 0) customSpc = sDense;
                                    CreateStirrupArray(profile, minZ, maxZ_BeamBottom, customSpc, barType, style, hook, startOrient, endOrient);
                                }
                                else
                                {
                                    string layout = SelectedStirrupLayout?.Name ?? "";
                                    if (layout.Contains("L1, L2, L1"))
                                    {
                                        CreateStirrupArray(profile, minZ, minZ + lOver4, sDense, barType, style, hook, startOrient, endOrient);
                                        CreateStirrupArray(profile, minZ + lOver4, maxZ_BeamBottom - lOver4, sSparse, barType, style, hook, startOrient, endOrient);
                                        CreateStirrupArray(profile, maxZ_BeamBottom - lOver4, maxZ_BeamBottom, sDense, barType, style, hook, startOrient, endOrient);
                                    }
                                    else if (layout == "L1")
                                    {
                                        CreateStirrupArray(profile, minZ, maxZ_BeamBottom, sSparse, barType, style, hook, startOrient, endOrient);
                                    }
                                    else
                                    {
                                        CreateStirrupArray(profile, minZ, maxZ_BeamBottom - lOver4, sSparse, barType, style, hook, startOrient, endOrient);
                                        CreateStirrupArray(profile, maxZ_BeamBottom - lOver4, maxZ_BeamBottom, sDense, barType, style, hook, startOrient, endOrient);
                                    }
                                }

                                if (IsStirrupToSlabTop)
                                {
                                    double jointHeight = maxZ_MainRebar - maxZ_BeamBottom;
                                    if (jointHeight > 0)
                                    {
                                        double sSeismic = SeismicSpacing / 304.8;
                                        if (IsSeismicByQuantity && SeismicQuantity > 0) sSeismic = jointHeight / (SeismicQuantity + 1);
                                        CreateStirrupArray(profile, maxZ_BeamBottom, maxZ_MainRebar, sSeismic, barType, style, hook, startOrient, endOrient);
                                    }
                                }
                            };

                        // ===============================================
                        // 5. VẼ ĐAI BAO NGOÀI
                        // ===============================================
                        double stirrupExt = mainBarRadius + (stirrupBarType.BarModelDiameter / 2.0);
                        XYZ p1 = new XYZ(bb.Min.X + offsetRev - stirrupExt, bb.Min.Y + offsetRev - stirrupExt, 0);
                        XYZ p2 = new XYZ(bb.Max.X - offsetRev + stirrupExt, bb.Min.Y + offsetRev - stirrupExt, 0);
                        XYZ p3 = new XYZ(bb.Max.X - offsetRev + stirrupExt, bb.Max.Y - offsetRev + stirrupExt, 0);
                        XYZ p4 = new XYZ(bb.Min.X + offsetRev - stirrupExt, bb.Max.Y - offsetRev + stirrupExt, 0);
                        List<Curve> outerProfile = new List<Curve> { Line.CreateBound(p1, p2), Line.CreateBound(p2, p3), Line.CreateBound(p3, p4), Line.CreateBound(p4, p1) };

                        ProcessProfileZones(outerProfile, stirrupBarType, RebarStyle.StirrupTie, mainStirrupHook, RebarHookOrientation.Left, RebarHookOrientation.Left, false);

                        // ===============================================
                        // 6. VẼ ĐAI PHỤ BÊN TRONG 
                        // ===============================================
                        double tieExt = mainBarRadius + (tieBarType.BarModelDiameter / 2.0);

                        var drawnHooks = new HashSet<string>();
                        foreach (var dot in RebarDots)
                        {
                            if (dot.TieType == 1)
                            {
                                string key = $"{Math.Min(dot.X, dot.OppositeX):F1}_{Math.Min(dot.Y, dot.OppositeY):F1}";
                                if (!drawnHooks.Contains(key))
                                {
                                    XYZ pt1 = GetRevitPt(dot.X, dot.Y);
                                    XYZ pt2 = GetRevitPt(dot.OppositeX, dot.OppositeY);

                                    XYZ dir = (pt2 - pt1).Normalize();
                                    XYZ perpLeft = XYZ.BasisZ.CrossProduct(dir).Normalize();

                                    XYZ offsetPt1 = pt1 + perpLeft * tieExt;
                                    XYZ offsetPt2 = pt2 + perpLeft * tieExt;

                                    XYZ extPt1 = offsetPt1 - dir * mainBarRadius;
                                    XYZ extPt2 = offsetPt2 + dir * mainBarRadius;

                                    List<Curve> hookProfile = new List<Curve> { Line.CreateBound(extPt1, extPt2) };

                                    ProcessProfileZones(hookProfile, tieBarType, RebarStyle.Standard, addStandardHook, RebarHookOrientation.Right, RebarHookOrientation.Right, true);
                                    drawnHooks.Add(key);
                                }
                            }
                        }

                        if (_customClosedTies != null)
                        {
                            foreach (var tie in _customClosedTies)
                            {
                                XYZ pt1 = GetRevitPt(tie.Item1.X, tie.Item1.Y);
                                XYZ pt2 = GetRevitPt(tie.Item2.X, tie.Item2.Y);

                                double minX = Math.Min(pt1.X, pt2.X) - tieExt;
                                double maxX = Math.Max(pt1.X, pt2.X) + tieExt;
                                double minY = Math.Min(pt1.Y, pt2.Y) - tieExt;
                                double maxY = Math.Max(pt1.Y, pt2.Y) + tieExt;

                                XYZ c1 = new XYZ(minX, minY, 0); XYZ c2 = new XYZ(maxX, minY, 0);
                                XYZ c3 = new XYZ(maxX, maxY, 0); XYZ c4 = new XYZ(minX, maxY, 0);

                                if (Math.Abs(maxX - minX) > 0.01 && Math.Abs(maxY - minY) > 0.01)
                                {
                                    List<Curve> closedProfile = new List<Curve> { Line.CreateBound(c1, c2), Line.CreateBound(c2, c3), Line.CreateBound(c3, c4), Line.CreateBound(c4, c1) };
                                    ProcessProfileZones(closedProfile, tieBarType, RebarStyle.StirrupTie, addStirrupHook, RebarHookOrientation.Left, RebarHookOrientation.Left, true);
                                }
                            }
                        }
                    }

                    t.Commit();
                    if (window != null) window.Close();
                }
                catch (Exception ex)
                {
                    t.RollBack();
                    System.Windows.MessageBox.Show("Lỗi khi vẽ thép: " + ex.Message);
                }
            }
        }
    }
}