using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
namespace ColumnRebar.Models
{
    public class StirrupLayoutOption
    {
        public string Name { get; set; } // Ví dụ: "L1, L2, L1"
        public string IconPath { get; set; } // Đường dẫn ảnh icon nhỏ trong ComboBox
        public string DiagramPath { get; set; } // Đường dẫn ảnh to hiển thị ở cột bên trái
    }
}