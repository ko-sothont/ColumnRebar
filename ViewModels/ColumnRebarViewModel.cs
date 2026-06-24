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
        private ObservableCollection<MainTieType> _availableMainTieTypes;
        public ObservableCollection<MainTieType> AvailableMainTieTypes
        {
            get { return _availableMainTieTypes; }
            set { _availableMainTieTypes = value; OnPropertyChanged(nameof(AvailableMainTieTypes)); }
        }

        private MainTieType _selectedMainTieType;
        public MainTieType SelectedMainTieType
        {
            get { return _selectedMainTieType; }
            set { _selectedMainTieType = value; OnPropertyChanged(nameof(SelectedMainTieType));
            RefreshInternalTies();
            }
        }
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
        [RelayCommand]
        private void Close(System.Windows.Window window)
        {
            // Kiểm tra xem window có tồn tại không rồi đóng nó lại
            if (window != null)
            {
                window.Close();
            }
        }
        partial void OnSelectedStirrupLayoutChanged(StirrupLayoutOption value)
        {
            // 1. NẾU ĐANG LOAD DỮ LIỆU CHUYỂN TẦNG -> PHANH LẠI NGAY! (Không tự ý vẽ)
            if (_isLoadingData) return;

            // 2. Cập nhật hình vẽ mặt cắt cột (bên phải)
            UpdateRebarDiagram();

            // 3. Ép giao diện vẽ lại ngay các sọc đai cho đúng cấu hình vừa chọn (bên trái)
            if (SelectedColumnPreview != null)
            {
                UpdateStirrupGraphic(SelectedColumnPreview, value?.Name ?? "");
            }

            // 4. Chạy hàm này để nó cập nhật các đoạn Text ghi chú
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
        private void UpdateStirrupGraphic(ColumnPreviewItem item, string layoutName)
        {
            if (item == null) return;

            double lOver4 = (item.UIColumnHeight - item.UIBeamDepth) / 4;
            var stirrups = new List<double>();

            // Vẽ đai trong vùng Dầm
            for (double y = 2; y <= item.UIBeamDepth; y += 4) stirrups.Add(y);

            if (layoutName.Contains("L1, L2, L1"))
            {
                for (double y = item.UIBeamDepth; y <= item.UIBeamDepth + lOver4; y += 4) stirrups.Add(y);
                for (double y = item.UIBeamDepth + lOver4 + 10; y <= item.UIColumnHeight - lOver4; y += 10) stirrups.Add(y);
                for (double y = item.UIColumnHeight - lOver4 + 4; y <= item.UIColumnHeight - 2; y += 4) stirrups.Add(y);
            }
            else if (layoutName == "L1")
            {
                for (double y = item.UIBeamDepth; y <= item.UIColumnHeight - 2; y += 6) stirrups.Add(y);
            }
            else if (layoutName.Contains("L1, L2"))
            {
                for (double y = item.UIBeamDepth; y <= item.UIColumnHeight - lOver4; y += 10) stirrups.Add(y);
                for (double y = item.UIColumnHeight - lOver4 + 4; y <= item.UIColumnHeight - 2; y += 4) stirrups.Add(y);
            }

            // === SỬA Ở ĐÂY: Dùng Clear() và Add() để WPF tự động nhận diện thay đổi ngay lập tức ===
            if (item.StirrupPositions == null) item.StirrupPositions = new ObservableCollection<double>();
            item.StirrupPositions.Clear();
            foreach (var y in stirrups)
            {
                item.StirrupPositions.Add(y);
            }

            item.RebarInfoText = $"Thép chủ: {TotalRebarText}\nKiểu phân bố: {layoutName}";
        }
        private void GenerateColumnPreview()
        {
            if (SelectedColumns == null || SelectedColumns.Count == 0) return;

            // Nếu danh sách đã có rồi, ta KHÔNG tạo lại từ đầu nữa, mà chỉ Cập nhật hình vẽ cho Tầng đang chọn thôi
            if (ColumnPreviews != null && ColumnPreviews.Count > 0)
            {
                if (SelectedColumnPreview != null)
                {
                    string currentLayout = SelectedStirrupLayout?.Name ?? "";
                    UpdateStirrupGraphic(SelectedColumnPreview, currentLayout);
                }
                return;
            }

            // KHỞI TẠO LẦN ĐẦU TIÊN
            var previews = new List<ColumnPreviewItem>();
            string defaultLayoutName = SelectedStirrupLayout?.Name ?? "";
            var sortedColumns = SelectedColumns.OrderByDescending(c => c.get_BoundingBox(null).Min.Z).ToList();

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

                var newItem = new ColumnPreviewItem
                {
                    ColumnId = col.Id,
                    LevelName = levelName,
                    DimensionText = $"Height = {Math.Round(actualHeightMm)} (mm)\nBxH = {ColumnWidth}x{ColumnHeight}\nMark: {col.LookupParameter("Mark")?.AsString() ?? "Unknown"}",
                    UIColumnHeight = uiHeight,
                    UIColumnWidth = uiWidth,
                    UIBeamDepth = uiBeamDepth
                };

                // Gọi hàm vẽ ngay lúc khởi tạo
                UpdateStirrupGraphic(newItem, defaultLayoutName);
                previews.Add(newItem);
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

            // --- THÊM LIST ĐAI CHÍNH VÀO ĐÂY (NẰM TRONG HÀM) ---
            AvailableMainTieTypes = new ObservableCollection<MainTieType>
    {
        new MainTieType { Id = 1, Name = "1. Kín (Móc 135x135)" },
        new MainTieType { Id = 2, Name = "2. Nối chồng (Lap Splice 30d)" },
        new MainTieType { Id = 3, Name = "3. Chạm 1 điểm (Hàn giáp mối)" },
        new MainTieType { Id = 4, Name = "4. Kín (Móc 135x90)" },
        new MainTieType { Id = 5, Name = "5. Ghép góc (Móc 90x90 + 15d)" },
        new MainTieType { Id = 6, Name = "6. Ghép góc (Móc 135x135 + 15d)" }
    };
            SelectedMainTieType = AvailableMainTieTypes.FirstOrDefault();
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
            if (RebarDots != null && RebarDots.Any() && SelectedMainTieType != null)
            {
                // LẤY SỐ ID CỦA ĐAI CHÍNH (Nếu class của bạn dùng tên khác như .Value hoặc .Type thì đổi chữ .Id nhé)
                int mainTieId = SelectedMainTieType.Id;

                // Tính bán kính bo để ôm vừa ngoài cây thép chủ
                double r = (RebarDots.First().Size / 2) + (tieThickness / 2) + 0.5;

                // Tìm khung chữ nhật lớn nhất bao quanh TOÀN BỘ các cây thép
                double minX = RebarDots.Min(d => Math.Min(d.X, d.OppositeX)) - r;
                double maxX = RebarDots.Max(d => Math.Max(d.X, d.OppositeX)) + r;
                double minY = RebarDots.Min(d => Math.Min(d.Y, d.OppositeY)) - r;
                double maxY = RebarDots.Max(d => Math.Max(d.Y, d.OppositeY)) + r;

                string mainPath = "";
                double hk = 12;  // Chiều dài râu vươn ra (15d)
                double gap = 3;  // Khoảng hở tàng hình để nhìn rõ 2 thanh gác đè nhau trên 2D

                if (mainTieId == 1)
                {
                    // ITEM 1, 3: Đai thường khép kín
                    mainPath = $"M {minX},{minY} L {maxX},{minY} L {maxX},{maxY} L {minX},{maxY} Z";
                    mainPath += $" M {minX},{minY} L {minX + hk},{minY + hk}";
                    mainPath += $" M {minX},{minY} L {minX + hk / 2},{minY + hk + 2}";
                }
                else if (mainTieId == 2)
                {
                    // ITEM 2: Đai nối chồng (Lap Splice) - Nối đè nhau rõ ràng ở giữa CẠNH TRÁI
                    double midY = (minY + maxY) / 2; // Tìm điểm chính giữa cạnh trái
                    double lapHalf = 25; // Nửa chiều dài đoạn nối đè lên nhau
                    double lapGap = 4;      // Khoảng hở tàng hình để nhìn rõ 2 nét đè song song

                    // Vẽ 1 nét liền mạch chuẩn nét đỏ của bạn:
                    // 1. Lớp ngoài (sát lề trái): Bắt đầu từ dưới (midY + lapHalf) đâm thẳng lên góc Top-Left
                    mainPath = $"M {minX},{midY + lapHalf} L {minX},{minY}";

                    // 2. Chạy vòng quanh cột: Top-Left -> Top-Right -> Bottom-Right -> Bottom-Left (hơi thụt vào tạo lớp trong)
                    mainPath += $" L {maxX},{minY} L {maxX},{maxY} L {minX + lapGap},{maxY}";

                    // 3. Lớp trong (thụt vào 'gap'): Từ góc Bottom-Left đâm ngược lên trên (midY - lapHalf) để gác đè lên lớp ngoài
                    mainPath += $" L {minX + lapGap},{midY - lapHalf}";
                }
                else if (mainTieId == 3)
                {
                    // ITEM 3: Khép kín chạm 1 điểm - Vạch đánh dấu cực ngắn & Màu đen
                    double midX = (minX + maxX) / 2;

                    // 1. Vẽ vòng chữ nhật nét đơn (Sẽ tự được Add vào list ở cuối hàm với màu xanh)
                    mainPath = $"M {minX},{minY} L {maxX},{minY} L {maxX},{maxY} L {minX},{maxY} Z";

                    // 2. Kẻ 1 vạch ngắn xíu (Độ dài nhô ra chỉ bằng ~0.8 lần bề dày nét vẽ)
                    double tick = tieThickness * 0.8;
                    string tickPath = $"M {midX},{minY - tick} L {midX},{minY + tick}";

                    // 3. Tách riêng vạch kẻ thành 1 RebarLine độc lập để làm MÀU ĐEN
                    // (Nếu class RebarLine của bạn chưa có thuộc tính Color/Stroke, bạn hãy tạm thời dùng màu mặc định 
                    // hoặc thêm property `public string StrokeColor {get; set;}` vào class RebarLine nhé!)
                    ties.Add(new RebarLine
                    {
                        PathData = tickPath,
                        Thickness = tieThickness + 1 // Cho vạch đánh dấu dày hơn 1 chút cho rõ
                        /*, StrokeColor = "Black" */ // Mở comment này ra nếu class của bạn hỗ trợ đổi màu
                    });
                }
                else if (mainTieId == 4)
                {
                    // ITEM 4: Đai khép kín - 1 móc 135 độ và 1 móc 90 độ tại góc Top-Left
                    double hookLen = 15; // Chiều dài của râu móc
                    double outGap = 4;   // Khoảng vươn ra ngoài của móc 90 độ

                    // 1. Khung chữ nhật chính ôm ngoài cùng
                    mainPath = $"M {minX},{minY} L {maxX},{minY} L {maxX},{maxY} L {minX},{maxY} Z";

                    // 2. Râu móc 135 độ: Xích xuống dưới một chút, bẻ chéo 135 độ đâm thẳng vào tâm cột
                    mainPath += $" M {minX},{minY + gap} L {minX + hookLen},{minY + hookLen + gap}";

                    // 3. Râu móc 90 độ: Xích sang phải, đâm ngang lố ra ngoài góc (outGap) rồi bẻ gập 90 độ cắm thẳng xuống
                    mainPath += $" M {minX + gap},{minY} L {minX - outGap},{minY} L {minX - outGap},{minY + hookLen}";
                }
                else if (mainTieId == 5)
                {
                    // ITEM 5: Vươn dài rõ ràng như nét vẽ đỏ (Móc 90 độ)
                    double ext = 45;  // Đoạn vươn thẳng 15d (kéo dài hẳn ra)
                    double tail = 15; // Đoạn bẻ quặp 90 độ

                    // Khung chữ nhật chính ôm ngoài cùng
                    mainPath = $"M {minX},{minY} L {maxX},{minY} L {maxX},{maxY} L {minX},{maxY} Z";

                    // Râu 1 (Phương ngang): Nằm thụt xuống 'gap', vươn thẳng sang PHẢI, bẻ 90 độ đâm XUỐNG
                    mainPath += $" M {minX},{minY + gap} L {minX + ext},{minY + gap} L {minX + ext},{minY + gap + tail}";

                    // Râu 2 (Phương dọc): Nằm thụt sang 'gap', vươn thẳng XUỐNG DƯỚI, bẻ 90 độ đâm sang PHẢI
                    mainPath += $" M {minX + gap},{minY} L {minX + gap},{minY + ext} L {minX + gap + tail},{minY + ext}";
                }
                else if (mainTieId == 6)
                {
                    // ITEM 6: Vươn dài rõ ràng như nét vẽ đỏ (Móc 135 độ)
                    double ext = 45;
                    double tail = 15;

                    // Khung chữ nhật chính ôm ngoài cùng
                    mainPath = $"M {minX},{minY} L {maxX},{minY} L {maxX},{maxY} L {minX},{maxY} Z";

                    // Râu 1 (Phương ngang): Nằm thụt xuống 'gap', vươn thẳng sang PHẢI, bẻ 135 độ QUẶP VÀO TRONG
                    mainPath += $" M {minX},{minY + gap} L {minX + ext},{minY + gap} L {minX + ext - tail},{minY + gap + tail}";

                    // Râu 2 (Phương dọc): Nằm thụt sang 'gap', vươn thẳng XUỐNG DƯỚI, bẻ 135 độ QUẶP VÀO TRONG
                    mainPath += $" M {minX + gap},{minY} L {minX + gap},{minY + ext} L {minX + gap + tail},{minY + ext - tail}";
                }
                if (!string.IsNullOrEmpty(mainPath))
                {
                    // Thêm đai chính vào Canvas
                    ties.Add(new RebarLine { PathData = mainPath, Thickness = tieThickness + 0.5 });
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

                    if (tieId == 2)
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
                    else if (tieId == 6)
                    {
                        bool isHorizontal = Math.Abs(d1.X - d2.X) > Math.Abs(d1.Y - d2.Y);
                        if (isHorizontal)
                        {
                            // Click ngang -> Hình chữ U mở ở cạnh TRÊN
                            // Đi từ Top-Left -> Bottom-Left -> Bottom-Right -> Top-Right
                            path = $"M {minX},{minY} L {minX},{maxY} L {maxX},{maxY} L {maxX},{minY}";

                            // Thêm 2 móc 135 độ quặp vào trong
                            path += $" M {minX},{minY} L {minX + hk},{minY + hk}";
                            path += $" M {maxX},{minY} L {maxX - hk},{minY + hk}";
                        }
                        else
                        {
                            // Click dọc -> Hình chữ U mở ở cạnh PHẢI
                            // Đi từ Top-Right -> Top-Left -> Bottom-Left -> Bottom-Right
                            path = $"M {maxX},{minY} L {minX},{minY} L {minX},{maxY} L {maxX},{maxY}";

                            // Thêm 2 móc 135 độ quặp vào trong
                            path += $" M {maxX},{minY} L {maxX - hk},{minY + hk}";
                            path += $" M {maxX},{maxY} L {maxX - hk},{maxY - hk}";
                        }
                    }
                    else if (tieId == 7)
                    {
                        // --- THANH L1 (Cạnh Trên và Cạnh Phải) ---
                        path = $"M {minX},{minY} L {maxX},{minY} L {maxX},{maxY}";
                        // Đầu 90 độ tại Top-Left (Văng ra ngoài đâm xuống)
                        path += $" M {minX},{minY} L {minX - gap},{minY} L {minX - gap},{minY + hk + 5}";
                        // Đầu 135 độ tại Bottom-Right (Bẻ quặp vào trong)
                        path += $" M {maxX},{maxY} L {maxX - hk},{maxY - hk}";

                        // --- THANH L2 (Cạnh Dưới và Cạnh Trái) ---
                        path += $" M {maxX},{maxY} L {minX},{maxY} L {minX},{minY}";
                        // Đầu 90 độ tại Bottom-Right (Văng ra ngoài đâm lên)
                        path += $" M {maxX},{maxY} L {maxX + gap},{maxY} L {maxX + gap},{maxY - hk - 5}";
                        // Đầu 135 độ tại Top-Left (Bẻ quặp vào trong, khóa chung với móc 90 của L1)
                        path += $" M {minX},{minY} L {minX + hk},{minY + hk}";
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
                item.SavedMainTieType = SelectedColumnPreview.SavedMainTieType;

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
                    // SỬA Ở ĐÂY: Nâng cấp Tuple<int, int> thành Tuple<int, int, int>
                    item.SavedCustomClosedTieIndices = new List<Tuple<int, int, int>>(SelectedColumnPreview.SavedCustomClosedTieIndices);
                }
                else
                {
                    // VÀ SỬA Ở ĐÂY NỮA
                    item.SavedCustomClosedTieIndices = new List<Tuple<int, int, int>>();
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
                    RebarHookType standardHook90 = new FilteredElementCollector(_doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>()
                        .FirstOrDefault(h => h.Style == RebarStyle.Standard && (Math.Abs(h.HookAngle - Math.PI / 2) < 0.05 || (h.Name != null && h.Name.Contains("90"))));
                    var columnsToRun = SelectedColumns.ToList();
                    RebarHookType standardHook180 = new FilteredElementCollector(_doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>()
                .FirstOrDefault(h => h.Style == RebarStyle.Standard && (Math.Abs(h.HookAngle - Math.PI) < 0.05 || (h.Name != null && h.Name.Contains("180"))))
                ?? standardHook135;

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
                            // Tọa độ 4 góc của Đai chính
                            // p1 (Bottom-Left), p2 (Bottom-Right), p3 (Top-Right), p4 (Top-Left)
                            XYZ p1 = new XYZ(bb.Min.X + offsetRev - stirrupExt, bb.Min.Y + offsetRev - stirrupExt, 0);
                            XYZ p2 = new XYZ(bb.Max.X - offsetRev + stirrupExt, bb.Min.Y + offsetRev - stirrupExt, 0);
                            XYZ p3 = new XYZ(bb.Max.X - offsetRev + stirrupExt, bb.Max.Y - offsetRev + stirrupExt, 0);
                            XYZ p4 = new XYZ(bb.Min.X + offsetRev - stirrupExt, bb.Max.Y - offsetRev + stirrupExt, 0);

                            // --- LOGIC 6 LOẠI ĐAI CHÍNH ---
                            int mainTieItem = SelectedMainTieType != null ? SelectedMainTieType.Id : 1;

                            List<Curve> mainProfile = new List<Curve>();
                            RebarStyle mainStyle = RebarStyle.StirrupTie;
                            RebarHookType mainStartHook = null;
                            RebarHookType mainEndHook = null;

                            double dMain_mm = stirrupBarType.BarNominalDiameter * 304.8;

                            // Lấy chiều dài 2 cạnh của đai để làm mốc chặn an toàn
                            double faceWidth = p2.X - p1.X;
                            double faceHeight = p4.Y - p1.Y;

                            if (mainTieItem == 1)
                            {
                                // ITEM 1: Đai chữ nhật khép kín, 2 móc 135 độ
                                mainProfile = new List<Curve> { Line.CreateBound(p1, p2), Line.CreateBound(p2, p3), Line.CreateBound(p3, p4), Line.CreateBound(p4, p1) };
                                mainStyle = RebarStyle.StirrupTie;
                                mainStartHook = hook135;
                                mainEndHook = hook135;
                            }
                            else if (mainTieItem == 2)
                            {
                                // ITEM 2: BẢN TINH GỌN TỐI ĐA - 1 THANH DUY NHẤT, PHẲNG 100%
                                // Thuận theo luật của Revit để không bao giờ bị lỗi và liền mạch mọi góc.

                                double d_ft = SelectedStirrupRebar.DiameterMm / 304.8;
                                double overlap_ft = 10 * d_ft;
                                double shift_ft = 5 * d_ft;

                                double leftEdgeLength = p1.DistanceTo(p4);
                                if (overlap_ft + shift_ft > leftEdgeLength * 0.8)
                                {
                                    overlap_ft = leftEdgeLength * 0.5;
                                    shift_ft = leftEdgeLength * 0.2;
                                }

                                XYZ dirY = (p4 - p1).Normalize();

                                // ĐIỂM NỐI CHỒNG TRÊN CẠNH TRÁI (Nằm hoàn toàn trên mặt phẳng Z=0)
                                XYZ lapStart = p1 + dirY * (shift_ft + overlap_ft); // Đầu bắt đầu (nằm cao hơn)
                                XYZ lapEnd = p1 + dirY * shift_ft;                  // Đầu kết thúc (nằm thấp hơn)

                                // VẼ ĐÚNG 1 VÒNG HÌNH CHỮ NHẬT DUY NHẤT
                                List<Curve> singleProfile = new List<Curve> {
                                    Line.CreateBound(lapStart, p4), // Cạnh trái (nửa trên)
                                    Line.CreateBound(p4, p3),       // Cạnh trên -> LIỀN MẠCH
                                    Line.CreateBound(p3, p2),       // Cạnh phải -> LIỀN MẠCH
                                    Line.CreateBound(p2, p1),       // Cạnh đáy -> LIỀN MẠCH
                                    Line.CreateBound(p1, lapEnd)    // Cạnh trái (nửa dưới) gác thẳng lên lapStart
                                };

                                // Gọi 1 lần duy nhất! Mọi góc tự bo tròn, ra chuẩn 1 thanh đai.
                                ProcessProfileZones("Đai chính Nối chồng", singleProfile, stirrupBarType, RebarStyle.StirrupTie, null, null, RebarHookOrientation.Left, RebarHookOrientation.Left, false);
                            }
                            else if (mainTieItem == 3)
                            {
                                // ITEM 3: Đai chữ nhật chạm nhau tại 1 điểm (Hàn giáp mối)
                                double lap_mm = Math.Max(30 * dMain_mm, 300);
                                double lap_ft = lap_mm / 304.8;

                                // BÍ QUYẾT 1: Chặn không cho đoạn nối đâm xuyên ra ngoài nếu cột quá nhỏ
                                if (lap_ft > faceWidth * 0.8) lap_ft = faceWidth * 0.8;

                                XYZ sLap = new XYZ(p1.X + lap_ft, p1.Y, 0);
                                mainProfile = new List<Curve> { Line.CreateBound(sLap, p2), Line.CreateBound(p2, p3), Line.CreateBound(p3, p4), Line.CreateBound(p4, p1), Line.CreateBound(p1, sLap) };

                                // BÍ QUYẾT 2: Ép dùng StirrupTie để Revit tự động cuộn tròn khép kín, không bị văng râu thẳng ra ngoài
                                mainStyle = RebarStyle.StirrupTie;
                                mainStartHook = null;
                                mainEndHook = null;
                            }
                            else if (mainTieItem == 4)
                            {
                                // ITEM 4: Đai chữ nhật khép kín, móc 135 và 90
                                mainProfile = new List<Curve> { Line.CreateBound(p1, p2), Line.CreateBound(p2, p3), Line.CreateBound(p3, p4), Line.CreateBound(p4, p1) };
                                mainStyle = RebarStyle.StirrupTie;
                                mainStartHook = hook135;
                                mainEndHook = hook90;
                            }
                            else if (mainTieItem == 5 || mainTieItem == 6)
                            {
                                // BẢN CHUẨN CUỐI CÙNG: 1 THANH DUY NHẤT - ĐƯỜNG XOẮN ỐC TÀNG HÌNH (SPIRAL)

                                double d_ft = SelectedStirrupRebar.DiameterMm / 304.8;
                                double lap15_ft = 15.0 * d_ft;
                                if (lap15_ft > faceHeight * 0.8) lap15_ft = faceHeight * 0.8;
                                if (lap15_ft > faceWidth * 0.8) lap15_ft = faceWidth * 0.8;

                                // BÍ QUYẾT: Để vẽ 1 thanh đai vươn 15d đè lên nhau mà KHÔNG BỊ LỖI TRÙNG NÉT,
                                // chúng ta phải offset lớp trong vào 1.5mm.
                                double gap = 0.005; // Khoảng cách 1.5mm (Vô hình trên 3D)

                                XYZ S = new XYZ(p4.X + lap15_ft, p4.Y, 0); // Đầu Start vươn phương X (nằm ngoài)
                                XYZ E_in = new XYZ(p4.X + gap, p4.Y - lap15_ft, 0); // Đầu End vươn phương Y (nằm trong, thụt 1.5mm)

                                // Vẽ 1 đường chạy liên tục không đứt đoạn (1 THANH DUY NHẤT)
                                List<Curve> singleProfile = new List<Curve> {
                                    Line.CreateBound(S, p4), // 1. Vươn 15d phương X chạy về góc p4
                                    Line.CreateBound(p4, p1), // 2. Cạnh trái
                                    Line.CreateBound(p1, p2), // 3. Cạnh đáy (LIỀN 1 MẠCH TUYỆT ĐỐI, KHÔNG VẾT CẮT!)
                                    
                                    // 4. Cạnh phải chạy lên (nhưng dừng sớm 1.5mm để chuẩn bị thụt vào trong)
                                    Line.CreateBound(p2, new XYZ(p3.X, p3.Y - gap, 0)), 
                                    
                                    // 5. Cạnh trên chạy về p4 (thụt vào trong 1.5mm để né đường số 1)
                                    Line.CreateBound(new XYZ(p3.X, p3.Y - gap, 0), new XYZ(p4.X + gap, p4.Y - gap, 0)),
                                    
                                    // 6. Vươn 15d phương Y cắm xuống (thụt vào trong 1.5mm để né đường số 2)
                                    Line.CreateBound(new XYZ(p4.X + gap, p4.Y - gap, 0), E_in)
                                };

                                RebarHookType hook = (mainTieItem == 5) ? standardHook90 : standardHook135;

                                // GỌI ĐÚNG 1 LẦN -> Ra đúng 1 thanh thép trong thống kê!
                                // Dùng RebarStyle.Standard để giữ nguyên hình dáng 15d vươn thẳng rồi mới móc.
                                ProcessProfileZones("Đai chính góc p4", singleProfile, stirrupBarType, RebarStyle.Standard, hook, hook, RebarHookOrientation.Left, RebarHookOrientation.Left, false);
                            }
                            // TIẾN HÀNH VẼ ĐAI CHÍNH DỰA TRÊN CẤU HÌNH ĐÃ CHỌN
                            ProcessProfileZones("Đai chính", mainProfile, stirrupBarType, mainStyle, mainStartHook, mainEndHook, RebarHookOrientation.Left, RebarHookOrientation.Left, false);

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
                                        List<Curve> profileU;

                                        if (isHorizontal)
                                        {
                                            // Đai chữ U mở ở cạnh TRÊN (Đi ngược chiều kim đồng hồ: c4 -> c1 -> c2 -> c3)
                                            profileU = new List<Curve> {
                                        Line.CreateBound(c4, c1), // Cạnh trái
                                        Line.CreateBound(c1, c2), // Cạnh dưới
                                        Line.CreateBound(c2, c3)  // Cạnh phải
                                    };
                                        }
                                        else
                                        {
                                            // Đai chữ U mở ở cạnh PHẢI (Đi ngược chiều kim đồng hồ: c3 -> c4 -> c1 -> c2)
                                            profileU = new List<Curve> {
                                        Line.CreateBound(c3, c4), // Cạnh trên
                                        Line.CreateBound(c4, c1), // Cạnh trái
                                        Line.CreateBound(c1, c2)  // Cạnh dưới
                                    };
                                        }

                                        // CHÌA KHÓA: Đổi thành RebarStyle.Standard và standardHook135 vì đây là đai chữ U (hở)
                                        ProcessProfileZones("Item 6", profileU, tieBarType, RebarStyle.Standard, standardHook135, standardHook135, RebarHookOrientation.Left, RebarHookOrientation.Left, true);
                                    }
                                    else if (tieId == 7)
                                    {
                                        // THANH L1: Nằm ở cao độ chuẩn.
                                        List<Curve> profileL1 = new List<Curve> { Line.CreateBound(c4, c3), Line.CreateBound(c3, c2) };

                                        // LOGIC CHUẨN CỦA BẠN: Dịch chuyển Z đúng bằng 1 đường kính cốt thép
                                        double dZ = SelectedTieRebar.DiameterMm / 304.8;
                                        XYZ offsetZ = new XYZ(0, 0, dZ);

                                        // THANH L2: Nhấc lên bằng đúng 1 đường kính.
                                        List<Curve> profileL2 = new List<Curve> {
                                    Line.CreateBound(c2 + offsetZ, c1 + offsetZ),
                                    Line.CreateBound(c1 + offsetZ, c4 + offsetZ)
                                };

                                        // SỬA LỖI CHIỀU MÓC: Đổi toàn bộ 'Left' thành 'Right' để ép móc quặp vào trong
                                        ProcessProfileZones("Item 7 L1", profileL1, tieBarType, RebarStyle.StirrupTie, hook90, hook135, RebarHookOrientation.Right, RebarHookOrientation.Right, true);
                                        ProcessProfileZones("Item 7 L2", profileL2, tieBarType, RebarStyle.StirrupTie, hook90, hook135, RebarHookOrientation.Right, RebarHookOrientation.Right, true);
                                    }
                                    else if (tieId == 8)
                                    {
                                        // THANH L1: Góc Top-Left (c4) -> Top-Right (c3) -> Bottom-Right (c2)
                                        List<Curve> profileL1 = new List<Curve> { Line.CreateBound(c4, c3), Line.CreateBound(c3, c2) };

                                        // Dịch chuyển Z đúng bằng 1 đường kính cốt thép để 2 thanh nằm sát mép nhau
                                        double dZ = SelectedTieRebar.DiameterMm / 304.8;
                                        XYZ offsetZ = new XYZ(0, 0, dZ);

                                        // THANH L2: Nhấc lên bằng đúng 1 đường kính. Góc Bottom-Right (c2) -> Bottom-Left (c1) -> Top-Left (c4)
                                        List<Curve> profileL2 = new List<Curve> {
                                    Line.CreateBound(c2 + offsetZ, c1 + offsetZ),
                                    Line.CreateBound(c1 + offsetZ, c4 + offsetZ)
                                };

                                        // VẼ BẰNG STIRRUP TIE: Gán móc 180 và 90. 
                                        // Dùng hướng 'Right' để ép toàn bộ râu thép quặp ôm sát vào lõi cột!
                                        ProcessProfileZones("Item 8 L1", profileL1, tieBarType, RebarStyle.StirrupTie, hook180, hook90, RebarHookOrientation.Right, RebarHookOrientation.Right, true);
                                        ProcessProfileZones("Item 8 L2", profileL2, tieBarType, RebarStyle.StirrupTie, hook180, hook90, RebarHookOrientation.Right, RebarHookOrientation.Right, true);
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

            // 1. Lưu các thông số cơ bản
            item.SavedCx = this.Cx;
            item.SavedCy = this.Cy;
            item.SavedSpacingDense = this.SpacingDense;
            item.SavedSpacingSparse = this.SpacingSparse;

            item.SavedMainRebar = this.SelectedMainRebar;
            item.SavedStirrupRebar = this.SelectedStirrupRebar;
            item.SavedTieRebar = this.SelectedTieRebar;
            item.SavedStirrupLayout = this.SelectedStirrupLayout;
            item.SavedMainTieType = this.SelectedMainTieType;

            // ==========================================================
            // 2. LƯU TRẠNG THÁI ĐAI MÓC CỦA TỪNG CHẤM ĐỎ TẠI ĐÂY
            // ==========================================================
            if (item.SavedDotTies == null) item.SavedDotTies = new List<int>();
            item.SavedDotTies.Clear();
            foreach (var dot in RebarDots)
            {
                item.SavedDotTies.Add(dot.TieType);
            }

            // ==========================================================
            if (item.SavedCustomClosedTieIndices == null) item.SavedCustomClosedTieIndices = new List<Tuple<int, int, int>>();
            item.SavedCustomClosedTieIndices.Clear();

            var rebarList = RebarDots.ToList();
            HashSet<string> savedKeys = new HashSet<string>();

            // Ưu tiên quét list hiển thị (có chứa ID đai ở Item3)
            if (AdvancedClosedTies != null)
            {
                foreach (var tie in AdvancedClosedTies)
                {
                    int index1 = rebarList.IndexOf(tie.Item1);
                    if (index1 < 0) index1 = rebarList.FindIndex(d => Math.Abs(d.X - tie.Item1.X) < 5 && Math.Abs(d.Y - tie.Item1.Y) < 5);

                    int index2 = rebarList.IndexOf(tie.Item2);
                    if (index2 < 0) index2 = rebarList.FindIndex(d => Math.Abs(d.X - tie.Item2.X) < 5 && Math.Abs(d.Y - tie.Item2.Y) < 5);

                    if (index1 >= 0 && index2 >= 0)
                    {
                        string key = $"{index1}_{index2}";
                        if (!savedKeys.Contains(key))
                        {
                            // LƯU THÊM tie.Item3 LÀ ID CỦA LOẠI ĐAI BỔ SUNG
                            item.SavedCustomClosedTieIndices.Add(new Tuple<int, int, int>(index1, index2, tie.Item3));
                            savedKeys.Add(key);
                        }
                    }
                }
            }

            // Dự phòng list _customClosedTies (vì list này ko có ID đai, nên ta mặc định gán số 1 - Đai chữ nhật)
            if (_customClosedTies != null)
            {
                foreach (var tie in _customClosedTies)
                {
                    int index1 = rebarList.IndexOf(tie.Item1);
                    if (index1 < 0) index1 = rebarList.FindIndex(d => Math.Abs(d.X - tie.Item1.X) < 5 && Math.Abs(d.Y - tie.Item1.Y) < 5);

                    int index2 = rebarList.IndexOf(tie.Item2);
                    if (index2 < 0) index2 = rebarList.FindIndex(d => Math.Abs(d.X - tie.Item2.X) < 5 && Math.Abs(d.Y - tie.Item2.Y) < 5);

                    if (index1 >= 0 && index2 >= 0)
                    {
                        string key1 = $"{index1}_{index2}";
                        string key2 = $"{index2}_{index1}";

                        if (!savedKeys.Contains(key1) && !savedKeys.Contains(key2))
                        {
                            // Gán mặc định loại 1 vì list cũ ko lưu kiểu
                            item.SavedCustomClosedTieIndices.Add(new Tuple<int, int, int>(index1, index2, 1));
                            savedKeys.Add(key1);
                        }
                    }
                }
            }
        }

        private void LoadDataForSelectedColumn(ColumnPreviewItem item)
        {
            if (item == null) return;

            // 1. BẬT CỜ KHÓA: Báo cho ViewModel biết đang thao tác gán dữ liệu, đừng tự động tính toán hay lưu đè
            _isLoadingData = true;

            // 2. XỬ LÝ DỮ LIỆU NGUỒN
            if (!item.IsDataLoaded)
            {
                ExtractExistingRebarFromRevit(item);
                item.IsDataLoaded = true;
            }

            // 3. ĐỔ DỮ LIỆU TỪ ITEM RA GIAO DIỆN (Đã dọn dẹp các dòng lặp thừa)
            this.Cx = item.SavedCx > 0 ? item.SavedCx : 2;
            this.Cy = item.SavedCy > 0 ? item.SavedCy : 2;
            this.SpacingDense = !string.IsNullOrEmpty(item.SavedSpacingDense) ? item.SavedSpacingDense : "100";
            this.SpacingSparse = !string.IsNullOrEmpty(item.SavedSpacingSparse) ? item.SavedSpacingSparse : "200";

            if (item.SavedMainRebar != null) this.SelectedMainRebar = item.SavedMainRebar;
            if (item.SavedStirrupRebar != null) this.SelectedStirrupRebar = item.SavedStirrupRebar;
            if (item.SavedTieRebar != null) this.SelectedTieRebar = item.SavedTieRebar;
            if (item.SavedStirrupLayout != null) this.SelectedStirrupLayout = item.SavedStirrupLayout;

            // (Khôi phục Đai chính - Nhớ dòng này từ lần fix trước nhé)
            if (item.SavedMainTieType != null) this.SelectedMainTieType = item.SavedMainTieType;

            // ===================================================================================
            // 4. BƯỚC QUYẾT ĐỊNH: TÍNH TOÁN LẠI LƯỚI THÉP (SỐ CHẤM ĐỎ) TRƯỚC TIÊN!
            // Phải tạo xong khung thép của tầng này rồi mới có chỗ mà gắn đai vào.
            // ===================================================================================
            RecalculateRebarInfo();
            UpdateRebarDiagram();

            // ===================================================================================
            // 5. XÓA SẠCH "TÀN DƯ" ĐAI CỦA TẦNG CŨ (DIỆT TẬN GỐC BÓNG MA TRÊN CANVAS)
            // ===================================================================================
            if (_customClosedTies == null) _customClosedTies = new List<Tuple<RebarDot, RebarDot>>();
            _customClosedTies.Clear();

            if (AdvancedClosedTies == null) AdvancedClosedTies = new List<Tuple<RebarDot, RebarDot, int>>();
            AdvancedClosedTies.Clear();

            // ===================================================================================
            // 6. PHỤC HỒI ĐAI TỪ DATA ĐÃ LƯU LÊN LƯỚI THÉP MỚI
            // ===================================================================================
            // A. Phục hồi Đai móc (TieType cho từng chấm đỏ)
            if (item.SavedDotTies != null && RebarDots != null)
            {
                for (int i = 0; i < RebarDots.Count && i < item.SavedDotTies.Count; i++)
                {
                    RebarDots[i].TieType = item.SavedDotTies[i];
                }
            }

            // B. Phục hồi Đai bổ sung khép kín (Đã nâng cấp)
            if (item.SavedCustomClosedTieIndices != null && RebarDots != null)
            {
                foreach (var indices in item.SavedCustomClosedTieIndices)
                {
                    if (indices.Item1 < RebarDots.Count && indices.Item2 < RebarDots.Count)
                    {
                        var dot1 = RebarDots[indices.Item1];
                        var dot2 = RebarDots[indices.Item2];

                        _customClosedTies.Add(new Tuple<RebarDot, RebarDot>(dot1, dot2));

                        // ĐỔI SỐ 1 THÀNH indices.Item3 ĐỂ NÓ VẼ ĐÚNG LOẠI ĐAI BẠN ĐÃ CHỌN
                        AdvancedClosedTies.Add(new Tuple<RebarDot, RebarDot, int>(dot1, dot2, indices.Item3));
                    }
                }
            }

            // ===================================================================================
            // 7. RA LỆNH VẼ LẠI GIAO DIỆN
            // ===================================================================================
            RefreshInternalTies();
            GenerateColumnPreview();
            string savedLayoutName = item.SavedStirrupLayout?.Name ?? "";
            UpdateStirrupGraphic(item, savedLayoutName);
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
            item.SavedMainTieType = this.SelectedMainTieType;
            item.IsDataLoaded = true;
        }

        // Hàm Xóa sạch thép cũ của cột để Revit không sinh ra thanh thép trùng
        private void ClearExistingRebars(FamilyInstance column)
        {
            if (column == null || !column.IsValidObject) return;
            try
            {
                // Dùng FilteredElementCollector quét toàn bộ thép trong dự án 
                // và lọc ra đúng những thanh đang nhận Cột này làm vật chủ (Host)
                var rebarsInColumn = new FilteredElementCollector(column.Document)
                    .OfClass(typeof(Rebar))
                    .Cast<Rebar>()
                    .Where(r => r.GetHostId() == column.Id)
                    .ToList();

                if (rebarsInColumn.Any())
                {
                    foreach (var r in rebarsInColumn)
                    {
                        if (r != null && r.IsValidObject)
                        {
                            column.Document.Delete(r.Id);
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