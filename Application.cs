using ColumnRebar.Commands;
using Nice3point.Revit.Toolkit.External;

namespace ColumnRebar
{
    /// <summary>
    ///     Application entry point
    /// </summary>
    [UsedImplicitly]
    public class Application : ExternalApplication
    {
        public override void OnStartup()
        {
            CreateRibbon();
        }

        private void CreateRibbon()
        {
            var panel = Application.CreatePanel("Commands", "ColumnRebar");

            panel.AddPushButton<StartupCommand>("Execute")
                .SetImage("/ColumnRebar;component/Resources/Icons/RibbonIcon16.png")
                .SetLargeImage("/ColumnRebar;component/Resources/Icons/RibbonIcon32.png");
        }
    }
}