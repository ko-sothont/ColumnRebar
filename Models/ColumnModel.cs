using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
namespace ColumnRebar.Models
{
    public partial class RebarDot : ObservableObject
    {
        [ObservableProperty] private double _x;
        [ObservableProperty] private double _y;
        [ObservableProperty] private double _size;
        public double HalfSize => -Size / 2;

        public bool IsCorner { get; set; }
        public double OppositeX { get; set; }
        public double OppositeY { get; set; }

        [ObservableProperty] private int _tieType; // 0 = Không có đai, 1 = Đai móc 135

        // MỚI: Biến lưu trạng thái xem chấm đỏ có đang được chọn ở Click 1 hay không
        [ObservableProperty] private bool _isSelected;
    }
    public class TieStyleOption
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public bool IsRectangular { get; set; } // False = Click 1 điểm (như Item 1), True = Click 2 điểm chéo nhau (Item 2-8)
    }
    public class RebarLine
    {
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
        public double Thickness { get; set; }
        public string PathData { get; set; }
    }
    public class MainTieType
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }
    public class RebarTypeOption
    {
        public string Name { get; set; }
        public double DiameterMm { get; set; } // Đường kính thực tế (đã đổi ra mm)
        public ElementId Id { get; set; }// Bạn có thể thêm ElementId vào đây nếu sau này cần dùng để tạo thép 3D
    }

    public partial class ColumnPreviewItem : ObservableObject
    {
        // --- CÁC THUỘC TÍNH BẠN ĐÃ CÓ ---
        public string LevelName { get; set; }
        public string DimensionText { get; set; }
        public string RebarInfoText { get; set; }
        public double UIColumnHeight { get; set; }
        public double UIColumnWidth { get; set; }
        public double UIBeamDepth { get; set; }
        public ObservableCollection<double> StirrupPositions { get; set; }
         
        // ==========================================================
        // --- CÁC THUỘC TÍNH CẦN THÊM CHO PANEL HIỆU CHỈNH BÊN PHẢI ---
        // ==========================================================

        // 1. Số lượng thép chính
        [ObservableProperty]
        private int _nx = 2; // Số lượng thép cạnh Cx

        [ObservableProperty]
        private int _ny = 2; // Số lượng thép cạnh Cy

        // 2. Dữ liệu vẽ mặt cắt (khu vực có chấm đỏ và viền thép đai)
        public ObservableCollection<RebarDot> CrossSectionDots { get; set; } = new ObservableCollection<RebarDot>();
        public ObservableCollection<RebarLine> CrossSectionTies { get; set; } = new ObservableCollection<RebarLine>();

        // 3. Lựa chọn đường kính thép (Binding từ ComboBox)
        [ObservableProperty]
        private RebarTypeOption _selectedMainRebar;

        [ObservableProperty]
        private RebarTypeOption _selectedStirrupRebar;

        [ObservableProperty]
        private RebarTypeOption _selectedTieRebar;

        // 4. Thông số lớp bảo vệ
        [ObservableProperty]
        private double _topCover = 25; // Bê tông bảo vệ trên

        [ObservableProperty]
        private double _otherCover = 25; // Bê tông bảo vệ khác

        // 5. Thông số khoảng cách đai
        [ObservableProperty]
        private double _spacingS1 = 200; // Khoảng cách đai dày

        [ObservableProperty]
        private double _spacingS2 = 500; // Khoảng cách đai thưa

        // 6. Kiểu đai (Đai móc / Đai lồng kín)
        [ObservableProperty]
        private bool _isClosedTie = false; // true = đai lồng kín, false = đai móc
        public ElementId ColumnId { get; set; }
        // Thêm vào class ColumnPreviewItem của bạn
        public bool IsDataLoaded { get; set; } = false;

        // Lưu các thông số cơ bản
        public int SavedCx { get; set; }
        public int SavedCy { get; set; }
        public string SavedSpacingDense { get; set; }
        public string SavedSpacingSparse { get; set; }

        // Lưu các object thép được chọn
        public RebarTypeOption SavedMainRebar { get; set; }
        public RebarTypeOption SavedStirrupRebar { get; set; }
        public RebarTypeOption SavedTieRebar { get; set; }
        public StirrupLayoutOption SavedStirrupLayout { get; set; }

        // Lưu trạng thái vẽ đai phụ (Cực kỳ quan trọng để không mất hình đai khi chuyển tầng)
        public List<int> SavedDotTies { get; set; } = new List<int>();
        public List<Tuple<int, int>> SavedCustomClosedTieIndices { get; set; } = new List<Tuple<int, int>>();
        public List<Tuple<int, int, int>> SavedAdvancedClosedTieIndices { get; set; } = new List<Tuple<int, int, int>>();
    }
    public class RebarHookOption
    {
        public string Name { get; set; }
        public ElementId Id { get; set; }
    }
    public enum StirrupRangeMode
    {
        ToBeamSlabBottom,   // Rải tới đáy dầm/sàn (giới hạn từ đầu dầm tường dưới đến đáy dầm tầng trên)
        ToMaxElevation      // Rải tới cao độ lớn nhất (chạy xuyên vào trong dầm - đai kháng chấn)
    }

    public enum SeismicArrangementMode
    {
        BySpacing,          // Bố trí theo khoảng cách
        ByQuantity          // Bố trí theo số lượng
    }

    public enum HookType
    {
        Hook_90_Degrees,    // Móc 90 độ
        Hook_135_Degrees,   // Móc 135 độ
        Hook_180_Degrees    // Móc 180 độ
    }
}
