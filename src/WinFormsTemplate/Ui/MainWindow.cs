using GetText.WindowsForms;

namespace Oire.WinFormsTemplate.Ui;

public partial class MainWindow: Form {
    public MainWindow() {
        InitializeComponent();

        // Walks the control tree and translates every text property through the gettext
        // catalog. Designer-set strings are therefore written in English and translated here;
        // strings built at run time go through _() instead.
        Localizer.Localize(this, Utils.Localization.Catalog);
    }
}
