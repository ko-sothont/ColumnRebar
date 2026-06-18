using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using ColumnRebar.ViewModels;
using ColumnRebar.Views;
using System.Diagnostics; // Cần thêm thư viện này
using System.Windows.Interop; // Cần thêm thư viện này

namespace ColumnRebar.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class StartupCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // 1. Yêu cầu chọn cột
                IList<Reference> selectedRefs = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new ColumnSelectionFilter(),
                    "Vui lòng chọn các cột tại cùng một vị trí (từ dưới lên trên)...");

                if (selectedRefs.Count == 0) return Result.Cancelled;

                // 2. LẤY TỌA ĐỘ Z AN TOÀN HƠN (Dùng BoundingBox thay vì LocationPoint)
                List<FamilyInstance> selectedColumns = selectedRefs
                    .Select(r => doc.GetElement(r) as FamilyInstance)
                    .OrderBy(c =>
                    {
                        BoundingBoxXYZ bbox = c.get_BoundingBox(null);
                        return bbox != null ? bbox.Min.Z : 0;
                    })
                    .ToList();

                // 3. Khởi tạo ViewModel
                var viewModel = new ColumnRebarViewModel(doc, selectedColumns);

                // 4. KHỞI TẠO VÀ ÉP WINDOW OWNER CHO CỬA SỔ WPF
                var view = new ColumnRebarView(viewModel);

                WindowInteropHelper helper = new WindowInteropHelper(view);
                helper.Owner = Process.GetCurrentProcess().MainWindowHandle; // Trói chặt Window vào Revit

                view.ShowDialog();

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (System.Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    public class ColumnSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            if (elem.Category == null) return false;
            return elem.Category.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralColumns;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}