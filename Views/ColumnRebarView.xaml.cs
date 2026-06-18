using ColumnRebar.ViewModels;

namespace ColumnRebar.Views
{
    public sealed partial class ColumnRebarView
    {
        public ColumnRebarView(ColumnRebarViewModel viewModel)
        {
            DataContext = viewModel;
            InitializeComponent();
        }

        private void ComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {

        }
    }
}