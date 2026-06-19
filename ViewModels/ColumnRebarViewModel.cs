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
        public ObservableCollection<TieStyleOption> AvailableTieStyles { get; set; }
        public TieStyleOption SelectedTieStyle { get; set; }
        public List<Tuple<RebarDot, RebarDot, int>> AdvancedClosedTies { get; set; } = new List<Tuple<RebarDot, RebarDot, int>>();
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
        private bool _isLoadingData = false; // Cờ chặn ghi đè dữ liệu khi đang load
        private ColumnPreviewItem _previousColumnPreview;

        partial void OnSelectedColumnPreviewChanged(ColumnPreviewItem value)
        {
            // Nếu đang trong quá trình load data thì không làm gì cả để tránh vòng lặp
            if (_isLoadingData) return;

            // 1. LƯU dữ liệu của tầng CŨ
            if (_previousColumnPreview != null && _previousColumnPreview != value)
            {
                SaveCurrentUiStateToModel(_previousColumnPreview);
            }

            // 2. TẢI dữ liệu của tầng MỚI
            if (value != null)
            {
                LoadDataForSelectedColumn(value);
            }

            _previousColumnPreview = value;
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
            foreach (var dot in RebarDots) dot.TieType = 0;
            AdvancedClosedTies.Clear();
            if (_firstClickedDot != null) { _firstClickedDot.IsSelected = false; _firstClickedDot = null; }
            RefreshInternalTies();
        }

        private void GenerateColumnPreview()
        {
            if (SelectedColumns == null || SelectedColumns.Count == 0) return;
            if (ColumnPreviews != null && ColumnPreviews.Count > 0)
            {
                if (SelectedColumnPreview != null)
                {
                    SelectedColumnPreview.RebarInfoText = $"Thép chủ: {TotalRebarText}\nKiểu phân bố: {SelectedStirrupLayout?.Name ?? ""}";
                }
                return; // Thoát hàm luôn, không cho chạy phần code "new List<ColumnPreviewItem>()" bên dưới
            }
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
            AvailableTieStyles = new ObservableCollection<TieStyleOption>
            {
                new TieStyleOption { Id = 1, Name = "1. Đai C / Đai thẳng (1 điểm)" },
                new TieStyleOption { Id = 2, Name = "2. Đai CN (Móc 135x135)" },
                new TieStyleOption { Id = 3, Name = "3. Đai 2 chữ C đối nhau" },
                new TieStyleOption { Id = 4, Name = "4. Đai CN (Dừng ở trung điểm)" },
                new TieStyleOption { Id = 5, Name = "5. Đai CN (Móc 135x90 + 1d)" },
                new TieStyleOption { Id = 6, Name = "6. Đai CN (Móc 135x135)" },
                new TieStyleOption { Id = 7, Name = "7. Đai chéo kép (135x90 + 1d)" },
                new TieStyleOption { Id = 8, Name = "8. Đai chéo kép (Móc 180x180)" }
            };

            SelectedTieStyle = AvailableTieStyles[0];
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

            List<Rebar> mainRebars = new List<Rebar>();
            List<Rebar> stirrups = new List<Rebar>();

            foreach (var r in rebars)
            {
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

            // --- 1. LẤY THÔNG SỐ THÉP CHỦ VÀ TÍNH CX, CY ---
            if (mainRebars.Any())
            {
                var mainBar = mainRebars.First();
                var matchedType = AvailableRebarTypes.FirstOrDefault(t => t.Id == mainBar.GetTypeId());

                // Phải gán vào item.Saved... để hàm LoadData phía sau sử dụng
                if (matchedType != null) item.SavedMainRebar = matchedType;

                // Đếm tổng số lượng thép chủ trong cột
                int totalMainBars = 0;
                foreach (var r in mainRebars)
                {
                    totalMainBars += r.Quantity;
                }

                // Nội suy Cx và Cy dựa vào tổng số thép và tỷ lệ BxH của cột
                if (totalMainBars >= 4)
                {
                    double ratio = (ColumnWidth > 0 && ColumnHeight > 0) ? (ColumnWidth / ColumnHeight) : 1.0;
                    double sumCxCy = (totalMainBars + 4) / 2.0;

                    int cy = (int)Math.Round(sumCxCy / (1.0 + ratio));
                    if (cy < 2) cy = 2;

                    int cx = (int)Math.Round(sumCxCy - cy);
                    if (cx < 2) cx = 2;

                    // Lưu vào item
                    item.SavedCx = cx;
                    item.SavedCy = cy;
                }
            }

            // --- 2. LẤY THÔNG SỐ THÉP ĐAI ---
            if (stirrups.Any())
            {
                var stirrup = stirrups.First();
                var matchedStirrupType = AvailableRebarTypes.FirstOrDefault(t => t.Id == stirrup.GetTypeId());

                if (matchedStirrupType != null) item.SavedStirrupRebar = matchedStirrupType;

                double spacingFeet = stirrup.MaxSpacing;
                double spacingMm = Math.Round(spacingFeet * 304.8);
                if (spacingMm > 0)
                {
                    item.SavedSpacingDense = spacingMm.ToString();
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
            if (clickedDot == null || clickedDot.IsCorner || SelectedTieStyle == null) return;

            if (SelectedTieStyle.Id == 1) // LOGIC CHO ITEM 1 (Click 1 điểm)
            {
                // Xóa điểm đang chọn dở dang (nếu có)
                if (_firstClickedDot != null) { _firstClickedDot.IsSelected = false; _firstClickedDot = null; }

                // Bật/tắt trạng thái móc của điểm được click
                clickedDot.TieType = clickedDot.TieType == 1 ? 0 : 1;

                // Đồng bộ điểm đối diện
                var oppDot = RebarDots.FirstOrDefault(d => Math.Abs(d.X - clickedDot.OppositeX) < 0.1 && Math.Abs(d.Y - clickedDot.OppositeY) < 0.1);
                if (oppDot != null) oppDot.TieType = clickedDot.TieType;
            }
            else // LOGIC CHO ITEM 2 ĐẾN 8 (Click 2 điểm)
            {
                if (_firstClickedDot == null) // Lần click đầu tiên
                {
                    _firstClickedDot = clickedDot;
                    _firstClickedDot.IsSelected = true; // Sáng màu điểm đầu tiên lên
                }
                else // Lần click thứ 2
                {
                    if (_firstClickedDot != clickedDot)
                    {
                        // Lưu lại cặp điểm + Id của loại thép vừa chọn
                        AdvancedClosedTies.Add(new Tuple<RebarDot, RebarDot, int>(_firstClickedDot, clickedDot, SelectedTieStyle.Id));
                    }

                    // Reset trạng thái
                    _firstClickedDot.IsSelected = false;
                    _firstClickedDot = null;
                }
            }

            RefreshInternalTies(); // Gọi hàm vẽ UI 2D
        }

        private void RefreshInternalTies()
        {
            if (SelectedTieRebar == null) return;
            var ties = new List<RebarLine>();
            double tieThickness = SelectedTieRebar.DiameterMm * 0.2;
            var drawn = new HashSet<string>();

            if (RebarDots != null)
            {
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
            }

            if (AdvancedClosedTies != null)
            {
                foreach (var tie in AdvancedClosedTies)
                {
                    var d1 = tie.Item1;
                    var d2 = tie.Item2;
                    if (d1 == null || d2 == null) continue;

                    int tieId = tie.Item3;
                    double r = (d1.Size / 2) + (tieThickness / 2) + 0.5;

                    double minX = Math.Min(Math.Min(d1.X, d1.OppositeX), Math.Min(d2.X, d2.OppositeX)) - r;
                    double maxX = Math.Max(Math.Max(d1.X, d1.OppositeX), Math.Max(d2.X, d2.OppositeX)) + r;
                    double minY = Math.Min(Math.Min(d1.Y, d1.OppositeY), Math.Min(d2.Y, d2.OppositeY)) - r;
                    double maxY = Math.Max(Math.Max(d1.Y, d1.OppositeY), Math.Max(d2.Y, d2.OppositeY)) + r;

                    string path = "";
                    double hk = 10;
                    double gap = 4; // Độ lồi ra ngoài của móc

                    if (tieId == 2 || tieId == 6)
                    {
                        path = $"M {minX},{minY} L {maxX},{minY} L {maxX},{maxY} L {minX},{maxY} Z";
                        path += $" M {minX},{minY} L {minX + hk},{minY + hk}";
                        path += $" M {minX},{minY} L {minX + hk / 2},{minY + hk + 2}";
                    }
                    else if (tieId == 3)
                    {
                        bool isHorizontal = Math.Abs(d1.X - d2.X) > Math.Abs(d1.Y - d2.Y);
                        if (isHorizontal)
                        {
                            path = $"M {minX + hk},{maxY - 4} L {minX},{maxY} L {minX},{minY} L {minX + hk},{minY + 4}";
                            ties.Add(new RebarLine { PathData = path, Thickness = tieThickness });
                            path = $"M {maxX - hk},{maxY - 4} L {maxX},{maxY} L {maxX},{minY} L {maxX - hk},{minY + 4}";
                            ties.Add(new RebarLine { PathData = path, Thickness = tieThickness });
                            continue;
                        }
                        else
                        {
                            path = $"M {minX + 4},{minY + hk} L {minX},{minY} L {maxX},{minY} L {maxX - 4},{minY + hk}";
                            ties.Add(new RebarLine { PathData = path, Thickness = tieThickness });
                            path = $"M {minX + 4},{maxY - hk} L {minX},{maxY} L {maxX},{maxY} L {maxX - 4},{maxY - hk}";
                            ties.Add(new RebarLine { PathData = path, Thickness = tieThickness });
                            continue;
                        }
                    }
                    else if (tieId == 4)
                    {
                        double midX = (minX + maxX) / 2;
                        path = $"M {midX},{minY} L {maxX},{minY} L {maxX},{maxY} L {minX},{maxY} L {minX},{minY} L {midX},{minY}";
                    }
                    else if (tieId == 5)
                    {
                        path = $"M {minX},{minY} L {maxX},{minY} L {maxX},{maxY} L {minX},{maxY} Z";
                        path += $" M {minX},{minY} L {minX + hk},{minY + hk}"; // 135 vào trong
                        path += $" M {minX},{minY} L {minX - gap},{minY} L {minX - gap},{minY + hk + 5}"; // 90 bẻ xuống
                    }
                    else if (tieId == 7)
                    {
                        path = $"M {minX},{minY} L {maxX},{minY} L {maxX},{maxY} L {minX},{maxY} Z";
                        // Top-Left
                        path += $" M {minX},{minY} L {minX + hk},{minY + hk}"; // 135 In
                        path += $" M {minX},{minY} L {minX - gap},{minY} L {minX - gap},{minY + hk + 5}"; // 90 Out Down
                                                                                                          // Bottom-Right
                        path += $" M {maxX},{maxY} L {maxX - hk},{maxY - hk}"; // 135 In
                        path += $" M {maxX},{maxY} L {maxX + gap},{maxY} L {maxX + gap},{maxY - hk - 5}"; // 90 Out Up
                    }
                    else if (tieId == 8)
                    {
                        // Khung chữ nhật chính
                        path = $"M {minX},{minY} L {maxX},{minY} L {maxX},{maxY} L {minX},{maxY} Z";

                        double gap_out = 4; // Khoảng cách móc 90 văng ra ngoài
                        double gap_in = 5;  // Khoảng cách móc 180 vòng vào trong

                        // --- TOP-LEFT ---
                        // 1. Móc 180 (TRONG): Đâm ngang sang phải ngay tại điểm bắt đầu, rồi cắm thẳng xuống
                        path += $" M {minX},{minY} L {minX + gap_in},{minY} L {minX + gap_in},{minY + hk}";

                        // 2. Móc 90 (NGOÀI): Đâm ngang sang trái, rồi cắm thẳng xuống
                        path += $" M {minX},{minY} L {minX - gap_out},{minY} L {minX - gap_out},{minY + hk + 5}";

                        // --- BOTTOM-RIGHT ---
                        // 1. Móc 180 (TRONG): Đâm ngang sang trái ngay tại điểm bắt đầu, rồi đâm thẳng lên
                        path += $" M {maxX},{maxY} L {maxX - gap_in},{maxY} L {maxX - gap_in},{maxY - hk}";

                        // 2. Móc 90 (NGOÀI): Đâm ngang sang phải, rồi đâm thẳng lên
                        path += $" M {maxX},{maxY} L {maxX + gap_out},{maxY} L {maxX + gap_out},{maxY - hk - 5}";
                    }

                    if (tieId != 3) ties.Add(new RebarLine { PathData = path, Thickness = tieThickness });
                }
            }

            InternalTies = new ObservableCollection<RebarLine>(ties);
        }
        private void RecalculateRebarInfo()
        {
            if (SelectedMainRebar == null) return;

            // --- 1. PHẦN TÍNH TOÁN TEXT (An toàn) ---
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

            // --- 2. PHẦN VẼ LẠI UI (Chỉ chạy khi người dùng thao tác tay, KHÔNG chạy khi đang Load) ---
            if (!_isLoadingData)
            {
                UpdateRebarDiagram();
                RefreshInternalTies();
                GenerateColumnPreview();
            }
        }

        [RelayCommand]
        private void ApplyToOtherFloors()
        {
            // 1. Kiểm tra xem có đang chọn tầng nào không
            if (SelectedColumnPreview == null || ColumnPreviews == null || ColumnPreviews.Count <= 1)
            {
                System.Windows.MessageBox.Show("Vui lòng chọn một tầng để áp dụng cấu hình.");
                return;
            }

            // 2. Ép hệ thống lưu ngay các thông số trên màn hình vào Tầng hiện tại 
            // (đề phòng người dùng vừa gõ số mới xong nhấn Apply luôn mà chưa click ra ngoài)
            SaveCurrentUiStateToModel(SelectedColumnPreview);

            // 3. Duyệt qua tất cả các tầng trong danh sách
            foreach (var item in ColumnPreviews)
            {
                // Bỏ qua tầng đang được chọn (không cần tự copy cho chính mình)
                if (item.ColumnId == SelectedColumnPreview.ColumnId) continue;

                // --- COPY THÔNG SỐ CƠ BẢN ---
                item.SavedCx = SelectedColumnPreview.SavedCx;
                item.SavedCy = SelectedColumnPreview.SavedCy;
                item.SavedSpacingDense = SelectedColumnPreview.SavedSpacingDense;
                item.SavedSpacingSparse = SelectedColumnPreview.SavedSpacingSparse;

                item.SavedMainRebar = SelectedColumnPreview.SavedMainRebar;
                item.SavedStirrupRebar = SelectedColumnPreview.SavedStirrupRebar;
                item.SavedTieRebar = SelectedColumnPreview.SavedTieRebar;
                item.SavedStirrupLayout = SelectedColumnPreview.SavedStirrupLayout;

                // --- COPY ĐAI MÓC (Cực kỳ quan trọng: Phải dùng từ khóa 'new' để tách biệt bộ nhớ) ---
                if (SelectedColumnPreview.SavedDotTies != null)
                {
                    // Tạo một List mới hoàn toàn chép từ List cũ, tránh lỗi sửa Tầng 2 bị dính sang Tầng 1
                    item.SavedDotTies = new List<int>(SelectedColumnPreview.SavedDotTies);
                }
                else
                {
                    item.SavedDotTies = new List<int>();
                }

                // --- COPY ĐAI LỒNG KÍN ---
                if (SelectedColumnPreview.SavedCustomClosedTieIndices != null)
                {
                    item.SavedCustomClosedTieIndices = new List<Tuple<int, int>>(SelectedColumnPreview.SavedCustomClosedTieIndices);
                }
                else
                {
                    item.SavedCustomClosedTieIndices = new List<Tuple<int, int>>();
                }

                // Đánh dấu là tầng này đã có dữ liệu (không cần tự động quét mô hình Revit nữa)
                item.IsDataLoaded = true;

                // --- CẬP NHẬT LẠI DÒNG CHỮ PREVIEW BÊN CỘT TRÁI ---
                int totalRebars = (item.SavedCx * 2) + (item.SavedCy * 2) - 4;
                if (totalRebars < 4) totalRebars = 4;

                string rebarName = item.SavedMainRebar != null ? item.SavedMainRebar.Name : "";
                string layoutName = item.SavedStirrupLayout != null ? item.SavedStirrupLayout.Name : "";

                item.RebarInfoText = $"Thép chủ: {totalRebars} {rebarName}\nKiểu phân bố: {layoutName}";
            }

            // 4. Thông báo cho người dùng biết thao tác đã thành công
            System.Windows.MessageBox.Show("Đã áp dụng cấu hình thép của tầng hiện tại cho tất cả các tầng khác!",
                                           "Thành công",
                                           System.Windows.MessageBoxButton.OK,
                                           System.Windows.MessageBoxImage.Information);
        }

        
        [RelayCommand]
        private void Run(System.Windows.Window window)
        {
            if (SelectedColumns == null || !SelectedColumns.Any())
            {
                System.Windows.MessageBox.Show("Vui lòng chọn cột trước khi chạy!");
                return;
            }

            if (SelectedColumnPreview != null) SaveCurrentUiStateToModel(SelectedColumnPreview);

            System.Text.StringBuilder errorLog = new System.Text.StringBuilder();

            using (Transaction t = new Transaction(_doc, "Vẽ thép cột toàn diện"))
            {
                t.Start();
                var currentDisplayItem = SelectedColumnPreview;

                try
                {
                    string mainHookName = SelectedMainTieHook != null ? SelectedMainTieHook.Name : "135";

                    RebarHookType hook135 = new FilteredElementCollector(_doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>()
                        .FirstOrDefault(h => h.Style == RebarStyle.StirrupTie && ((h.Name != null && h.Name == mainHookName) || (h.Name != null && h.Name.Contains("135")) || Math.Abs(h.HookAngle - 2.356) < 0.01))
                        ?? new FilteredElementCollector(_doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>().FirstOrDefault(h => h.Style == RebarStyle.StirrupTie);

                    if (hook135 == null)
                    {
                        System.Windows.MessageBox.Show("Dự án chưa load Family móc thép đai kiểu StirrupTie!", "Lỗi", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                        t.RollBack(); return;
                    }

                    RebarHookType hook90 = new FilteredElementCollector(_doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>()
                        .FirstOrDefault(h => h.Style == RebarStyle.StirrupTie && ((h.Name != null && h.Name.Contains("90")) || Math.Abs(h.HookAngle - 1.57) < 0.01)) ?? hook135;

                    RebarHookType hook180 = new FilteredElementCollector(_doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>()
                        .FirstOrDefault(h => h.Style == RebarStyle.StirrupTie && ((h.Name != null && h.Name.Contains("180")) || Math.Abs(h.HookAngle - 3.14) < 0.01)) ?? hook135;

                    RebarHookType standardHook135 = new FilteredElementCollector(_doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>()
                        .FirstOrDefault(h => h.Style == RebarStyle.Standard && ((h.Name != null && h.Name.Contains("135")) || Math.Abs(h.HookAngle - 2.356) < 0.01))
                        ?? new FilteredElementCollector(_doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>().FirstOrDefault(h => h.Style == RebarStyle.Standard);

                    var columnsToRun = SelectedColumns.ToList();

                    foreach (var col in columnsToRun)
                    {
                        if (col == null || !col.IsValidObject) continue;

                        try
                        {
                            var previewItem = ColumnPreviews?.FirstOrDefault(p => p.ColumnId == col.Id);
                            if (previewItem != null)
                            {
                                if (!previewItem.IsDataLoaded) ApplyDefaultDataToItem(previewItem);
                                try { LoadDataForSelectedColumn(previewItem); } catch { }
                            }

                            ClearExistingRebars(col);

                            if (SelectedMainRebar == null || SelectedStirrupRebar == null || SelectedTieRebar == null) continue;

                            RebarBarType mainBarType = _doc.GetElement(SelectedMainRebar.Id) as RebarBarType;
                            RebarBarType stirrupBarType = _doc.GetElement(SelectedStirrupRebar.Id) as RebarBarType;
                            RebarBarType tieBarType = _doc.GetElement(SelectedTieRebar.Id) as RebarBarType;

                            if (mainBarType == null || stirrupBarType == null || tieBarType == null) continue;

                            BoundingBoxXYZ bb = col.get_BoundingBox(null);
                            if (bb == null) continue;

                            double.TryParse(TopCover, out double topCoverMm);
                            double.TryParse(OtherCover, out double otherCoverMm);
                            double.TryParse(BeamDepth, out double beamDepthMm);

                            double topCoverFeet = topCoverMm / 304.8;
                            double otherCoverFeet = otherCoverMm / 304.8;
                            double beamDepthFeet = beamDepthMm / 304.8;

                            double minZ = bb.Min.Z + (StirrupOffset / 304.8);
                            double maxZ_BeamBottom = bb.Max.Z - beamDepthFeet;
                            double maxZ_MainRebar = bb.Max.Z - topCoverFeet;

                            double mainBarRadius = mainBarType.BarModelDiameter / 2.0;
                            double stirrupDiameter = stirrupBarType.BarModelDiameter;

                            double offsetRev = otherCoverFeet + stirrupDiameter + mainBarRadius;
                            double spanX_Rev = (bb.Max.X - bb.Min.X) - 2 * offsetRev;
                            double spanY_Rev = (bb.Max.Y - bb.Min.Y) - 2 * offsetRev;

                            if (spanX_Rev < 0.2 || spanY_Rev < 0.2) continue;

                            double dotSizeUI = SelectedMainRebar.DiameterMm * 0.6;
                            if (dotSizeUI < 6) dotSizeUI = 6; if (dotSizeUI > 20) dotSizeUI = 20;
                            double offsetUI = dotSizeUI / 2;
                            double spanX_UI = 240 - 2 * offsetUI;
                            double spanY_UI = 100 - 2 * offsetUI;
                            bool isSwapped = spanY_Rev > spanX_Rev;

                            Func<double, double, XYZ> GetRevitPt = (uiX, uiY) =>
                            {
                                double ratioX = (uiX - offsetUI) / spanX_UI;
                                double ratioY = (uiY - offsetUI) / spanY_UI;
                                double px = !isSwapped ? bb.Min.X + offsetRev + ratioX * spanX_Rev : bb.Min.X + offsetRev + ratioY * spanX_Rev;
                                double py = !isSwapped ? bb.Min.Y + offsetRev + ratioY * spanY_Rev : bb.Min.Y + offsetRev + ratioX * spanY_Rev;
                                return new XYZ(px, py, 0);
                            };

                            var safeRebarDots = RebarDots?.ToList();
                            var safeAdvancedClosedTies = AdvancedClosedTies?.ToList();

                            var drawnMainBars = new HashSet<string>();
                            if (safeRebarDots != null)
                            {
                                foreach (var dot in safeRebarDots)
                                {
                                    if (dot == null) continue;
                                    string key = $"{dot.X:F1}_{dot.Y:F1}";
                                    if (!drawnMainBars.Contains(key))
                                    {
                                        XYZ pt = GetRevitPt(dot.X, dot.Y);
                                        Line mainBarLine = Line.CreateBound(new XYZ(pt.X, pt.Y, minZ), new XYZ(pt.X, pt.Y, maxZ_MainRebar));
                                        Rebar.CreateFromCurves(_doc, RebarStyle.Standard, mainBarType, null, null, col, XYZ.BasisX, new List<Curve> { mainBarLine }, RebarHookOrientation.Right, RebarHookOrientation.Right, true, true);
                                        drawnMainBars.Add(key);
                                    }
                                }
                            }

                            double sDense = 100.0 / 304.8;
                            if (double.TryParse(SpacingDense, out double sd) && sd > 0) sDense = sd / 304.8;
                            double sSparse = 200.0 / 304.8;
                            if (double.TryParse(SpacingSparse, out double ss) && ss > 0) sSparse = ss / 304.8;
                            double clearHeight = maxZ_BeamBottom - minZ;
                            double lOver4 = clearHeight / 4.0;

                            Action<string, List<Curve>, double, double, double, RebarBarType, RebarStyle, RebarHookType, RebarHookType, RebarHookOrientation, RebarHookOrientation> CreateStirrupArray =
                                (itemName, profile, startZ, endZ, spacing, barType, style, startHk, endHk, startOrient, endOrient) =>
                                {
                                    if (barType == null || profile == null || profile.Count == 0 || endZ - startZ < 0.1) return;
                                    List<Curve> shiftedProfile = profile.Select(c => c.CreateTransformed(Transform.CreateTranslation(new XYZ(0, 0, startZ)))).ToList();

                                    RebarHookType sHk = (startHk != null && startHk.Style == style) ? startHk : null;
                                    RebarHookType eHk = (endHk != null && endHk.Style == style) ? endHk : null;
                                    if (style == RebarStyle.StirrupTie && (sHk == null || eHk == null)) style = RebarStyle.Standard;

                                    try
                                    {
                                        Rebar stirrup = Rebar.CreateFromCurves(_doc, style, barType, sHk, eHk, col, XYZ.BasisZ, shiftedProfile, startOrient, endOrient, true, true);
                                        if (stirrup != null) stirrup.GetShapeDrivenAccessor().SetLayoutAsMaximumSpacing(spacing, endZ - startZ, true, true, true);
                                    }
                                    catch (Exception ex1)
                                    {
                                        try
                                        {
                                            Rebar stirrup = Rebar.CreateFromCurves(_doc, RebarStyle.Standard, barType, null, null, col, XYZ.BasisZ, shiftedProfile, RebarHookOrientation.Left, RebarHookOrientation.Left, true, true);
                                            if (stirrup != null) stirrup.GetShapeDrivenAccessor().SetLayoutAsMaximumSpacing(spacing, endZ - startZ, true, true, true);
                                        }
                                        catch (Exception) { errorLog.AppendLine($"[{itemName}] Hình học không hợp lệ: {ex1.Message}"); }
                                    }
                                };

                            Action<string, List<Curve>, RebarBarType, RebarStyle, RebarHookType, RebarHookType, RebarHookOrientation, RebarHookOrientation, bool> ProcessProfileZones =
                                (itemName, profile, barType, style, startHk, endHk, startOrient, endOrient, isAdditionalTie) =>
                                {
                                    if (isAdditionalTie && IsAdditionalStirrupCustom)
                                    {
                                        double customSpc = AdditionalStirrupSpacing / 304.8;
                                        if (customSpc <= 0) customSpc = sDense;
                                        CreateStirrupArray(itemName, profile, minZ, maxZ_BeamBottom, customSpc, barType, style, startHk, endHk, startOrient, endOrient);
                                    }
                                    else
                                    {
                                        string layout = SelectedStirrupLayout?.Name ?? "";
                                        if (layout.Contains("L1, L2, L1"))
                                        {
                                            CreateStirrupArray(itemName, profile, minZ, minZ + lOver4, sDense, barType, style, startHk, endHk, startOrient, endOrient);
                                            CreateStirrupArray(itemName, profile, minZ + lOver4, maxZ_BeamBottom - lOver4, sSparse, barType, style, startHk, endHk, startOrient, endOrient);
                                            CreateStirrupArray(itemName, profile, maxZ_BeamBottom - lOver4, maxZ_BeamBottom, sDense, barType, style, startHk, endHk, startOrient, endOrient);
                                        }
                                        else if (layout == "L1")
                                        {
                                            CreateStirrupArray(itemName, profile, minZ, maxZ_BeamBottom, sSparse, barType, style, startHk, endHk, startOrient, endOrient);
                                        }
                                        else
                                        {
                                            CreateStirrupArray(itemName, profile, minZ, maxZ_BeamBottom - lOver4, sSparse, barType, style, startHk, endHk, startOrient, endOrient);
                                            CreateStirrupArray(itemName, profile, maxZ_BeamBottom - lOver4, maxZ_BeamBottom, sDense, barType, style, startHk, endHk, startOrient, endOrient);
                                        }
                                    }
                                };

                            double tieExt = mainBarRadius + (tieBarType.BarModelDiameter / 2.0);
                            double stirrupExt = mainBarRadius + (stirrupBarType.BarModelDiameter / 2.0);
                            XYZ p1 = new XYZ(bb.Min.X + offsetRev - stirrupExt, bb.Min.Y + offsetRev - stirrupExt, 0);
                            XYZ p2 = new XYZ(bb.Max.X - offsetRev + stirrupExt, bb.Min.Y + offsetRev - stirrupExt, 0);
                            XYZ p3 = new XYZ(bb.Max.X - offsetRev + stirrupExt, bb.Max.Y - offsetRev + stirrupExt, 0);
                            XYZ p4 = new XYZ(bb.Min.X + offsetRev - stirrupExt, bb.Max.Y - offsetRev + stirrupExt, 0);
                            List<Curve> outerProfile = new List<Curve> { Line.CreateBound(p1, p2), Line.CreateBound(p2, p3), Line.CreateBound(p3, p4), Line.CreateBound(p4, p1) };

                            ProcessProfileZones("Đai chính", outerProfile, stirrupBarType, RebarStyle.StirrupTie, hook135, hook135, RebarHookOrientation.Left, RebarHookOrientation.Left, false);

                            var drawnHooks = new HashSet<string>();
                            if (safeRebarDots != null)
                            {
                                foreach (var dot in safeRebarDots)
                                {
                                    if (dot == null) continue;
                                    if (dot.TieType == 1)
                                    {
                                        string key = $"{Math.Min(dot.X, dot.OppositeX):F1}_{Math.Min(dot.Y, dot.OppositeY):F1}";
                                        if (!drawnHooks.Contains(key))
                                        {
                                            XYZ pt1_s = GetRevitPt(dot.X, dot.Y);
                                            XYZ pt2_s = GetRevitPt(dot.OppositeX, dot.OppositeY);

                                            if (pt1_s.DistanceTo(pt2_s) < 0.1) continue;

                                            XYZ dir = (pt2_s - pt1_s).Normalize();
                                            XYZ perpLeft = XYZ.BasisZ.CrossProduct(dir).Normalize();

                                            XYZ extPt1 = pt1_s + perpLeft * tieExt - dir * mainBarRadius;
                                            XYZ extPt2 = pt2_s + perpLeft * tieExt + dir * mainBarRadius;

                                            List<Curve> hookProfile = new List<Curve> { Line.CreateBound(extPt1, extPt2) };
                                            ProcessProfileZones("Item 1", hookProfile, tieBarType, RebarStyle.Standard, standardHook135, standardHook135, RebarHookOrientation.Right, RebarHookOrientation.Right, true);
                                            drawnHooks.Add(key);
                                        }
                                    }
                                }
                            }

                            if (safeAdvancedClosedTies != null)
                            {
                                foreach (var tie in safeAdvancedClosedTies)
                                {
                                    if (tie == null || tie.Item1 == null || tie.Item2 == null) continue;

                                    XYZ ptA = GetRevitPt(tie.Item1.X, tie.Item1.Y);
                                    XYZ ptA_opp = GetRevitPt(tie.Item1.OppositeX, tie.Item1.OppositeY);
                                    XYZ ptB = GetRevitPt(tie.Item2.X, tie.Item2.Y);
                                    XYZ ptB_opp = GetRevitPt(tie.Item2.OppositeX, tie.Item2.OppositeY);

                                    int tieId = tie.Item3;

                                    double minX = Math.Min(Math.Min(ptA.X, ptA_opp.X), Math.Min(ptB.X, ptB_opp.X)) - tieExt;
                                    double maxX = Math.Max(Math.Max(ptA.X, ptA_opp.X), Math.Max(ptB.X, ptB_opp.X)) + tieExt;
                                    double minY = Math.Min(Math.Min(ptA.Y, ptA_opp.Y), Math.Min(ptB.Y, ptB_opp.Y)) - tieExt;
                                    double maxY = Math.Max(Math.Max(ptA.Y, ptA_opp.Y), Math.Max(ptB.Y, ptB_opp.Y)) + tieExt;

                                    if (maxX - minX < 0.1 || maxY - minY < 0.1) continue;

                                    XYZ c1 = new XYZ(minX, minY, 0);
                                    XYZ c2 = new XYZ(maxX, minY, 0);
                                    XYZ c3 = new XYZ(maxX, maxY, 0);
                                    XYZ c4 = new XYZ(minX, maxY, 0);

                                    List<Curve> rectProfile = new List<Curve> { Line.CreateBound(c1, c2), Line.CreateBound(c2, c3), Line.CreateBound(c3, c4), Line.CreateBound(c4, c1) };

                                    if (tieId == 2)
                                    {
                                        ProcessProfileZones("Item 2", rectProfile, tieBarType, RebarStyle.StirrupTie, hook135, hook135, RebarHookOrientation.Left, RebarHookOrientation.Left, true);
                                    }
                                    else if (tieId == 3)
                                    {
                                        bool isHorizontal = Math.Abs(ptA.X - ptB.X) > Math.Abs(ptA.Y - ptB.Y);
                                        if (isHorizontal)
                                        {
                                            // Vẽ cLeft từ Trên (c4) xuống Dưới (c1) -> Móc Right bẻ vào trong
                                            List<Curve> cLeft = new List<Curve> { Line.CreateBound(c4, c1) };

                                            // SỬA LỖI: Vẽ cRight ngược lại từ Dưới (c2) lên Trên (c3) -> Móc Right bẻ vào trong
                                            List<Curve> cRight = new List<Curve> { Line.CreateBound(c2, c3) };

                                            ProcessProfileZones("Item 3 Trái", cLeft, tieBarType, RebarStyle.Standard, standardHook135, standardHook135, RebarHookOrientation.Right, RebarHookOrientation.Right, true);
                                            ProcessProfileZones("Item 3 Phải", cRight, tieBarType, RebarStyle.Standard, standardHook135, standardHook135, RebarHookOrientation.Right, RebarHookOrientation.Right, true);
                                        }
                                        else
                                        {
                                            // SỬA LỖI: Vẽ cTop từ Trái (c4) sang Phải (c3) -> Móc Right bẻ vào trong
                                            List<Curve> cTop = new List<Curve> { Line.CreateBound(c4, c3) };

                                            // Vẽ cBottom từ Phải (c2) sang Trái (c1) -> Móc Right bẻ vào trong
                                            List<Curve> cBottom = new List<Curve> { Line.CreateBound(c2, c1) };

                                            ProcessProfileZones("Item 3 Trên", cTop, tieBarType, RebarStyle.Standard, standardHook135, standardHook135, RebarHookOrientation.Right, RebarHookOrientation.Right, true);
                                            ProcessProfileZones("Item 3 Dưới", cBottom, tieBarType, RebarStyle.Standard, standardHook135, standardHook135, RebarHookOrientation.Right, RebarHookOrientation.Right, true);
                                        }
                                    }
                                    else if (tieId == 4)
                                    {
                                        XYZ mTop = (c4 + c3) / 2;
                                        List<Curve> profileMid = new List<Curve> { Line.CreateBound(mTop, c4), Line.CreateBound(c4, c1), Line.CreateBound(c1, c2), Line.CreateBound(c2, c3), Line.CreateBound(c3, mTop) };
                                        ProcessProfileZones("Item 4", profileMid, tieBarType, RebarStyle.Standard, null, null, RebarHookOrientation.Left, RebarHookOrientation.Left, true);
                                    }
                                    else if (tieId == 5)
                                    {
                                        // SỬA LỖI: Đổi Right thành Left để móc 90 đâm vào trong lõi cột
                                        ProcessProfileZones("Item 5", rectProfile, tieBarType, RebarStyle.StirrupTie, hook135, hook90, RebarHookOrientation.Left, RebarHookOrientation.Left, true);
                                    }
                                    else if (tieId == 6)
                                    {
                                        bool isHorizontal = Math.Abs(ptA.X - ptB.X) > Math.Abs(ptA.Y - ptB.Y);
                                        List<Curve> profileMid;
                                        if (isHorizontal)
                                        {
                                            XYZ mTop = (c4 + c3) / 2;
                                            profileMid = new List<Curve> { Line.CreateBound(mTop, c4), Line.CreateBound(c4, c1), Line.CreateBound(c1, c2), Line.CreateBound(c2, c3), Line.CreateBound(c3, mTop) };
                                        }
                                        else
                                        {
                                            XYZ mRight = (c3 + c2) / 2;
                                            profileMid = new List<Curve> { Line.CreateBound(mRight, c3), Line.CreateBound(c3, c4), Line.CreateBound(c4, c1), Line.CreateBound(c1, c2), Line.CreateBound(c2, mRight) };
                                        }
                                        ProcessProfileZones("Item 6", profileMid, tieBarType, RebarStyle.StirrupTie, hook135, hook135, RebarHookOrientation.Left, RebarHookOrientation.Left, true);
                                    }
                                    else if (tieId == 7)
                                    {
                                        List<Curve> rectReverse = new List<Curve> { Line.CreateBound(c3, c2), Line.CreateBound(c2, c1), Line.CreateBound(c1, c4), Line.CreateBound(c4, c3) };
                                        // SỬA LỖI: Đổi Right thành Left cho móc 90
                                        ProcessProfileZones("Item 7", rectProfile, tieBarType, RebarStyle.StirrupTie, hook135, hook90, RebarHookOrientation.Left, RebarHookOrientation.Left, true);
                                        ProcessProfileZones("Item 7 Đảo", rectReverse, tieBarType, RebarStyle.StirrupTie, hook135, hook90, RebarHookOrientation.Left, RebarHookOrientation.Left, true);
                                    }
                                    else if (tieId == 8)
                                    {
                                        List<Curve> rectReverse = new List<Curve> { Line.CreateBound(c3, c2), Line.CreateBound(c2, c1), Line.CreateBound(c1, c4), Line.CreateBound(c4, c3) };
                                        // SỬA LỖI: Đổi Right thành Left cho móc 90
                                        ProcessProfileZones("Item 8", rectProfile, tieBarType, RebarStyle.StirrupTie, hook180, hook90, RebarHookOrientation.Left, RebarHookOrientation.Left, true);
                                        ProcessProfileZones("Item 8 Đảo", rectReverse, tieBarType, RebarStyle.StirrupTie, hook180, hook90, RebarHookOrientation.Left, RebarHookOrientation.Left, true);
                                    }
                                }
                            }
                        }
                        catch (Exception colEx)
                        {
                            errorLog.AppendLine($"Lỗi ngoại lệ tại cột {col.Id}:\n{colEx.Message}");
                        }
                    }

                    t.Commit();
                    if (window != null) window.Close();

                    if (errorLog.Length > 0)
                    {
                        System.Windows.MessageBox.Show("Cảnh báo:\n" + errorLog.ToString(), "Báo cáo", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex)
                {
                    t.RollBack();
                    System.Windows.MessageBox.Show("Lỗi hệ thống: " + ex.Message);
                }
                finally
                {
                    if (currentDisplayItem != null)
                    {
                        try { LoadDataForSelectedColumn(currentDisplayItem); } catch { }
                        // ĐÃ XÓA SẠCH LỆNH ÉP CẬP NHẬT UI ĐỂ CHỐNG SẬP
                    }
                }
            }
        }
        private void SaveCurrentUiStateToModel(ColumnPreviewItem item)
        {
            // Bỏ qua nếu item rỗng hoặc đang trong quá trình gán dữ liệu tự động
            if (item == null || _isLoadingData) return;

            // 1. Lưu các thông số cơ bản (bạn giữ nguyên các biến bạn đang có)
            item.SavedCx = this.Cx;
            item.SavedCy = this.Cy;
            item.SavedSpacingDense = this.SpacingDense;
            item.SavedSpacingSparse = this.SpacingSparse;

            item.SavedMainRebar = this.SelectedMainRebar;
            item.SavedStirrupRebar = this.SelectedStirrupRebar;
            item.SavedTieRebar = this.SelectedTieRebar;
            item.SavedStirrupLayout = this.SelectedStirrupLayout;

            // ==========================================================
            // 2. LƯU TRẠNG THÁI ĐAI MÓC CỦA TỪNG CHẤM ĐỎ TẠI ĐÂY
            // ==========================================================
            if (item.SavedDotTies == null) item.SavedDotTies = new List<int>();
            item.SavedDotTies.Clear(); // Xóa data cũ của tầng này
            foreach (var dot in RebarDots)
            {
                // Lưu lại giá trị đai móc (0 hoặc 1) của từng chấm
                item.SavedDotTies.Add(dot.TieType);
            }

            // ==========================================================
            // 3. LƯU TRẠNG THÁI ĐAI LỒNG KÍN (Dựa vào vị trí Index)
            // ==========================================================
            if (item.SavedCustomClosedTieIndices == null) item.SavedCustomClosedTieIndices = new List<Tuple<int, int>>();
            item.SavedCustomClosedTieIndices.Clear();

            if (_customClosedTies != null)
            {
                foreach (var tie in _customClosedTies)
                {
                    int index1 = RebarDots.IndexOf(tie.Item1);
                    int index2 = RebarDots.IndexOf(tie.Item2);

                    if (index1 >= 0 && index2 >= 0)
                    {
                        item.SavedCustomClosedTieIndices.Add(new Tuple<int, int>(index1, index2));
                    }
                }
            }

            item.IsDataLoaded = true;
        }

        // === HÀM TẢI DỮ LIỆU TỪ TẦNG MỚI LÊN GIAO DIỆN ===
        private void LoadDataForSelectedColumn(ColumnPreviewItem item)
        {
            if (item == null) return;

            // 1. BẬT CỜ KHÓA: Báo cho ViewModel biết đang thao tác gán dữ liệu, đừng tự động tính toán hay lưu đè
            _isLoadingData = true;

            // 2. XỬ LÝ DỮ LIỆU NGUỒN
            if (!item.IsDataLoaded)
            {
                // Nếu click lần đầu tiên -> Quét Revit. 
                ExtractExistingRebarFromRevit(item);
                item.IsDataLoaded = true;
            }

            // 3. ĐỔ DỮ LIỆU TỪ ITEM RA GIAO DIỆN 
            this.Cx = item.SavedCx > 0 ? item.SavedCx : 2;
            this.Cy = item.SavedCy > 0 ? item.SavedCy : 2;
            this.SpacingDense = !string.IsNullOrEmpty(item.SavedSpacingDense) ? item.SavedSpacingDense : "100";
            this.SpacingSparse = !string.IsNullOrEmpty(item.SavedSpacingSparse) ? item.SavedSpacingSparse : "200";

            if (item.SavedMainRebar != null) this.SelectedMainRebar = item.SavedMainRebar;
            if (item.SavedStirrupRebar != null) this.SelectedStirrupRebar = item.SavedStirrupRebar;
            if (item.SavedTieRebar != null) this.SelectedTieRebar = item.SavedTieRebar;
            if (item.SavedStirrupLayout != null) this.SelectedStirrupLayout = item.SavedStirrupLayout;

            // --- PHỤC HỒI HÌNH VẼ ---
            UpdateRebarDiagram();

            // A. Phục hồi Đai móc (TieType cho từng chấm đỏ)
            if (item.SavedDotTies != null)
            {
                for (int i = 0; i < RebarDots.Count && i < item.SavedDotTies.Count; i++)
                {
                    RebarDots[i].TieType = item.SavedDotTies[i]; // Giao diện tự động vẽ nhờ OnPropertyChanged
                }
            }

            // B. Phục hồi Đai lồng kín (Dựa vào Index đã lưu) -> BẠN ĐANG THIẾU ĐOẠN NÀY
            _customClosedTies = new List<Tuple<RebarDot, RebarDot>>();
            if (item.SavedCustomClosedTieIndices != null)
            {
                foreach (var indices in item.SavedCustomClosedTieIndices)
                {
                    if (indices.Item1 < RebarDots.Count && indices.Item2 < RebarDots.Count)
                    {
                        var dot1 = RebarDots[indices.Item1];
                        var dot2 = RebarDots[indices.Item2];
                        _customClosedTies.Add(new Tuple<RebarDot, RebarDot>(dot1, dot2));
                    }
                }
            }

            RefreshInternalTies();
            RecalculateRebarInfo();
            GenerateColumnPreview();
            _isLoadingData = false;
        }
        // Hàm copy cấu hình qua lại cho những tầng bị bỏ qua (chưa click vào xem)
        private void ApplyDefaultDataToItem(ColumnPreviewItem item)
        {
            item.SavedCx = this.Cx;
            item.SavedCy = this.Cy;
            item.SavedSpacingDense = this.SpacingDense;
            item.SavedSpacingSparse = this.SpacingSparse;
            item.SavedMainRebar = this.SelectedMainRebar;
            item.SavedStirrupRebar = this.SelectedStirrupRebar;
            item.SavedTieRebar = this.SelectedTieRebar;
            item.SavedStirrupLayout = this.SelectedStirrupLayout;
            item.IsDataLoaded = true;
        }

        // Hàm Xóa sạch thép cũ của cột để Revit không sinh ra thanh thép trùng
        private void ClearExistingRebars(FamilyInstance column)
        {
            if (column == null || !column.IsValidObject) return;
            try
            {
                RebarHostData rebarHostData = RebarHostData.GetRebarHostData(column);
                if (rebarHostData == null) return;

                var rebars = rebarHostData.GetRebarsInHost();
                if (rebars != null && rebars.Count > 0)
                {
                    // Ép sang ToList() để không bị lỗi CollectionModified khi xóa
                    foreach (var r in rebars.ToList())
                    {
                        if (r != null && r.IsValidObject)
                        {
                            _doc.Delete(r.Id);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Nuốt lỗi an toàn nếu Revit từ chối xóa một thanh thép bị khóa
            }
        }
    }
}